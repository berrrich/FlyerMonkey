extern alias CraterAzureIdentity;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Core.Pipeline;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using FlyerMonkey.Api.Services;
using FlyerMonkey.Shared.Model;
using FlyerMonkey.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Identity = CraterAzureIdentity::Azure.Identity;

// Separate opt-in live runner: never part of the solution or offline quality gate.
if (args.Length != 1 || args[0] != "--live-approved-product-102")
{
    Console.WriteLine("NOT RUN: --live-approved-product-102 requires explicit approval for one create and one identical replay. No automatic retries or deletes.");
    return 2;
}
var requestId = Guid.Parse("0e135ff0-58b8-4f4d-9365-0ff981599a5b");
var artworkId = Guid.Parse("df6a9c89-85b1-40e4-bbc9-d80103913b6c");
var imageName = $"artwork/{artworkId:N}/artwork.jpg";
var metadataName = $"artwork/{artworkId:N}/metadata.json";
var root = Directory.GetCurrentDirectory();
var apiDirectory = Path.Combine(root, "FlyerMonkey.Api");
var apiDll = Path.Combine(apiDirectory, "bin", "Debug", "net10.0", "FlyerMonkey.Api.dll");
const string baseUrl = "http://localhost:5146/";
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
var payload = JsonSerializer.Serialize(new { requestId, artworkId, productID = 102 }, json);
var posts = 0;
var stage = "local prerequisites";
var created = false;
var replaySafe = false;
var disabled = false;
Process? api = null;
ConcurrentQueue<string> apiLog = new();
Task<string>? apiError = null;
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(90) };
try
{
    Require(File.Exists(apiDll) && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FLYERMONKEY_SQL_CONNECTION")), "API binary and existing SQL startup configuration present");
    foreach (var name in new[] { "AZURE_CLIENT_SECRET", "AZURE_CLIENT_CERTIFICATE_PATH", "AZURE_FEDERATED_TOKEN_FILE", "AZURE_PASSWORD" })
        Require(string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name)), "no nondeveloper credential override: " + name);
    using (var socket = new TcpClient())
    {
        var occupied = false;
        try { await socket.ConnectAsync("localhost", 5146, timeout.Token); occupied = true; }
        catch (SocketException) { }
        Require(!occupied, "isolated localhost:5146 port is free; existing services untouched");
    }

    stage = "read-only Azure destination preflight";
    var credential = new Identity.DefaultAzureCredential(new Identity.DefaultAzureCredentialOptions
    {
        ExcludeEnvironmentCredential = true, ExcludeWorkloadIdentityCredential = true, ExcludeManagedIdentityCredential = true,
        ExcludeSharedTokenCacheCredential = true, ExcludeInteractiveBrowserCredential = true,
#pragma warning disable CS0618 // Explicitly exclude the obsolete VS Code credential; never enable it.
        ExcludeVisualStudioCodeCredential = true,
#pragma warning restore CS0618
        ExcludeVisualStudioCredential = false, ExcludeAzureCliCredential = true,
        ExcludeAzurePowerShellCredential = true, ExcludeAzureDeveloperCliCredential = true
    });
    using var storageHttp = new HttpClient(new ReadOnlyStorageGuard(imageName, metadataName));
    var options = new BlobClientOptions { Transport = new HttpClientTransport(storageHttp) };
    options.Retry.MaxRetries = 0;
    options.Retry.NetworkTimeout = TimeSpan.FromSeconds(30);
    options.Diagnostics.IsLoggingEnabled = false;
    options.Diagnostics.IsLoggingContentEnabled = false;
    var container = new BlobContainerClient(new Uri("https://flyermonkey.blob.core.windows.net/crater-artwork"), credential, options);
    Require((await container.GetPropertiesAsync(cancellationToken: timeout.Token)).Value.PublicAccess == PublicAccessType.None,
        "destination flyermonkey/crater-artwork is private (Visual Studio developer authentication)");
    await RequireAbsent(container, imageName, timeout.Token);
    await RequireAbsent(container, metadataName, timeout.Token);

    stage = "owned Development API and catalogue preflight";
    await StartApi(true);
    Require(api!.StartInfo.Environment["ASPNETCORE_ENVIRONMENT"] == "Development" && api.StartInfo.Environment["DOTNET_ENVIRONMENT"] == "Development" &&
        api.StartInfo.Environment["CraterIt__FixtureImportEnabled"] == "true" && api.StartInfo.Environment["CraterIt__FixtureProductId"] == "102" &&
        api.StartInfo.Environment["CraterArt__Provider"] == "Azure" && api.StartInfo.Environment["CraterArt__Azure__ServiceUri"] == "https://flyermonkey.blob.core.windows.net/" &&
        api.StartInfo.Environment["CraterArt__Azure__Container"] == "crater-artwork", "owned API explicitly configured Development/Azure/fixture-enabled/ProductID 102 only");
    using var productResponse = await http.GetAsync("api/products/102", timeout.Token);
    Require(productResponse.StatusCode == HttpStatusCode.OK, "ProductID 102 exists through normal API product lookup");
    var product = await productResponse.Content.ReadFromJsonAsync<Product>(json, timeout.Token);
    Require(product is { ID: 102 } && !string.IsNullOrWhiteSpace(product.Name), "product identity valid");
    Console.WriteLine("ProductID 102: " + product!.Name.Replace('\r', ' ').Replace('\n', ' '));
    // Recheck immediately before the first POST. Any pre-existing blob aborts without invoking a write.
    await RequireAbsent(container, imageName, timeout.Token);
    await RequireAbsent(container, metadataName, timeout.Token);
    Console.WriteLine("ALL PREREQUISITES PASSED. Sending the one approved create request.");

    stage = "approved fixture-create POST";
    var first = await PostFixture();
    created = true;
    Require(first.RequestId == requestId && first.ArtworkId == artworkId && first.ProductID == 102 &&
        first.ProductName == ("[DEV FIXTURE] " + product.Name)[..Math.Min(1000, ("[DEV FIXTURE] " + product.Name).Length)] &&
        first.PresetName == "Development fixture (not generated)" && first.CreatedUtc.Offset == TimeSpan.Zero &&
        first.ContentType == "image/jpeg" && first.Filename == "artwork.jpg", "create returned expected IDs, product and labelled metadata");
    var imageBefore = (await container.GetBlobClient(imageName).GetPropertiesAsync(cancellationToken: timeout.Token)).Value;
    var metadataBefore = (await container.GetBlobClient(metadataName).GetPropertiesAsync(cancellationToken: timeout.Token)).Value;
    Require(imageBefore.ContentLength == 631 && imageBefore.ContentType == "image/jpeg" && metadataBefore.ContentType == "application/json",
        "both new blobs visible with expected content properties");
    stage = "approved identical replay POST";
    Require(await PostFixture() == first, "identical replay returned the original metadata");
    var imageAfter = (await container.GetBlobClient(imageName).GetPropertiesAsync(cancellationToken: timeout.Token)).Value;
    var metadataAfter = (await container.GetBlobClient(metadataName).GetPropertiesAsync(cancellationToken: timeout.Token)).Value;
    Require(imageBefore.ETag == imageAfter.ETag && metadataBefore.ETag == metadataAfter.ETag &&
        imageBefore.LastModified == imageAfter.LastModified && metadataBefore.LastModified == metadataAfter.LastModified,
        "replay changed neither blob ETag nor timestamp");
    replaySafe = true;
    await VerifyGets(first);

    stage = "API restart with fixture import disabled";
    await StopApi();
    await StartApi(false);
    Require(api!.StartInfo.Environment["CraterIt__FixtureImportEnabled"] == "false" &&
        string.IsNullOrEmpty(api.StartInfo.Environment["CraterIt__FixtureProductId"]), "restarted API has fixture-import flag disabled and product scope cleared");
    // Do not send a third POST: GET confirms this route cannot import via GET, not that its flag is disabled.
    // Disabled process configuration is asserted above; the off-flag guard itself is covered by offline tests.
    using var getWriteRoute = await http.GetAsync("api/images/artwork/development-fixture", timeout.Token);
    Require(getWriteRoute.StatusCode == HttpStatusCode.MethodNotAllowed, "GET cannot invoke fixture import; no extra POST sent");
    await VerifyGets(first);
    disabled = true;
    Require(posts == 2, "exactly one create and one identical replay POST were sent");
    Console.WriteLine($"JPEG blob: {imageName}");
    Console.WriteLine($"Metadata blob: {metadataName}");
    Console.WriteLine("PASS live API fixture test. Save, replay and GET integrity across process restart verified. Both blobs retained. Actual Web UI/Android device NOT tested.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"FAIL at stage: {stage}. POSTs attempted={posts}; create confirmed={created}; replay safe={replaySafe}; disabled-restart confirmed={disabled}.");
    var chain = Chain(ex).ToArray();
    if (chain.OfType<RequestFailedException>().FirstOrDefault() is { } storage)
        Console.Error.WriteLine($"Azure HTTP status={storage.Status}; raw error body/message withheld.");
    else if (chain.Any(e => e is Identity.AuthenticationFailedException or Identity.CredentialUnavailableException))
        Console.Error.WriteLine("VisualStudioCredential unavailable or failed. No credential fallback or permission changes attempted.");
    else if (ex is CheckFailure check) Console.Error.WriteLine("Failed check: " + check.Check);
    else Console.Error.WriteLine("Operation failed or timed out; raw server/credential details withheld.");
    Console.Error.WriteLine("No retry, overwrite or cleanup will be attempted. Any partial/created blobs remain in place.");
    return 1;
}
finally { await StopApi(); }

