using System.Net;
using System.Security.Cryptography;
using FlyerMonkey.Shared.Services;
using Microsoft.Extensions.DependencyInjection;

// Explicit live-read opt-in only. No Azure SDK, write requests, image generation or UI automation.
if (args.Length != 3 || args[0] != "--verify-existing-fixture" || args[1] != "--port" ||
    !int.TryParse(args[2], out var port) || port < 1024 || port > 65535)
{
    Console.WriteLine("NOT RUN: --verify-existing-fixture --port PORT reads the previous smoke fixture through localhost. No writes are supported.");
    return 2;
}
var artworkId = Guid.Parse("e21ce070-019e-4429-b32d-9872e38b5475");
var requestId = Guid.Parse("b32a97e1-4da6-4d86-a4d5-1de0bc66b498");
const string expectedHash = "B879F70CAC2DCA989DA9691B445CDD5E0370BF1274E7EC00E66A827614D65928";
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
try
{
    if (port != 5145) Console.WriteLine("SKIP Android registered client: its fixed ADB port is 5145; this isolated API uses a different port.");
    foreach (var android in port == 5145 ? new[] { false, true } : new[] { false })
    {
        var services = new ServiceCollection().AddLogging();
        if (android) services.AddMauiCraterClients(androidDebug: true);
        else services.AddWebCraterClients(development: true, enabled: true, $"http://localhost:{port}/");
        services.AddHttpClient<CraterArtworkApiService>().ConfigurePrimaryHttpMessageHandler(() => new ReadOnlyLoopbackHandler(port));
        using var provider = services.BuildServiceProvider();
        var gallery = provider.GetRequiredService<CraterArtworkApiService>();
        var history = await gallery.ListAsync(int.MaxValue, timeout.Token);
        var artwork = history.Single(a => a.ArtworkId == artworkId);
        Require(artwork.RequestId == requestId && artwork.ProductID == int.MaxValue &&
            artwork.ProductName == "Storage-only smoke fixture (not a catalogue product)" &&
            artwork.ContentType == "image/jpeg" && artwork.Filename == "artwork.jpg" &&
            artwork.SchemaVersion == 1 && artwork.CreatedUtc.Offset == TimeSpan.Zero, "saved metadata");
        var bytes = await gallery.ReadAsync(artworkId, timeout.Token);
        Require(bytes.Length == 631 && Convert.ToHexString(SHA256.HashData(bytes)) == expectedHash, "JPEG bytes/SHA-256");
        if (android) Require(!provider.GetRequiredService<ImageGenerationApiService>().IsEnabled, "Android generation stays disabled");
        Console.WriteLine($"PASS {(android ? "Android Debug" : "Web Development")} registered C# gallery client: metadata and JPEG retrieved through local API.");
    }
    using var http = new HttpClient(new ReadOnlyLoopbackHandler(port)) { BaseAddress = new Uri($"http://localhost:{port}/") };
    using var noHeader = await http.GetAsync("api/images/artwork?productId=2147483647", timeout.Token);
    Require(noHeader.StatusCode == HttpStatusCode.NotFound, "missing local header rejected");
    using var wrongHost = new HttpRequestMessage(HttpMethod.Get, "api/images/artwork?productId=2147483647");
    wrongHost.Headers.Host = "public.example";
    wrongHost.Headers.Add("X-CraterIt-Local", "1");
    using var wrongHostResponse = await http.SendAsync(wrongHost, timeout.Token);
    Require(wrongHostResponse.StatusCode == HttpStatusCode.NotFound, "nonloopback Host rejected");
    Console.WriteLine("PASS read-only API probe. Actual Web UI and Android device were NOT exercised. No POST/PUT/DELETE or paid generation occurred.");
    return 0;
}
catch (HttpRequestException ex)
{
    Console.Error.WriteLine($"FAIL read-only API probe: HTTP status {(ex.StatusCode.HasValue ? ((int)ex.StatusCode.Value).ToString() : "unavailable")}. Check server-side identity, permissions and networking; raw details withheld.");
    return 1;
}
catch (Exception)
{
    Console.Error.WriteLine("FAIL read-only API probe: fixture missing, verification failed or request timed out; raw details withheld.");
    return 1;
}

static void Require(bool condition, string check)
{
    if (!condition) throw new InvalidOperationException();
    Console.WriteLine("PASS " + check);
}

sealed class ReadOnlyLoopbackHandler(int port) : DelegatingHandler(new HttpClientHandler { AllowAutoRedirect = false })
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        if (request.Method != HttpMethod.Get || request.RequestUri is not { } uri ||
            uri.Scheme != "http" || uri.Host != "localhost" || uri.Port != port ||
            !uri.AbsolutePath.StartsWith("/api/images/artwork", StringComparison.Ordinal))
            throw new InvalidOperationException("Read-only probe boundary rejected an operation.");
        return base.SendAsync(request, token);
    }
}
