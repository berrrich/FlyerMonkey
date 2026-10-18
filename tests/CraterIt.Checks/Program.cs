using System.Net;
using FlyerMonkey.Api.Controllers;
using FlyerMonkey.Api.Models;
using FlyerMonkey.Api.Services;
using FlyerMonkey.Shared.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using FlyerMonkey.Shared.Model;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net.Http.Json;

var root = Path.Combine(Path.GetTempPath(), "CraterChecks-" + Guid.NewGuid().ToString("N"));
var store = new LocalCraterArtworkStore(root, NullLogger<LocalCraterArtworkStore>.Instance);

var checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    Console.WriteLine($"PASS: {name}");
    checks++;
}

var product = new FlyerMonkey.Shared.Model.Product
    { Brand = "Brand", Name = "Asparagus", Variant = "Fresh", PackSizeText = "250g" };
var classic = CraterItPromptBuilder.Build(product, CraterStyle.Classic);
Check(classic.StartsWith("Create a clean product image for Brand Asparagus, Fresh, 250g on a plain background."),
    "Classic preserves established wording and metadata order");
Check(CraterItPromptBuilder.Presets.Count == 5 &&
    CraterItPromptBuilder.Presets.Select(p => p.Style).Distinct().Count() == 5, "Five distinct presets available");
product.Brand = product.Name = product.Variant = product.PackSizeText = new string('x', 4000);
Check(CraterItPromptBuilder.Presets.All(p => CraterItPromptBuilder.Build(product, p.Style).Length <= 2000),
    "Every preset stays within API prompt limit with oversized metadata");
Check(CraterItPromptBuilder.Presets.All(p => !string.IsNullOrWhiteSpace(
    CraterItPromptBuilder.Build(new FlyerMonkey.Shared.Model.Product(), p.Style))), "Missing metadata supported");
Check(CraterItPromptBuilder.Presets.Where(p => p.Style != CraterStyle.Classic).All(p =>
    !CraterItPromptBuilder.Build(product, p.Style).Contains("on a plain background")), "Creative presets do not inherit plain-background constraint");
try { CraterItPromptBuilder.Build(product, (CraterStyle)999); throw new Exception("Expected unknown preset rejection"); }
catch (ArgumentOutOfRangeException) { Check(true, "Unknown preset rejected"); }

async Task<ActionResult> Call(string environment = "Development", bool enabled = true,
    string remote = "127.0.0.1", string host = "localhost:5000", bool header = true,
    string prompt = "product", ImageGenerationService? service = null,
    CancellationToken token = default, GenerateImageRequest? input = null, ICraterArtworkStore? storage = null,
    string operation = "generate", string artworkId = "bad")
{
    var context = new DefaultHttpContext();
    context.Connection.RemoteIpAddress = IPAddress.Parse(remote);
    context.Request.Host = new HostString(host);
    if (header) context.Request.Headers["X-CraterIt-Local"] = "1";
    // Missing services intentionally fail if a rejected request resolves the paid service.
    context.RequestServices = service is null && storage is null ? new ServiceCollection().BuildServiceProvider()
        : new ServiceCollection().AddLogging().AddSingleton(service ?? new ImageGenerationService())
            .AddSingleton<ICraterArtworkStore>(storage ?? store).BuildServiceProvider();
    var controller = new ImagesController { ControllerContext = new ControllerContext { HttpContext = context } };
    var configuration = new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["CraterIt:Enabled"] = enabled.ToString() }).Build();
    if (operation == "list") return await controller.List(1, new TestEnvironment { EnvironmentName = environment }, configuration, token);
    if (operation == "image") return await controller.Image(artworkId, new TestEnvironment { EnvironmentName = environment }, configuration, token);
    return await controller.Generate(input ?? new GenerateImageRequest { Prompt = prompt, RequestId = Guid.NewGuid(), ProductID = 1, ProductName = "Product" },
        new TestEnvironment { EnvironmentName = environment }, configuration, token);
}