async Task StartApi(bool enabled)
{
    var info = new ProcessStartInfo("dotnet") { WorkingDirectory = apiDirectory, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
    info.ArgumentList.Add(apiDll);
    info.ArgumentList.Add("--urls"); info.ArgumentList.Add(baseUrl);
    info.ArgumentList.Add("--Logging:LogLevel:Microsoft.Hosting.Lifetime=Information");
    info.Environment["DOTNET_ENVIRONMENT"] = "Development";
    info.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
    info.Environment["CraterIt__Enabled"] = "true";
    info.Environment["CraterArt__Provider"] = "Azure";
    info.Environment["CraterArt__Azure__ServiceUri"] = "https://flyermonkey.blob.core.windows.net/";
    info.Environment["CraterArt__Azure__Container"] = "crater-artwork";
    info.Environment["CraterIt__FixtureImportEnabled"] = enabled ? "true" : "false";
    info.Environment["CraterIt__FixtureProductId"] = enabled ? "102" : "";
    apiLog = new ConcurrentQueue<string>();
    api = new Process { StartInfo = info };
    api.OutputDataReceived += (_, data) => { if (data.Data is { } line) apiLog.Enqueue(line); };
    Require(api.Start(), "start owned API", print: false);
    api.BeginOutputReadLine(); apiError = api.StandardError.ReadToEndAsync();
    for (var i = 0; i < 60; i++)
    {
        Require(!api.HasExited, "owned API stays running during startup", print: false);
        try
        {
            using var response = await http.GetAsync("api/images/artwork?productId=102", timeout.Token);
            if (response.StatusCode == HttpStatusCode.NotFound && apiLog.Any(line => line.Contains("Hosting environment: Development", StringComparison.Ordinal)))
            {
                Console.WriteLine($"Owned API PID {api.Id}: loopback listener ready; Development process configuration; fixture import {(enabled ? "enabled for 102" : "disabled")}.");
                return;
            }
        }
        catch (HttpRequestException) { }
        await Task.Delay(250, timeout.Token);
    }
    throw new CheckFailure("owned API listener ready");
}
async Task StopApi()
{
    if (api is null) return;
    if (!api.HasExited) { api.Kill(); await api.WaitForExitAsync(); }
    // Only owned API processes are stopped. Raw output stays in memory and is never printed.
    api.WaitForExit();
    Console.WriteLine("Owned API logged Development environment: " + apiLog.Any(line => line.Contains("Hosting environment: Development", StringComparison.Ordinal)));
    if (apiError is not null) _ = await apiError;
    api.Dispose(); api = null;
    Console.WriteLine("Owned API stopped; no fixture-enabled process from this test remains running.");
}
async Task<CraterArtwork> PostFixture()
{
    Require(posts < 2, "POST limit", print: false);
    using var request = new HttpRequestMessage(HttpMethod.Post, "api/images/artwork/development-fixture");
    request.Headers.Add("X-CraterIt-Local", "1");
    request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
    posts++;
    using var response = await http.SendAsync(request, timeout.Token);
    Require(response.StatusCode == HttpStatusCode.OK, "fixture POST returned HTTP 200 (no automatic retry)");
    return await response.Content.ReadFromJsonAsync<CraterArtwork>(json, timeout.Token) ?? throw new CheckFailure("fixture response metadata");
}
async Task VerifyGets(CraterArtwork expected)
{
    using var provider = new ServiceCollection().AddLogging().AddWebCraterClients(true, true, baseUrl).BuildServiceProvider();
    var gallery = provider.GetRequiredService<CraterArtworkApiService>();
    Require((await gallery.ListAsync(102, timeout.Token)).Single(a => a.ArtworkId == artworkId) == expected, "Web-registered C# gallery client retrieves identical fixture metadata");
    var jpeg = await gallery.ReadAsync(artworkId, timeout.Token);
    Require(jpeg.AsSpan().SequenceEqual(DevelopmentCraterFixture.GetJpeg()) && Convert.ToHexString(SHA256.HashData(jpeg)) == DevelopmentCraterFixture.Sha256,
        "API JPEG exact bytes and SHA-256 match fixture");
    using var request = new HttpRequestMessage(HttpMethod.Get, $"api/images/artwork/{artworkId:N}/image");
    request.Headers.Add("X-CraterIt-Local", "1");
    using var response = await http.SendAsync(request, timeout.Token);
    Require(response.StatusCode == HttpStatusCode.OK && response.Headers.CacheControl?.NoStore == true &&
        response.Content.Headers.ContentType?.MediaType == "image/jpeg", "API image content type and no-store headers");
}
static async Task RequireAbsent(BlobContainerClient container, string name, CancellationToken token)
{
    try { await container.GetBlobClient(name).GetPropertiesAsync(cancellationToken: token); }
    catch (RequestFailedException ex) when (ex.Status == 404 && ex.ErrorCode == "BlobNotFound") { Console.WriteLine("PASS destination absent: " + name); return; }
    throw new CheckFailure("destination already exists; stop without writing");
}
static void Require(bool condition, string check, bool print = true) { if (!condition) throw new CheckFailure(check); if (print) Console.WriteLine("PASS " + check); }
static IEnumerable<Exception> Chain(Exception ex) { for (Exception? current = ex; current is not null; current = current.InnerException) yield return current; }
sealed class CheckFailure(string check) : Exception { public string Check { get; } = check; }
sealed class ReadOnlyStorageGuard(string image, string metadata) : DelegatingHandler(new HttpClientHandler { AllowAutoRedirect = false })
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        var uri = request.RequestUri!;
        if (uri.Scheme != "https" || uri.Host != "flyermonkey.blob.core.windows.net" || !uri.IsDefaultPort ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment) ||
            request.Method != HttpMethod.Get && request.Method != HttpMethod.Head ||
            uri.AbsolutePath != "/crater-artwork" && uri.AbsolutePath != "/crater-artwork/" + image && uri.AbsolutePath != "/crater-artwork/" + metadata)
            throw new CheckFailure("direct storage read-only boundary");
        return base.SendAsync(request, token);
    }
}
