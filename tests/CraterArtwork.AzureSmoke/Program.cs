extern alias CraterAzureIdentity;
using System.Collections.Concurrent;
using System.Diagnostics.Tracing;
using System.Security.Cryptography;
using System.Text.Json;
using Azure;
using Azure.Core.Pipeline;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using FlyerMonkey.Api.Services;
using FlyerMonkey.Shared.Model;
using FlyerMonkey.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Identity = CraterAzureIdentity::Azure.Identity;

// Not part of the solution, offline checks or quality gate. No live access without the exact opt-in.
const string fixtureBase64 = "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAMCAgMCAgMDAwMEAwMEBQgFBQQEBQoHBwYIDAoMDAsKCwsNDhIQDQ4RDgsLEBYQERMUFRUVDA8XGBYUGBIUFRT/2wBDAQMEBAUEBQkFBQkUDQsNFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBT/wAARCAABAAEDASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwD8qqKKKAP/2Q==";
if (args.Length != 1 || args[0] is not ("--offline-preflight" or "--live-flyermonkey-crater-artwork"))
{
    Console.WriteLine("NOT RUN: explicit --live-flyermonkey-crater-artwork opt-in required (writes two fixture blobs). Offline validation: --offline-preflight.");
    return 2;
}
var jpeg = Convert.FromBase64String(fixtureBase64);
if (jpeg.Length > 2048 || jpeg[0] != 255 || jpeg[1] != 216 || jpeg[^2] != 255 || jpeg[^1] != 217)
{
    Console.Error.WriteLine("FAIL: embedded JPEG fixture is invalid.");
    return 1;
}
if (args[0] == "--offline-preflight")
{
    Console.WriteLine($"PASS offline preflight: embedded 1x1 JPEG, {jpeg.Length} bytes; no network or credentials used.");
    return 0;
}
if (Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") != "Development" ||
    Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") is { Length: > 0 } environment && environment != "Development")
{
    Console.Error.WriteLine("NOT RUN: this live harness requires process-local DOTNET_ENVIRONMENT=Development and no conflicting ASPNETCORE_ENVIRONMENT.");
    return 2;
}

var artworkId = Guid.NewGuid();
var requestId = Guid.NewGuid();
var imageName = $"artwork/{artworkId:N}/artwork.jpg";
var manifestName = $"artwork/{artworkId:N}/metadata.json";
Console.WriteLine("LIVE target: https://flyermonkey.blob.core.windows.net/crater-artwork (existing private container only)");
Console.WriteLine($"Request ID: {requestId}");
Console.WriteLine($"Planned JPEG: {imageName}");
Console.WriteLine($"Planned metadata: {manifestName}");
Console.WriteLine("Credentials allowed: VisualStudioCredential, AzureCliCredential, AzurePowerShellCredential, AzureDeveloperCliCredential (existing sign-in only).");
var stage = "credential/container preflight";
var saveConfirmed = false;
var visible = false;
var replaySafe = false;
using var identityEvents = new SanitizedIdentityEvents();
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
try
{
    var credential = new Identity.DefaultAzureCredential(new Identity.DefaultAzureCredentialOptions
    {
        ExcludeEnvironmentCredential = true,
        ExcludeWorkloadIdentityCredential = true,
        ExcludeManagedIdentityCredential = true,
        ExcludeSharedTokenCacheCredential = true,
        ExcludeVisualStudioCodeCredential = true,
        ExcludeInteractiveBrowserCredential = true,
        ExcludeVisualStudioCredential = false,
        ExcludeAzureCliCredential = false,
        ExcludeAzurePowerShellCredential = false,
        ExcludeAzureDeveloperCliCredential = false
    });
    using var transport = new StorageGuard(imageName, manifestName);
    using var http = new HttpClient(transport);
    var options = new BlobClientOptions { Transport = new HttpClientTransport(http) };
    options.Retry.MaxRetries = 0; // No automatic retries of this live experiment.
    options.Retry.NetworkTimeout = TimeSpan.FromSeconds(30);
    options.Diagnostics.IsLoggingEnabled = false;
    options.Diagnostics.IsLoggingContentEnabled = false;
    BlobContainerClient Container() => new(new Uri("https://flyermonkey.blob.core.windows.net/crater-artwork"), credential, options);
    var container = Container();
    var properties = await container.GetPropertiesAsync(cancellationToken: timeout.Token);
    Require(properties.Value.PublicAccess == PublicAccessType.None, "container is private");
    // Verify only the fresh candidate paths before writing. Never inspect product-images or local files.
    Require(!(await container.GetBlobClient(imageName).ExistsAsync(timeout.Token)).Value &&
        !(await container.GetBlobClient(manifestName).ExistsAsync(timeout.Token)).Value, "fresh blob names are unused");
    var hash = Convert.ToHexString(SHA256.HashData(jpeg));
    var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { requestId, artworkId, hash })));
    // Reserved fixture association only; no SQL connection or catalogue mutation.
    var artwork = new CraterArtwork(artworkId, requestId, int.MaxValue,
        "Storage-only smoke fixture (not a catalogue product)", CraterStyle.Classic, "Crater Classic",
        DateTimeOffset.UtcNow, "image/jpeg", "artwork.jpg", 1, fingerprint);
    stage = "save";
    var writer = new AzureCraterArtworkStore(container, NullLogger<AzureCraterArtworkStore>.Instance);
    await writer.SaveAsync(artwork, jpeg, timeout.Token);
    saveConfirmed = true;
    Console.WriteLine("PASS adapter save completed.");

    stage = "fresh adapter retrieval";
    var freshContainer = Container();
    var reader = new AzureCraterArtworkStore(freshContainer, NullLogger<AzureCraterArtworkStore>.Instance);
    Require(await reader.FindRequestAsync(requestId, timeout.Token) == artwork, "fresh adapter metadata matches every field");
    var retrieved = await reader.ReadAsync(artworkId, timeout.Token);
    Require(retrieved is not null && retrieved.AsSpan().SequenceEqual(jpeg), "fresh adapter returns exact JPEG bytes");
    Require(retrieved is not null && Convert.ToHexString(SHA256.HashData(retrieved)) == hash, "SHA-256 matches");
    Require((await reader.ListAsync(int.MaxValue, timeout.Token)).Any(a => a == artwork), "fixture appears in adapter history");
    var imageBefore = (await freshContainer.GetBlobClient(imageName).GetPropertiesAsync(cancellationToken: timeout.Token)).Value;
    var metadataBefore = (await freshContainer.GetBlobClient(manifestName).GetPropertiesAsync(cancellationToken: timeout.Token)).Value;
    Require(imageBefore.ContentLength == jpeg.Length && imageBefore.ContentType == "image/jpeg" && metadataBefore.ContentType == "application/json",
        "both blobs visible with expected content properties");
    visible = true;
    Console.WriteLine($"SHA-256: {hash}");

    stage = "identical request replay";
    var putsBefore = transport.PutCount;
    await reader.SaveAsync(artwork, jpeg, timeout.Token);
    var imageAfter = (await freshContainer.GetBlobClient(imageName).GetPropertiesAsync(cancellationToken: timeout.Token)).Value;
    var metadataAfter = (await freshContainer.GetBlobClient(manifestName).GetPropertiesAsync(cancellationToken: timeout.Token)).Value;
    Require(imageAfter.ETag == imageBefore.ETag && metadataAfter.ETag == metadataBefore.ETag, "both ETags unchanged on replay");
    Require(imageAfter.LastModified == imageBefore.LastModified && metadataAfter.LastModified == metadataBefore.LastModified && transport.PutCount == putsBefore,
        "duplicate replay issued no PUT and changed neither timestamp");
    Require((await reader.ReadAsync(artworkId, timeout.Token))!.AsSpan().SequenceEqual(jpeg), "JPEG unchanged after replay");
    replaySafe = true;
    Console.WriteLine($"JPEG blob: {imageName}");
    Console.WriteLine($"Metadata blob: {manifestName}");
    Console.WriteLine("PASS live smoke test. Both blobs remain in place; duplicate replay was safe. No image generation, SQL, local artwork, resource/policy changes or deletes.");
    return 0;
}
catch (Exception ex)
{
    // Do not print exception messages, stack traces, HTTP bodies, tokens, identity claims or credential subprocess output.
    var failures = Exceptions(ex).ToArray();
    Console.Error.WriteLine($"FAIL live smoke test at stage: {stage}.");
    if (failures.OfType<RequestFailedException>().FirstOrDefault() is { } storage)
    {
        var knownCodes = new[] { "AuthorizationPermissionMismatch", "AuthorizationFailure", "AuthenticationFailed", "ContainerNotFound", "BlobNotFound", "PublicAccessNotPermitted", "ConditionNotMet", "BlobAlreadyExists" };
        Console.Error.WriteLine($"Storage HTTP status: {storage.Status}; code: {(knownCodes.Contains(storage.ErrorCode) ? storage.ErrorCode : "withheld/unclassified")}.");
    }
    if (failures.Any(e => e is Identity.AuthenticationFailedException or Identity.CredentialUnavailableException))
        Console.Error.WriteLine("Developer authentication unavailable or failed. Verify the existing organisational sign-in/tenant. No account-key fallback or role changes attempted.");
    else if (failures.Any(e => e is OperationCanceledException)) Console.Error.WriteLine("Operation cancelled or exceeded its time budget.");
    else if (failures.Any(e => e is HttpRequestException)) Console.Error.WriteLine("Network/transport operation failed; no networking changes attempted.");
    else if (ex is VerificationFailure verified) Console.Error.WriteLine("Verification failed: " + verified.Check);
    else Console.Error.WriteLine("Operation failed; raw exception details withheld to protect credentials.");
    Console.Error.WriteLine($"Save confirmed: {saveConfirmed}; both blobs verified visible: {visible}; replay verified safe: {replaySafe}.");
    Console.Error.WriteLine("Any partial/committed blobs are left in place. No retry or cleanup will be attempted.");
    return 1;
}
finally { identityEvents.Report(); }