Check(await Call(environment: "Production") is NotFoundResult, "Production denied before service resolution");
Check(await Call(enabled: false) is NotFoundResult, "Disabled configuration denied");
Check(await Call(remote: "192.0.2.1") is NotFoundResult, "Remote caller denied");
Check(await Call(host: "public.example") is NotFoundResult, "Non-local host denied");
Check(await Call(header: false) is NotFoundResult, "Missing local header denied");
Check(await Call(prompt: " ") is BadRequestObjectResult, "Blank prompt rejected");
Check(await Call(prompt: new string('x', 2001)) is BadRequestObjectResult, "Long prompt rejected");
var fake = new ImageGenerationService();
var input = new GenerateImageRequest { RequestId = Guid.NewGuid(), ProductID = 1, ProductName = "Asparagus", Prompt = "product" };
var saved = (CraterArtwork)((OkObjectResult)await Call(service: fake, input: input)).Value!;
Check((await store.ReadAsync(saved.ArtworkId, default))!.Length == 3 && saved.ProductName == "Asparagus" &&
    saved.CreatedUtc.Offset == TimeSpan.Zero, "Success follows committed JPEG and UTC metadata");
fake.Work = _ => throw new Exception("Duplicate called provider");
Check(await Call(service: fake, input: input) is OkObjectResult { Value: CraterArtwork replay } && replay.ArtworkId == saved.ArtworkId,
    "Duplicate returns saved artwork without provider call");
input.Prompt = "different";
Check(await Call(service: fake, input: input) is ConflictObjectResult, "Conflicting duplicate rejected");
Check(await Call(service: fake, input: new GenerateImageRequest()) is BadRequestObjectResult, "Invalid identifiers rejected");
foreach (var operation in new[] { "list", "image" })
{
    Check(await Call(operation: operation, environment: "Production") is NotFoundResult, $"{operation}: Production denied");
    Check(await Call(operation: operation, enabled: false) is NotFoundResult, $"{operation}: disabled denied");
    Check(await Call(operation: operation, remote: "192.0.2.1") is NotFoundResult, $"{operation}: remote denied");
    Check(await Call(operation: operation, host: "public.example") is NotFoundResult, $"{operation}: public host denied");
    Check(await Call(operation: operation, header: false) is NotFoundResult, $"{operation}: header required");
}
Check(await Call(service: fake, operation: "image", artworkId: "../metadata.json") is NotFoundResult, "Traversal rejected");
Check(await Call(service: fake, operation: "image", artworkId: saved.ArtworkId.ToString("N")) is FileContentResult,
    "Protected image retrieval succeeds");
var restarted = new LocalCraterArtworkStore(root, NullLogger<LocalCraterArtworkStore>.Instance);
Check((await restarted.ListAsync(1, default)).Any(a => a.ArtworkId == saved.ArtworkId) &&
    (await restarted.ReadAsync(saved.ArtworkId, default))!.Length == 3, "Fresh store retrieves artwork after restart");
Check((await restarted.ListAsync(2, default)).Count == 0, "History isolated by product");
var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
fake.Work = async token => { started.SetResult(); await release.Task.WaitAsync(token); return [255, 216, 255]; };
var active = Call(service: fake);
await started.Task;
Check(await Call() is StatusCodeResult { StatusCode: 429 }, "Concurrent generation rejected");
release.SetResult();
await active;
using var cancelled = new CancellationTokenSource();
cancelled.Cancel();
try { await Call(service: fake, token: cancelled.Token); throw new Exception("Expected cancellation"); }
catch (OperationCanceledException) { Check(true, "API cancellation honored"); }

var rejectedRequest = new GenerateImageRequest { RequestId = Guid.NewGuid(), ProductID = 1,
    ProductName = "Asparagus", Preset = (CraterStyle)999, Prompt = "product" };
Check(await Call(service: fake, input: rejectedRequest) is BadRequestObjectResult, "Unknown API preset rejected");
fake.Work = _ => Task.FromResult<byte[]>([255, 216, 255]);
Check(await Call(service: fake, storage: new FaultStore()) is ObjectResult { StatusCode: 503 },
    "Write failure never reports generation success");
