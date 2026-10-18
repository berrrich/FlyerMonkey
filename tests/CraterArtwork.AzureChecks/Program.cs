using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Azure.Core.Pipeline;
using Azure.Storage.Blobs;
using FlyerMonkey.Api.Services;
using FlyerMonkey.Shared.Model;
using FlyerMonkey.Shared.Services;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

// No live mode: all SDK traffic terminates in this in-memory handler, with no credentials.
var checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    checks++;
    Console.WriteLine($"PASS: {name}");
}
async Task Fails<T>(Func<Task> work, string name) where T : Exception
{
    try { await work(); }
    catch (T) { Check(true, name); return; }
    throw new Exception("Expected " + typeof(T).Name + ": " + name);
}
string Image(Guid id) => $"artwork/{id:N}/artwork.jpg";
string Manifest(Guid id) => $"artwork/{id:N}/metadata.json";
CraterArtwork Artwork(int productId = 1) => new(Guid.NewGuid(), Guid.NewGuid(), productId, "Asparagus",
    CraterStyle.Classic, "Crater Classic", DateTimeOffset.UtcNow, "image/jpeg", "artwork.jpg", 1, new string('A', 64));

try
{
    using var handler = new BlobHandler();
    using var http = new HttpClient(handler);
    var options = new BlobClientOptions { Transport = new HttpClientTransport(http) };
    options.Retry.MaxRetries = 0;
    var container = new BlobContainerClient(new Uri("https://offline.invalid/crater-artwork"), options);
    AzureCraterArtworkStore Store() => new(container, NullLogger<AzureCraterArtworkStore>.Instance);
    var store = Store();
    var a = Artwork();
    byte[] jpeg = [255, 216, 255, 1, 2, 3];
    await store.SaveAsync(a, jpeg, default);
    Check(handler.Blobs.ContainsKey(Image(a.ArtworkId)) && handler.Blobs.ContainsKey(Manifest(a.ArtworkId)), "Save publishes image then manifest");
    Check(handler.Writes.SequenceEqual(new[] { Image(a.ArtworkId), Manifest(a.ArtworkId) }), "Manifest is written last");
    Check((await Store().ReadAsync(a.ArtworkId, default))!.SequenceEqual(jpeg), "Fresh adapter retrieves identical bytes");
    Check(await Store().FindRequestAsync(a.RequestId, default) == a, "Fresh adapter retrieves complete metadata by request");
    var writes = handler.Writes.Count;
    await store.SaveAsync(a, jpeg, default);
    Check(handler.Writes.Count == writes, "Identical save replay does not write again");
    await Fails<IOException>(() => store.SaveAsync(a with { ArtworkId = Guid.NewGuid() }, jpeg, default), "Conflicting request is rejected");
    await Fails<IOException>(() => store.SaveAsync(a, [255, 216, 255, 99], default), "Committed JPEG cannot be overwritten");
    var second = Artwork() with { CreatedUtc = a.CreatedUtc.AddSeconds(1) };
    var other = Artwork(2);
    await store.SaveAsync(second, jpeg, default);
    await store.SaveAsync(other, jpeg, default);
    Check((await store.ListAsync(1, default)).Select(x => x.ArtworkId).SequenceEqual(new[] { second.ArtworkId, a.ArtworkId }), "History is product-scoped and newest first across list pages");
    Check((await store.ListAsync(2, default)).Single() == other, "Other product history is separate");

    var orphan = Artwork();
    handler.Blobs[Image(orphan.ArtworkId)] = jpeg;
    Check(await store.ReadAsync(orphan.ArtworkId, default) is null, "Uncommitted JPEG is not readable");
    var malformed = Artwork();
    handler.Blobs[Manifest(malformed.ArtworkId)] = Encoding.UTF8.GetBytes("{broken");
    var invalid = Artwork();
    handler.Blobs[Manifest(invalid.ArtworkId)] = JsonSerializer.SerializeToUtf8Bytes(invalid with { Filename = "../secret.jpg" });
    Check((await store.ListAsync(1, default)).Count == 2, "Orphans, malformed and unsafe manifests are excluded");
    handler.Blobs.Remove(Image(a.ArtworkId)); // In-memory fixture only; never deletes files or Azure blobs.
    Check(await store.ReadAsync(a.ArtworkId, default) is null && (await store.ListAsync(1, default)).Count == 1, "Missing image is omitted from retrieval/history");
    Check(await store.FindRequestAsync(a.RequestId, default) == a, "Missing image retains known request identity");
    await Fails<IOException>(() => store.SaveAsync(a, jpeg, default), "Missing committed JPEG is not silently recreated");
    handler.Blobs[Image(a.ArtworkId)] = jpeg;

    var failed = Artwork();
    handler.Failure = (method, name) => method == HttpMethod.Put && name == Image(failed.ArtworkId) ? (500, "InternalError") : null;
    await Fails<IOException>(() => store.SaveAsync(failed, jpeg, default), "JPEG upload failure is explicit");
    Check(!handler.Blobs.ContainsKey(Manifest(failed.ArtworkId)), "JPEG failure publishes no manifest");
    handler.Failure = (method, name) => method == HttpMethod.Put && name == Manifest(failed.ArtworkId) ? (500, "InternalError") : null;
    await Fails<IOException>(() => store.SaveAsync(failed, jpeg, default), "Manifest upload failure is explicit");
    handler.Failure = null;
    handler.Failure = (method, name) => method == HttpMethod.Head && name == Image(a.ArtworkId) ? (404, "ContainerNotFound") : null;
    await Fails<IOException>(async () => { await store.ListAsync(1, default); }, "Container lost during history is not mistaken for missing JPEG");
    handler.Failure = (method, name) => method == HttpMethod.Get && name == Manifest(a.ArtworkId) ? (403, "AuthorizationFailure") : null;
    await Fails<IOException>(async () => { await store.FindRequestAsync(a.RequestId, default); }, "Manifest access denied never becomes missing request");
    handler.Failure = (method, name) => method == HttpMethod.Get && name == Image(a.ArtworkId) ? (500, "InternalError") : null;
    await Fails<IOException>(async () => { await store.ReadAsync(a.ArtworkId, default); }, "JPEG download error is not mistaken for missing image");
    handler.Failure = null;
    Check(handler.Blobs.ContainsKey(Image(failed.ArtworkId)) && await store.ReadAsync(failed.ArtworkId, default) is null, "Interrupted save keeps orphan private and uncommitted");
    await store.SaveAsync(failed, jpeg, default);
    Check(await store.FindRequestAsync(failed.RequestId, default) == failed, "Retry verifies existing orphan bytes before committing");
    var collision = Artwork();
    handler.Blobs[Image(collision.ArtworkId)] = [255, 216, 255, 99];
    await Fails<IOException>(() => store.SaveAsync(collision, jpeg, default), "Conflicting orphan bytes refuse overwrite");

    var ambiguous = Artwork();
    handler.AmbiguousManifest = Manifest(ambiguous.ArtworkId);
    await Fails<IOException>(() => store.SaveAsync(ambiguous, jpeg, default), "Lost manifest response is reported as failure");
    Check(await store.FindRequestAsync(ambiguous.RequestId, default) == ambiguous, "Committed manifest remains discoverable after lost response");
    await store.SaveAsync(ambiguous, jpeg, default);
    Check(handler.Blobs[Image(ambiguous.ArtworkId)].SequenceEqual(jpeg), "Ambiguous-save replay preserves bytes");

    foreach (var status in new[] { 401, 403, 500 })
    {
        handler.Failure = (_, _) => (status, "AuthorizationFailure");
        await Fails<IOException>(async () => { await store.ListAsync(1, default); }, $"HTTP {status} never becomes empty history");
    }
    handler.Failure = (_, _) => (404, "ContainerNotFound");
    await Fails<IOException>(async () => { await store.ReadAsync(a.ArtworkId, default); }, "Missing container is configuration failure, not missing image");
    handler.Failure = null;
    handler.NetworkFailure = true;
    await Fails<IOException>(async () => { await store.FindRequestAsync(a.RequestId, default); }, "Network failure never becomes missing request");
    handler.NetworkFailure = false;
    handler.Public = true;
    await Fails<IOException>(async () => { await store.ListAsync(1, default); }, "Public container is rejected");
    handler.Public = false;
    var wrongContainer = new AzureCraterArtworkStore(new BlobContainerClient(new Uri("https://offline.invalid/product-images"), options),
        NullLogger<AzureCraterArtworkStore>.Instance);
    await Fails<IOException>(async () => { await wrongContainer.ListAsync(1, default); }, "Adapter itself refuses canonical container before traffic");
    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();
    await Fails<OperationCanceledException>(() => store.SaveAsync(Artwork(), jpeg, cancelled.Token), "Cancellation propagates");
    var cancelledSave = Artwork();
    using var midSave = new CancellationTokenSource();
    handler.AfterWrite = name => { if (name == Image(cancelledSave.ArtworkId)) midSave.Cancel(); };
    await Fails<OperationCanceledException>(() => store.SaveAsync(cancelledSave, jpeg, midSave.Token), "Cancellation between image and manifest propagates");
    handler.AfterWrite = null;
    Check(handler.Blobs.ContainsKey(Image(cancelledSave.ArtworkId)) && !handler.Blobs.ContainsKey(Manifest(cancelledSave.ArtworkId)),
        "Cancelled partial save remains uncommitted without deletion");
    await Fails<InvalidDataException>(() => store.SaveAsync(Artwork(), [1, 2, 3], default), "Invalid JPEG is rejected");
    await Fails<InvalidDataException>(() => store.SaveAsync(Artwork() with { ArtworkId = Guid.Empty }, jpeg, default), "Empty artwork ID is rejected");

    var environment = new TestEnvironment { ContentRootPath = Path.Combine(Directory.GetCurrentDirectory(), "FlyerMonkey.Api") };
    var configuration = new ConfigurationManager();
    ICraterArtworkStore Create() => CraterArtworkStoreFactory.Create(configuration, environment, NullLoggerFactory.Instance);
    Check(Create() is LocalCraterArtworkStore, "Missing provider defaults to unchanged local storage");
    configuration["CraterArt:Provider"] = "Local";
    configuration["CraterArt:Azure:ServiceUri"] = "invalid";
    Check(Create() is LocalCraterArtworkStore, "Explicit Local ignores unused Azure settings");
    configuration["CraterArt:Provider"] = "invalid";
    await Fails<InvalidOperationException>(() => Task.FromResult(Create()), "Unknown provider fails without local fallback");
    configuration["CraterArt:Provider"] = "Azure";
    configuration["CraterArt:Azure:Container"] = "crater-artwork";
    foreach (var address in new[] { "invalid", "http://account.blob.core.windows.net", "https://public.example/", "https://account.blob.core.windows.net/path", "https://account.blob.core.windows.net/?sig=secret", "https://user@account.blob.core.windows.net/", "https://account.blob.core.windows.net:444/" })
    {
        configuration["CraterArt:Azure:ServiceUri"] = address;
        await Fails<InvalidOperationException>(() => Task.FromResult(Create()), "Invalid/credential-bearing service URI rejected");
    }
    configuration["CraterArt:Azure:ServiceUri"] = "https://account.blob.core.windows.net/";
    configuration["CraterArt:Azure:Container"] = "product-images";
    await Fails<InvalidOperationException>(() => Task.FromResult(Create()), "Canonical product-images container refused");
    configuration["CraterArt:Azure:Container"] = null;
    await Fails<InvalidOperationException>(() => Task.FromResult(Create()), "Container must be explicitly selected");
    configuration["CraterArt:Azure:Container"] = "crater-artwork";
    Check(Create() is AzureCraterArtworkStore, "Valid explicit Azure selection constructs without contacting Azure");
    foreach (var name in new[] { "Production", "Staging" })
    {
        environment.EnvironmentName = name;
        await Fails<InvalidOperationException>(() => Task.FromResult(Create()), $"Azure selection denied in {name}");
    }
    Check(handler.Requests.All(r => r.Method is "GET" or "HEAD" or "PUT") &&
        handler.Requests.Where(r => r.Method == "PUT").All(r => r.Path.StartsWith("/crater-artwork/artwork/") && r.CreateOnly),
        "Every write is create-only artwork data; no container/policy/delete operations");
    Console.WriteLine($"{checks} offline Azure checks passed. No Azure traffic, credentials, files or generation calls used.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    return 1; // Return the exception and exit status instead of opening a Windows unhandled-exception dialog.
}

sealed class TestEnvironment : IWebHostEnvironment
{
    public string EnvironmentName { get; set; } = "Development";
    public string ApplicationName { get; set; } = "OfflineAzureChecks";
    public string ContentRootPath { get; set; } = "";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public string WebRootPath { get; set; } = "";
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}

sealed class BlobHandler : HttpMessageHandler
{
    public Dictionary<string, byte[]> Blobs { get; } = new();
    public List<string> Writes { get; } = [];
    public List<(string Method, string Path, bool CreateOnly)> Requests { get; } = [];
    public Func<HttpMethod, string, (int Status, string Code)?>? Failure;
    public bool NetworkFailure;
    public bool Public;
    public string? AmbiguousManifest;
    public Action<string>? AfterWrite;
    private const string Date = "Sun, 18 Oct 2026 00:00:00 GMT";

    private static HttpResponseMessage Response(int status, byte[]? bytes = null, string? code = null)
    {
        var response = new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = new ByteArrayContent(bytes ?? [])
        };
        response.Headers.TryAddWithoutValidation("ETag", "\"offline-etag\"");
        response.Headers.TryAddWithoutValidation("x-ms-request-id", "offline-request");
        response.Headers.TryAddWithoutValidation("x-ms-blob-type", "BlockBlob");
        response.Content.Headers.TryAddWithoutValidation("Last-Modified", Date);
        response.Content.Headers.ContentLength = bytes?.Length ?? 0;
        if (code is not null)
        {
            response.Headers.TryAddWithoutValidation("x-ms-error-code", code);
            response.Content = new StringContent($"<Error><Code>{code}</Code><Message>Offline simulated failure</Message></Error>", Encoding.UTF8, "application/xml");
        }
        return response;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var uri = request.RequestUri!;
        if (uri.Host != "offline.invalid" || !uri.AbsolutePath.StartsWith("/crater-artwork"))
            throw new InvalidOperationException("Unexpected destination; live requests are forbidden.");
        var name = uri.AbsolutePath.Length > "/crater-artwork/".Length ? uri.AbsolutePath["/crater-artwork/".Length..] : "";
        var createOnly = request.Headers.TryGetValues("If-None-Match", out var values) && values.Single() == "*";
        Requests.Add((request.Method.Method, uri.AbsolutePath, createOnly));
        if (NetworkFailure) throw new HttpRequestException("Offline simulated network failure");
        if (Failure?.Invoke(request.Method, name) is { } failure) return Response(failure.Status, code: failure.Code);
        if (name == "" && request.Method == HttpMethod.Get && uri.Query.Contains("comp=list"))
        {
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            var names = Blobs.Keys.Where(n => n.StartsWith(query["prefix"] ?? "")).Order().ToArray();
            var offset = int.TryParse(query["marker"], out var marker) ? marker : 0;
            var page = names.Skip(offset).Take(2).ToArray();
            var xml = new XElement("EnumerationResults", new XAttribute("ServiceEndpoint", "https://offline.invalid/"),
                new XAttribute("ContainerName", "crater-artwork"), new XElement("Blobs", page.Select(n =>
                    new XElement("Blob", new XElement("Name", n), new XElement("Properties",
                        new XElement("Last-Modified", Date), new XElement("Etag", "\"offline-etag\""),
                        new XElement("Content-Length", Blobs[n].Length), new XElement("BlobType", "BlockBlob"))))),
                new XElement("NextMarker", offset + page.Length < names.Length ? (offset + page.Length).ToString() : ""));
            return Response(200, Encoding.UTF8.GetBytes(xml.ToString()));
        }
        if (name == "" && request.Method == HttpMethod.Get && uri.Query.Contains("restype=container"))
        {
            var response = Response(200);
            if (Public) response.Headers.TryAddWithoutValidation("x-ms-blob-public-access", "blob");
            return response;
        }
        if (request.Method == HttpMethod.Put && name.StartsWith("artwork/") && string.IsNullOrEmpty(uri.Query))
        {
            if (!createOnly) throw new InvalidOperationException("Unconditional writes forbidden");
            if (Blobs.ContainsKey(name)) return Response(412, code: "ConditionNotMet");
            Blobs[name] = await request.Content!.ReadAsByteArrayAsync(token);
            Writes.Add(name);
            AfterWrite?.Invoke(name);
            if (AmbiguousManifest == name) { AmbiguousManifest = null; return Response(500, code: "InternalError"); }
            return Response(201);
        }
        if (request.Method == HttpMethod.Get || request.Method == HttpMethod.Head)
            return Blobs.TryGetValue(name, out var content) ? Response(200, content) : Response(404, code: "BlobNotFound");
        throw new InvalidOperationException("Unexpected SDK operation: " + request.Method + " " + uri.AbsolutePath);
    }
}