static void Require(bool condition, string check)
{
    if (!condition) throw new VerificationFailure(check);
    Console.WriteLine("PASS " + check + ".");
}
static IEnumerable<Exception> Exceptions(Exception exception)
{
    yield return exception;
    if (exception is AggregateException aggregate)
        foreach (var inner in aggregate.InnerExceptions)
            foreach (var nested in Exceptions(inner)) yield return nested;
    else if (exception.InnerException is { } inner)
        foreach (var nested in Exceptions(inner)) yield return nested;
}
sealed class VerificationFailure(string check) : Exception { public string Check { get; } = check; }

sealed class SanitizedIdentityEvents : EventListener
{
    private readonly ConcurrentDictionary<string, byte> attempted = new();
    private readonly ConcurrentDictionary<string, byte> succeeded = new();
    private static readonly string[] Names = ["VisualStudioCredential", "AzureCliCredential", "AzurePowerShellCredential", "AzureDeveloperCliCredential"];
    protected override void OnEventSourceCreated(EventSource source)
    {
        if (source.Name == "Azure-Identity") EnableEvents(source, EventLevel.Verbose);
    }
    protected override void OnEventWritten(EventWrittenEventArgs data)
    {
        // Extract only whitelisted class names from diagnostics; never emit the diagnostics themselves.
        var text = (data.Message ?? "") + " " + string.Join(" ", data.Payload?.Select(p => p?.ToString()) ?? []);
        foreach (var name in Names)
            if (text.Contains(name, StringComparison.Ordinal))
            {
                attempted.TryAdd(name, 0);
                if ((data.EventName ?? "").Contains("Succeeded", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("credential selected", StringComparison.OrdinalIgnoreCase)) succeeded.TryAdd(name, 0);
            }
    }
    public void Report()
    {
        Console.WriteLine("Developer sources observed in diagnostics: " + (attempted.IsEmpty ? "none recorded" : string.Join(", ", attempted.Keys.Order())));
        Console.WriteLine("Successful/selected developer sources observed: " + (succeeded.IsEmpty ? "none recorded" : string.Join(", ", succeeded.Keys.Order())));
    }
}

sealed class StorageGuard(string image, string metadata) : DelegatingHandler(new HttpClientHandler { AllowAutoRedirect = false })
{
    public int PutCount { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        var uri = request.RequestUri!;
        var fixedTarget = uri.Scheme == "https" && uri.Host == "flyermonkey.blob.core.windows.net" && uri.IsDefaultPort &&
            string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Fragment) &&
            (uri.AbsolutePath == "/crater-artwork" || uri.AbsolutePath.StartsWith("/crater-artwork/artwork/", StringComparison.Ordinal));
        var safeRead = request.Method == HttpMethod.Get || request.Method == HttpMethod.Head;
        var safeWrite = request.Method == HttpMethod.Put && uri.Query.Length == 0 &&
            (uri.AbsolutePath == "/crater-artwork/" + image || uri.AbsolutePath == "/crater-artwork/" + metadata) &&
            request.Headers.IfNoneMatch.Any(etag => etag.Tag == "*");
        if (!fixedTarget || !(safeRead || safeWrite)) throw new InvalidOperationException("Smoke-test storage boundary rejected an operation.");
        if (safeWrite) PutCount++;
        return base.SendAsync(request, token);
    }
}