using var lateCancellation = new CancellationTokenSource();
fake.Work = _ => { lateCancellation.Cancel(); return Task.FromResult<byte[]>([255, 216, 255]); };
Check(await Call(service: fake, token: lateCancellation.Token) is OkObjectResult,
    "Cancellation after generated bytes does not discard artwork");
fake.Work = _ => Task.FromResult<byte[]>([255, 216, 255]);
var blockedRoot = Path.Combine(root, "blocked");
await File.WriteAllTextAsync(blockedRoot, "not a directory");
Check(await Call(service: fake, storage: new LocalCraterArtworkStore(blockedRoot, NullLogger<LocalCraterArtworkStore>.Instance))
    is ObjectResult { StatusCode: 503 }, "Real filesystem write failure returns no success");
var before = (await store.ListAsync(1, default)).Count;
var collision = saved with { RequestId = Guid.NewGuid() };
try { await store.SaveAsync(collision, [255, 216, 255], default); throw new Exception("Expected collision"); }
catch (IOException) { Check(!Directory.EnumerateDirectories(root, ".staging-*").Any(), "Failed commit cleans staging and preserves original"); }
var partial = Path.Combine(root, ".staging-interrupted");
Directory.CreateDirectory(partial);
await File.WriteAllBytesAsync(Path.Combine(partial, "artwork.jpg"), [255, 216, 255]);
var broken = Path.Combine(root, Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(broken);
await File.WriteAllTextAsync(Path.Combine(broken, "metadata.json"), "{broken");
Check((await restarted.ListAsync(1, default)).Count == before, "Staging and malformed records excluded without hiding valid history");
var imagePath = Path.Combine(root, saved.ArtworkId.ToString("N"), "artwork.jpg");
File.Move(imagePath, imagePath + ".missing");
Check(await restarted.ReadAsync(saved.ArtworkId, default) is null &&
    !(await restarted.ListAsync(1, default)).Any(a => a.ArtworkId == saved.ArtworkId), "Missing file returns no artwork and is omitted from history");
input.Prompt = "product";
fake.Work = _ => throw new Exception("Missing saved file must not regenerate");
Check(await Call(service: fake, input: input) is ObjectResult { StatusCode: 503 }, "Missing duplicate image never incurs another provider call");
File.Move(imagePath + ".missing", imagePath);
try { LocalCraterArtworkStore.ResolveRoot(new TestEnvironment { ContentRootPath = root }); throw new Exception("Expected root rejection"); }
catch (InvalidOperationException) { Check(true, "Unexpected repository layout fails closed"); }

var handler = new FakeHandler();
using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000/") };
var client = new ImageGenerationApiService(http);
var gallery = new CraterArtworkApiService(http);
handler.Artwork = saved;
Check((await client.GenerateAsync(Guid.NewGuid(), 1, "Asparagus", CraterStyle.Classic, "product", default)).ArtworkId == saved.ArtworkId && handler.Calls == 1,
    "Client posts once and accepts saved metadata");
Check((await gallery.ListAsync(1, default)).Count == 1 && (await gallery.ReadAsync(saved.ArtworkId, default)).Length == 3,
    "Client retrieves history and JPEG with local header");
handler.InvalidImage = true;
try { await gallery.ReadAsync(saved.ArtworkId, default); throw new Exception("Expected invalid image rejection"); }
catch (InvalidOperationException) { Check(true, "Non-JPEG response rejected"); }
try { await gallery.ReadAsync(saved.ArtworkId, cancelled.Token); throw new Exception("Expected cancellation"); }
catch (OperationCanceledException) { Check(true, "Client cancellation honored"); }
using var disabledHttp = new HttpClient(handler);
Check(!new ImageGenerationApiService(disabledHttp).IsEnabled, "MAUI/default client disabled");
using var publicHttp = new HttpClient(handler) { BaseAddress = new Uri("https://public.example/") };
Check(!new ImageGenerationApiService(publicHttp).IsEnabled, "Public API address disabled");
Check(!new CraterArtworkApiService(publicHttp).IsEnabled && !new CraterArtworkApiService(disabledHttp).IsEnabled,
    "Public and unconfigured gallery clients disabled");
Check(typeof(CraterArtworkApiService).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly | System.Reflection.BindingFlags.Instance)
    .All(m => m.Name is "get_IsEnabled" or "ListAsync" or "ReadAsync"), "Read-only client exposes no generation/upload operation");
var androidServices = new ServiceCollection().AddLogging();
androidServices.AddMauiCraterClients(androidDebug: true);
var androidHandler = new FakeHandler { Artwork = saved };
androidServices.AddHttpClient<CraterArtworkApiService>().ConfigurePrimaryHttpMessageHandler(() => androidHandler);
using (var provider = androidServices.BuildServiceProvider())
{
    var androidGallery = provider.GetRequiredService<CraterArtworkApiService>();
    var androidGeneration = provider.GetRequiredService<ImageGenerationApiService>();
    Check(androidGallery.IsEnabled && !androidGeneration.IsEnabled, "Android Debug registration enables gallery and disables generation");
    Check((await androidGallery.ListAsync(1, default)).Single().ArtworkId == saved.ArtworkId &&
        (await androidGallery.ReadAsync(saved.ArtworkId, default)).Length == 3 && androidHandler.Calls == 2,
        "Android Debug gallery reads saved history/JPEG through forwarded localhost using GET only");
    try
    {
        await androidGeneration.GenerateAsync(Guid.NewGuid(), 1, "Asparagus", CraterStyle.Classic, "product", default);
        throw new Exception("Expected Android generation denial");
    }
    catch (InvalidOperationException) { Check(androidHandler.Calls == 2, "Android generation rejected before any HTTP call"); }
}
using (var provider = new ServiceCollection().AddLogging().AddMauiCraterClients(androidDebug: false).BuildServiceProvider())
    Check(!provider.GetRequiredService<CraterArtworkApiService>().IsEnabled &&
        !provider.GetRequiredService<ImageGenerationApiService>().IsEnabled, "Release/non-Android MAUI has no gallery or generation address");
foreach (var setup in new[] { (true, true, "http://localhost:5145/", true), (true, true, "https://localhost:7255/", true),
    (false, true, "http://localhost:5145/", false), (true, false, "http://localhost:5145/", false),
    (true, true, "https://public.example/", false), (true, true, "http://10.0.2.2:5145/", false) })
{
    using var provider = new ServiceCollection().AddLogging().AddWebCraterClients(setup.Item1, setup.Item2, setup.Item3).BuildServiceProvider();
    Check(provider.GetRequiredService<CraterArtworkApiService>().IsEnabled == setup.Item4 &&
        provider.GetRequiredService<ImageGenerationApiService>().IsEnabled == setup.Item4,
        "Web retains explicit Development/flag/loopback policy for gallery and generation");
    foreach (var type in new[] { typeof(CraterArtworkApiService), typeof(ImageGenerationApiService) })
    {
        var pipeline = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(type.Name);
        while (pipeline is DelegatingHandler link) pipeline = link.InnerHandler!;
        Check(pipeline is HttpClientHandler { AllowAutoRedirect: false }, "Crater clients do not follow redirects");
    }
}
Check(androidHandler.GetOnly && androidHandler.LastHost == "localhost" && androidHandler.LastPort == 5145,
    "Android gallery stays on the ADB-forwarded loopback port");
async Task<ActionResult> CallFixture(string environment = "Development", bool enabled = true, bool? fixtureEnabled = true,
    string remote = "127.0.0.1", string host = "localhost:5146", bool header = true,
    string provider = "Azure", string? allowedProduct = "102", ImportDevelopmentFixtureRequest? input = null,
    FixtureStore? storage = null, ProductService? products = null, CancellationToken token = default)
{
    var context = new DefaultHttpContext();
    context.Connection.RemoteIpAddress = IPAddress.Parse(remote);
    context.Request.Host = new HostString(host);
    if (header) context.Request.Headers["X-CraterIt-Local"] = "1";
    var services = new ServiceCollection().AddLogging();
    if (storage is not null) services.AddSingleton<ICraterArtworkStore>(storage);
    if (products is not null) services.AddSingleton(products);
    // No paid provider is registered, including for successful imports.
    context.RequestServices = services.BuildServiceProvider();
    var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["CraterIt:Enabled"] = enabled.ToString(), ["CraterIt:FixtureImportEnabled"] = fixtureEnabled?.ToString(),
        ["CraterIt:FixtureProductId"] = allowedProduct, ["CraterArt:Provider"] = provider
    }).Build();
    var controller = new DevelopmentCraterFixturesController { ControllerContext = new ControllerContext { HttpContext = context } };
    return await controller.Import(input ?? new ImportDevelopmentFixtureRequest
        { RequestId = Guid.NewGuid(), ArtworkId = Guid.NewGuid(), ProductID = 102 },
        new TestEnvironment { EnvironmentName = environment }, configuration, token);
}

Check(await CallFixture(fixtureEnabled: null) is NotFoundResult, "Fixture endpoint defaults off before service resolution");
Check(await CallFixture(fixtureEnabled: false) is NotFoundResult, "Explicitly disabled fixture endpoint denied");
Check(await CallFixture(environment: "Production") is NotFoundResult, "Production fixture import denied even when enabled");
Check(await CallFixture(environment: "Staging") is NotFoundResult, "Staging fixture import denied even when enabled");
Check(await CallFixture(enabled: false) is NotFoundResult, "Crater opt-in is still required for fixtures");
Check(await CallFixture(remote: "192.0.2.1") is NotFoundResult, "Nonloopback fixture caller denied");
Check(await CallFixture(host: "public.example") is NotFoundResult, "Nonloopback fixture Host denied");
Check(await CallFixture(header: false) is NotFoundResult, "Fixture local header required");
Check(await CallFixture(provider: "Local") is NotFoundResult, "Fixture endpoint refuses local artwork storage");
Check(await CallFixture(allowedProduct: null) is NotFoundResult && await CallFixture(allowedProduct: "invalid") is NotFoundResult,
    "Fixture endpoint requires an explicit valid product scope");
Check(await CallFixture(input: new ImportDevelopmentFixtureRequest { ProductID = 102 }) is BadRequestObjectResult,
    "Empty fixture identifiers rejected before services");
Check(await CallFixture(input: new ImportDevelopmentFixtureRequest { RequestId = Guid.NewGuid(), ArtworkId = Guid.NewGuid(), ProductID = 103 }) is BadRequestObjectResult,
    "Fixture endpoint cannot write an unapproved product");

var fixtureStore = new FixtureStore();
var fixtureProducts = new ProductService();
var fixtureRequest = new ImportDevelopmentFixtureRequest { RequestId = Guid.NewGuid(), ArtworkId = Guid.NewGuid(), ProductID = 102 };
var fixtureResult = await CallFixture(input: fixtureRequest, storage: fixtureStore, products: fixtureProducts);
var fixtureMetadata = (CraterArtwork)((OkObjectResult)fixtureResult).Value!;
Check(fixtureStore.Writes == 1 && fixtureProducts.Reads == 1 && fixtureMetadata.ProductID == 102 &&
    fixtureMetadata.ProductName == "[DEV FIXTURE] Test product" && fixtureMetadata.PresetName == "Development fixture (not generated)",
    "Fixture import validates existing product and saves a clearly labelled record without a paid provider");
Check(fixtureMetadata.ArtworkId == fixtureRequest.ArtworkId && fixtureMetadata.RequestId == fixtureRequest.RequestId &&
    fixtureMetadata.CreatedUtc.Offset == TimeSpan.Zero && fixtureMetadata.ContentType == "image/jpeg" && fixtureMetadata.Filename == "artwork.jpg",
    "Fixture import preserves request/artwork identity and server UTC metadata");
var fixtureBytes = (await fixtureStore.ReadAsync(fixtureMetadata.ArtworkId, default))!;
Check(fixtureBytes.Length == 631 && Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fixtureBytes)) == DevelopmentCraterFixture.Sha256,
    "Only the embedded valid 631-byte fixture with expected SHA-256 is saved");
Check(await CallFixture(input: fixtureRequest, storage: fixtureStore) is OkObjectResult { Value: CraterArtwork fixtureReplay } &&
    fixtureReplay == fixtureMetadata && fixtureStore.Writes == 1, "Fixture replay returns original metadata without writing or reloading SQL");
var changedIdentity = new ImportDevelopmentFixtureRequest { RequestId = fixtureRequest.RequestId, ArtworkId = Guid.NewGuid(), ProductID = 102 };
Check(await CallFixture(input: changedIdentity, storage: fixtureStore) is ConflictObjectResult && fixtureStore.Writes == 1,
    "Fixture request conflict never overwrites");
var collidingIdentity = new ImportDevelopmentFixtureRequest { RequestId = Guid.NewGuid(), ArtworkId = fixtureMetadata.ArtworkId, ProductID = 102 };
Check(await CallFixture(input: collidingIdentity, storage: fixtureStore) is ConflictObjectResult && fixtureStore.Writes == 1,
    "Existing artwork ID cannot be overwritten by another request");
fixtureStore.Images.Remove(fixtureMetadata.ArtworkId);
Check(await CallFixture(input: fixtureRequest, storage: fixtureStore) is ObjectResult { StatusCode: 503 } && fixtureStore.Writes == 1,
    "Missing committed fixture is not recreated on replay");
fixtureStore.Images[fixtureMetadata.ArtworkId] = [255, 216, 255];
Check(await CallFixture(input: fixtureRequest, storage: fixtureStore) is ObjectResult { StatusCode: 503 } && fixtureStore.Writes == 1,
    "Changed committed fixture is not repaired or overwritten");
var absentProducts = new ProductService { Lookup = (_, _) => Task.FromResult<Product?>(null) };
var absentStore = new FixtureStore();
Check(await CallFixture(storage: absentStore, products: absentProducts) is NotFoundObjectResult && absentStore.Writes == 0,
    "Nonexistent catalogue product prevents fixture save");
var failedFixtureStore = new FixtureStore { FailSave = true };
Check(await CallFixture(storage: failedFixtureStore, products: fixtureProducts) is ObjectResult { StatusCode: 503 } && failedFixtureStore.Writes == 0,
    "Fixture persistence failure does not report success");
using var fixtureCancellation = new CancellationTokenSource();
fixtureCancellation.Cancel();
try { await CallFixture(token: fixtureCancellation.Token); throw new Exception("Expected fixture cancellation"); }
catch (OperationCanceledException) { Check(true, "Cancelled fixture request cannot reach storage"); }
var fixtureStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var fixtureRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var busyProducts = new ProductService { Lookup = async (id, token) =>
    { fixtureStarted.SetResult(); await fixtureRelease.Task.WaitAsync(token); return new Product { ID = id, Name = "Test product" }; } };
var busyFixture = CallFixture(storage: new FixtureStore(), products: busyProducts);
await fixtureStarted.Task;
Check(await CallFixture() is StatusCodeResult { StatusCode: 429 }, "Concurrent fixture import is rejected before storage");
Check(await Call() is StatusCodeResult { StatusCode: 429 }, "Fixture import and generation share the existing single-process write slot");
fixtureRelease.SetResult();
await busyFixture;
Check(Convert.ToInt64(typeof(DevelopmentCraterFixturesController).GetMethod("Import")!.CustomAttributes
    .Single(a => a.AttributeType == typeof(RequestSizeLimitAttribute)).ConstructorArguments.Single().Value) == 1024,
    "Fixture request body is limited to 1 KiB and contains no image/prompt/path fields");
Check(typeof(ImportDevelopmentFixtureRequest).GetProperties().Select(p => p.Name).Order().SequenceEqual(new[] { "ArtworkId", "ProductID", "RequestId" }),
    "Fixture body accepts only artwork ID, product ID and request ID");
Console.WriteLine($"{checks} checks passed. No provider calls or credentials used.");

sealed class FixtureStore : ICraterArtworkStore
{
    public Dictionary<Guid, CraterArtwork> Metadata = [];
    public Dictionary<Guid, byte[]> Images = [];
    public int Writes;
    public bool FailSave;
    public Task<CraterArtwork?> FindRequestAsync(Guid id, CancellationToken token) => Task.FromResult(Metadata.Values.SingleOrDefault(a => a.RequestId == id));
    public Task<IReadOnlyList<CraterArtwork>> ListAsync(int id, CancellationToken token) => Task.FromResult<IReadOnlyList<CraterArtwork>>(Metadata.Values.Where(a => a.ProductID == id).ToArray());
    public Task<byte[]?> ReadAsync(Guid id, CancellationToken token) => Task.FromResult(Images.GetValueOrDefault(id));
    public Task SaveAsync(CraterArtwork artwork, byte[] bytes, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (FailSave) throw new IOException("Simulated fixture save failure");
        Metadata.Add(artwork.ArtworkId, artwork);
        Images.Add(artwork.ArtworkId, bytes);
        Writes++;
        return Task.CompletedTask;
    }
}

sealed class FaultStore : ICraterArtworkStore
{
    public Task<CraterArtwork?> FindRequestAsync(Guid id, CancellationToken token) => Task.FromResult<CraterArtwork?>(null);
    public Task<IReadOnlyList<CraterArtwork>> ListAsync(int id, CancellationToken token) => Task.FromResult<IReadOnlyList<CraterArtwork>>([]);
    public Task<byte[]?> ReadAsync(Guid id, CancellationToken token) => Task.FromResult<byte[]?>(null);
    public Task SaveAsync(CraterArtwork artwork, byte[] bytes, CancellationToken token) => throw new IOException("Simulated failed write");
}

sealed class FakeHandler : HttpMessageHandler
{
    public int Calls;
    public bool InvalidImage;
    public CraterArtwork Artwork = null!;
    public bool GetOnly = true;
    public string? LastHost;
    public int LastPort;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!request.Headers.Contains("X-CraterIt-Local")) throw new Exception("Invalid request");
        GetOnly &= request.Method == HttpMethod.Get;
        LastHost = request.RequestUri!.Host;
        LastPort = request.RequestUri.Port;
        Calls++;
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(InvalidImage ? [1, 2, 3] : [255, 216, 255])
        };
        response.Content.Headers.ContentType = new("image/jpeg");
        if (request.Method == HttpMethod.Post) response.Content = JsonContent.Create(Artwork);
        else if (request.RequestUri!.AbsolutePath.EndsWith("/artwork")) response.Content = JsonContent.Create(new[] { Artwork });
        return Task.FromResult(response);
    }
}

sealed class TestEnvironment : IWebHostEnvironment
{
    public string EnvironmentName { get; set; } = "Development";
    public string ApplicationName { get; set; } = "Checks";
    public string WebRootPath { get; set; } = "";
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string ContentRootPath { get; set; } = "";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}

namespace FlyerMonkey.Api.Services
{
    // Only the read-only product lookup is needed by the linked fixture endpoint; no database is contacted.
    public sealed class ProductService
    {
        public int Reads;
        public Func<int, CancellationToken, Task<Product?>> Lookup = (id, _) => Task.FromResult<Product?>(new Product { ID = id, Name = "Test product" });
        public Task<Product?> GetAsync(int id, CancellationToken token) { Reads++; return Lookup(id, token); }
    }
    // Linked controller uses this fake instead of constructing the provider service.
    public sealed class ImageGenerationService
    {
        public Func<CancellationToken, Task<byte[]>> Work = _ => Task.FromResult<byte[]>([255, 216, 255]);
        public async Task<TestImage> GenerateAsync(string prompt, CancellationToken token) => new(await Work(token));
    }
    public sealed record TestImage(byte[] Bytes)
    {
        public byte[] ToArray() => Bytes;
    }
}
