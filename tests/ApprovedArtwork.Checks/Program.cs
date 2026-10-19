using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Core.Pipeline;
using Azure.Storage.Blobs;
using FlyerMonkey.Api.Controllers;
using FlyerMonkey.Api.Models;
using FlyerMonkey.Api.Services;
using FlyerMonkey.Shared.Model;
using Product = FlyerMonkey.Shared.Model.Product;
using FlyerMonkey.Shared.Pages;
using FlyerMonkey.Shared.Services;
using FlyerMonkey.Tools;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

var count = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); count++; Console.WriteLine("PASS: " + name); }
async Task Reject<T>(Func<Task> action, string name) where T : Exception
{
    try { await action(); } catch (T) { Check(true, name); return; }
    throw new Exception("Expected rejection: " + name);
}
var root = Path.Combine(Path.GetTempPath(), "approved-artwork-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var shipped = JsonSerializer.Deserialize<ApprovedArtworkManifest>(await File.ReadAllTextAsync(
        Path.Combine(AppContext.BaseDirectory, "approved-artwork.json")))!;
    shipped.Validate();
    Check(shipped.Entries.Length == 1 && shipped.Entries[0] is
        { ProductID:102, ArtworkId:var shippedId, Sha256:"D83EC0500BD2152E02E571BF2B7E825E31AD4DC1565DF49A78F6AD1DE99E4817",
          Length:164538, Preset:CraterStyle.CometCrush, Provenance:"generated", ApprovedBy:"FMAdmin" } &&
        shippedId == Guid.Parse("28071cca-5456-4f78-b621-28c504a3440a"),
        "Shipped manifest approves only the selected Dynamo bytes under FMAdmin");
    Check(shipped.Entries[0].ApprovedUtc <= DateTimeOffset.UtcNow,
        "Recorded approval has a valid nonfuture UTC timestamp");
    var entry = new ApprovedArtwork(102, Guid.NewGuid(), Convert.ToHexString(SHA256.HashData(new byte[] {255,216,255,7})),
        4, CraterStyle.CometCrush, "generated", "offline reviewer", DateTimeOffset.UtcNow);
    byte[] jpeg = [255,216,255,7];
    var metadata = new CraterArtwork(entry.ArtworkId, Guid.NewGuid(), 102, "Dynamo", CraterStyle.CometCrush,
        "Comet Crush", DateTimeOffset.UtcNow, "image/jpeg", "artwork.jpg", 1, new string('A',64));
    using var handler = new OfflineHandler();
    using var http = new HttpClient(handler);
    var options = new BlobClientOptions { Transport = new HttpClientTransport(http) };
    options.Retry.MaxRetries = 0;
    var container = new BlobContainerClient(new Uri("https://offline.invalid/crater-artwork"), options);
    var diagnostics = new List<string>();
    var reader = new ApprovedArtworkReader(container, new CaptureLogger<ApprovedArtworkReader>(diagnostics));
    var prefix = $"artwork/{entry.ArtworkId:N}/";
    handler.Blobs[prefix+"metadata.json"] = JsonSerializer.SerializeToUtf8Bytes(metadata);
    handler.Blobs[prefix+"metadata.json"] = Encoding.UTF8.GetBytes("broken");
    await Reject<JsonException>(() => reader.ReadAsync(entry,default), "Malformed Blob metadata denied");
    handler.Blobs[prefix+"metadata.json"] = JsonSerializer.SerializeToUtf8Bytes(metadata with { SchemaVersion=2 });
    await Reject<InvalidDataException>(() => reader.ReadAsync(entry,default), "Unsupported Blob schema denied");
    handler.Blobs.Remove(prefix+"metadata.json");
    Check(await reader.ReadAsync(entry,default) is null,"JPEG without committed metadata is never published");
    Check(diagnostics.Any(x => x.Contains("missing at stage metadata-read")), "Missing metadata has a distinct safe diagnostic");
    handler.Blobs[prefix+"metadata.json"] = JsonSerializer.SerializeToUtf8Bytes(metadata);
    handler.Blobs[prefix+"artwork.jpg"] = jpeg;
    Check((await reader.ReadAsync(entry, default))!.SequenceEqual(jpeg), "Exact approved bytes retrieved");
    Check(handler.Methods.All(x => x == "GET") && handler.Paths.All(x => x == "" || x.StartsWith(prefix)), "Reader only GETs exact approved paths; never lists");
    await Reject<InvalidDataException>(() => reader.ReadAsync(entry with { Sha256 = new string('0',64) }, default), "Wrong hash denied");
    await Reject<InvalidDataException>(() => reader.ReadAsync(entry with { ProductID = 88 }, default), "Wrong product denied");
    await Reject<InvalidDataException>(() => reader.ReadAsync(entry with { Preset = CraterStyle.Classic }, default), "Wrong preset denied");
    await Reject<InvalidDataException>(() => reader.ReadAsync(entry with { Length = 3 }, default), "Oversize denied");
    foreach (var bad in new[] { entry with { Provenance = "fixture" }, entry with { ApprovedBy = "" },
        entry with { ArtworkId = Guid.Parse("df6a9c89-85b1-40e4-bbc9-d80103913b6c") },
        entry with { ArtworkId = Guid.Parse("e21ce070-019e-4429-b32d-9872e38b5475") }, entry with { Sha256 = "bad" } })
        await Reject<InvalidDataException>(() => reader.ReadAsync(bad, default), "Invalid or fixture approval denied before reads");
    handler.Blobs[prefix+"metadata.json"] = JsonSerializer.SerializeToUtf8Bytes(metadata with { ProductName = "[DEV FIXTURE] Dynamo" });
    await Reject<InvalidDataException>(() => reader.ReadAsync(entry, default), "Fixture-labelled metadata denied");
    handler.Blobs[prefix+"metadata.json"] = JsonSerializer.SerializeToUtf8Bytes(metadata);
    handler.Public = true;
    await Reject<InvalidDataException>(() => reader.ReadAsync(entry, default), "Public container denied");
    handler.Public = false;
    handler.Blobs.Remove(prefix+"artwork.jpg");
    Check(await reader.ReadAsync(entry, default) is null, "Missing JPEG returns absent");
    Check(diagnostics.Any(x => x.Contains("missing at stage jpeg-read")), "Missing JPEG has a distinct safe diagnostic");
    handler.Blobs[prefix+"artwork.jpg"] = jpeg;
    handler.Failure = 403;
    await Reject<RequestFailedException>(() => reader.ReadAsync(entry, default), "Storage authorization failure is explicit");
    Check(diagnostics.Any(x => x.Contains("stage container-check") && x.Contains("403")), "Storage denial logs stage and status without URL");
    handler.Failure = null;
    using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
    await Reject<OperationCanceledException>(() => reader.ReadAsync(entry, cancelled.Token), "Cancellation honored");

    async Task<IActionResult> Call(bool enabled, string json, int id = 102, bool storage = false, IConfiguration? actualConfiguration = null)
    {
        await File.WriteAllTextAsync(Path.Combine(root,"approved-artwork.json"),json);
        var services = new ServiceCollection().AddLogging(builder => builder.AddProvider(new CaptureProvider(diagnostics)));
        if(storage) services.AddSingleton(reader);
        using var provider = services.BuildServiceProvider();
        var controller = new ApprovedArtworkController { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { RequestServices = provider } } };
        var result = await controller.Get(id, actualConfiguration ?? new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
            { ["ApprovedArtwork:Enabled"] = enabled.ToString() }).Build(), new TestEnvironment(root), default);
        Check(controller.Response.Headers.CacheControl == "no-store", "Response is not cached");
        return result;
    }
    string Manifest(params ApprovedArtwork[] entries) => JsonSerializer.Serialize(new ApprovedArtworkManifest(1, entries));
    // Exercise the same default builder used by API Program.cs, not an in-memory imitation of Azure keys.
    // Changes apply only to this test process and are always restored; no Azure setting is changed.
    string[] environmentKeys = ["ApprovedArtwork__Enabled", "ApprovedArtwork:Enabled",
        "ApprovedArtwork__ServiceUri", "ApprovedArtwork:ServiceUri",
        "ASPNETCORE_ApprovedArtwork__Enabled", "DOTNET_ApprovedArtwork__Enabled"];
    var savedEnvironment = environmentKeys.ToDictionary(key => key, Environment.GetEnvironmentVariable);
    var configRoot = Path.Combine(root,"configuration");
    Directory.CreateDirectory(configRoot);
    try
    {
        foreach(var key in environmentKeys) Environment.SetEnvironmentVariable(key,null);
        await File.WriteAllTextAsync(Path.Combine(configRoot,"appsettings.json"), """
            { "ApprovedArtwork": { "Enabled": false } }
            """);
        await File.WriteAllTextAsync(Path.Combine(configRoot,"appsettings.Production.json"), """
            { "ApprovedArtwork": { "Enabled": false } }
            """);
        Environment.SetEnvironmentVariable("ApprovedArtwork__Enabled","true");
        Environment.SetEnvironmentVariable("ApprovedArtwork__ServiceUri","https://account.blob.core.windows.net/");
        foreach(var environmentName in new[]{"Production","Development"})
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
                { Args=[], EnvironmentName=environmentName, ContentRootPath=configRoot });
            await using var app = builder.Build(); // Build only: no listener, database, identity or network activity.
            Check(app.Configuration.GetValue<bool>("ApprovedArtwork:Enabled"),
                $"Azure double-underscore true overrides JSON false in {environmentName}");
            Check(app.Configuration["ApprovedArtwork:ServiceUri"] == "https://account.blob.core.windows.net/",
                "Azure service URI uses the same normalized configuration hierarchy");
            Check(await Call(false,Manifest(entry),storage:true,actualConfiguration:app.Configuration) is FileContentResult,
                $"Actual controller accepts Azure environment flag in {environmentName}");
        }
        var commandLineBuilder = WebApplication.CreateBuilder(new WebApplicationOptions
            { Args=["--ApprovedArtwork:Enabled=false"], EnvironmentName="Production", ContentRootPath=configRoot });
        await using(var app = commandLineBuilder.Build())
            Check(!app.Configuration.GetValue<bool>("ApprovedArtwork:Enabled"),
                "Explicit command-line false has higher priority than Azure environment true");
        Environment.SetEnvironmentVariable("ApprovedArtwork__Enabled","false");
        var disabledBuilder = WebApplication.CreateBuilder(new WebApplicationOptions
            { Args=[], EnvironmentName="Production", ContentRootPath=configRoot });
        await using(var app = disabledBuilder.Build())
            Check(await Call(true,"broken",actualConfiguration:app.Configuration) is NotFoundResult,
                "Azure environment false keeps controller disabled before manifest access");
        Environment.SetEnvironmentVariable("ApprovedArtwork__Enabled",null);
        await File.WriteAllTextAsync(Path.Combine(configRoot,"appsettings.json"),"{}");
        await File.WriteAllTextAsync(Path.Combine(configRoot,"appsettings.Production.json"),"{}");
        var absentBuilder = WebApplication.CreateBuilder(new WebApplicationOptions
            { Args=[], EnvironmentName="Production", ContentRootPath=configRoot });
        await using(var app = absentBuilder.Build())
            Check(!app.Configuration.GetValue<bool>("ApprovedArtwork:Enabled"),
                "Missing approval flag remains false by default");
    }
    finally
    {
        foreach(var item in savedEnvironment) Environment.SetEnvironmentVariable(item.Key,item.Value);
    }
    Check(await Call(false, "broken") is NotFoundResult, "Disabled endpoint denies before manifest/storage resolution");
    Check(diagnostics.Any(x => x.Contains("404 stage feature-disabled")), "Disabled flag 404 is distinguishable");
    Check(await Call(true, Manifest()) is NotFoundResult, "Empty approval denies before storage resolution");
    Check(diagnostics.Any(x => x.Contains("404 stage product-unapproved")), "Missing approval 404 is distinguishable");
    Check(await Call(true, Manifest(entry), 88) is NotFoundResult, "Unapproved product denied");
    Check(await Call(true, Manifest(entry), -1) is NotFoundResult, "Invalid product denied");
    Check(await Call(true, "broken") is StatusCodeResult { StatusCode:503 }, "Malformed approval fails closed");
    Check(diagnostics.Any(x => x.Contains("503 stage manifest-read")), "Bad manifest diagnostic does not masquerade as 404");
    Check(await Call(true, Manifest(entry,entry)) is StatusCodeResult { StatusCode:503 }, "Duplicate approvals fail closed");
    Check(await Call(true, Manifest(entry), storage:true) is FileContentResult { ContentType:"image/jpeg" }, "Explicit approval returns only JPEG");
    Check(diagnostics.Any(x => x.Contains("stage jpeg-verified")), "Successful content verification is logged");
    handler.Blobs.Remove(prefix+"artwork.jpg");
    Check(await Call(true, Manifest(entry), storage:true) is NotFoundResult, "Approved but missing Blob returns controller 404");
    Check(diagnostics.Any(x => x.Contains("404 stage approved-blob-missing")), "Missing Blob 404 differs from missing approval");
    handler.Blobs[prefix+"artwork.jpg"] = jpeg;
    handler.Blobs[prefix+"artwork.jpg"] = [255,216,255,8];
    Check(await Call(true, Manifest(entry), storage:true) is StatusCodeResult { StatusCode:503 }, "Changed approved bytes never served");
    handler.Blobs[prefix+"artwork.jpg"] = jpeg;
    Check(await Call(true, Manifest(), storage:true) is NotFoundResult, "Revocation immediately denies");
    Check(diagnostics.All(x => !x.Contains("https://") && !x.Contains("offline.invalid") &&
        !x.Contains("Dynamo") && !x.Contains(entry.ArtworkId.ToString())), "Diagnostics omit private URLs and sensitive artwork metadata");

    foreach(var bad in new[] {"http://account.blob.core.windows.net/", "https://example.com/", "https://account.blob.core.windows.net/?unexpected=value"})
        await Reject<InvalidOperationException>(() => { ApprovedArtworkReader.CreateContainer(bad, true); return Task.CompletedTask; }, "Unsafe storage address rejected without traffic");
    Check(!new ApprovedArtworkApiService(new HttpClient()).IsEnabled, "Cloud client disabled by default");
    using var clientHandler = new ClientHandler();
    using var clientHttp = new HttpClient(clientHandler) { BaseAddress = new Uri("https://offline.invalid/") };
    var client = new ApprovedArtworkApiService(clientHttp);
    Check((await client.ReadAsync(102,default))!.SequenceEqual(jpeg), "Cloud HTTPS client reads approved JPEG");
    Check(clientHandler.Method == HttpMethod.Get && clientHandler.Path == "/api/products/102/approved-artwork/image" && !clientHandler.LocalHeader,
        "Cloud client uses product-only GET without local gallery header");
    clientHandler.Status = 404;
    Check(await client.ReadAsync(102,default) is null, "Unapproved client image omitted");
    clientHandler.Status = 302;
    await Reject<HttpRequestException>(() => client.ReadAsync(102,default), "Redirect rejected");
    clientHandler.Status = 200; clientHandler.Bytes = [1,2,3];
    await Reject<InvalidDataException>(() => client.ReadAsync(102,default), "Non-JPEG denied");
    clientHandler.Bytes = new byte[2*1024*1024+1];
    await Reject<InvalidDataException>(() => client.ReadAsync(102,default), "Large client response denied");
    var registration = new ServiceCollection().AddLogging();
    registration.AddApprovedArtworkClient(true,"https://offline.invalid/");
    using(var provider=registration.BuildServiceProvider())
    {
        var pipeline=provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(nameof(ApprovedArtworkApiService));
        while(pipeline is DelegatingHandler link)pipeline=link.InnerHandler!;
        Check(pipeline is HttpClientHandler { AllowAutoRedirect:false }, "Actual registered cloud handler disables redirects");
        Check(provider.GetRequiredService<ApprovedArtworkApiService>().IsEnabled,"Registered explicit HTTPS cloud client is enabled");
    }
    using(var provider=new ServiceCollection().AddLogging().AddApprovedArtworkClient().BuildServiceProvider())
        Check(!provider.GetRequiredService<ApprovedArtworkApiService>().IsEnabled,"Actual default registration remains disabled");
    await Reject<InvalidOperationException>(() => { new ServiceCollection().AddApprovedArtworkClient(true,"http://localhost:5145/"); return Task.CompletedTask; }, "Cloud registration rejects local/HTTP URL");

    async Task<(string Html, int Requests)> RenderProduct(bool enabled, int status, int productId = 102)
    {
        using var imageHandler = new ClientHandler { Status=status };
        using var generationHttp = new HttpClient();
        using var galleryHttp = new HttpClient();
        var services = new ServiceCollection().AddLogging();
        services.AddApprovedArtworkClient(enabled, "https://offline.invalid/");
        services.AddHttpClient<ApprovedArtworkApiService>().ConfigurePrimaryHttpMessageHandler(() => imageHandler);
        services.AddSingleton<IProductService>(new DisplayProducts(productId));
        services.AddSingleton(new ImageGenerationApiService(generationHttp));
        services.AddSingleton(new CraterArtworkApiService(galleryHttp));
        services.AddSingleton<NavigationManager>(new DisplayNavigation());
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var result = await renderer.RenderComponentAsync<ProductDetail>(ParameterView.FromDictionary(
                new Dictionary<string,object?> { [nameof(ProductDetail.Id)]=productId }));
            return result.ToHtmlString();
        });
        return (html,imageHandler.Calls);
    }
    var rendered = await RenderProduct(true,200);
    Check(rendered.Html.Contains("data:image/jpeg;base64,") && rendered.Html.Contains("Approved Crater creative artwork"),
        "Production Product Detail renders approved HTTPS artwork with creative caption");
    Check(rendered.Html.Contains("product-placeholder.png") && rendered.Html.Contains("Original product image"),
        "Null canonical image retains separate original placeholder area");
    Check(!rendered.Html.Contains("crater-controls") && !rendered.Html.Contains("Saved Crater artwork"),
        "Approved display does not expose generation or private history");
    Check(rendered.Requests == 1,"Enabled Product Detail requests only its approved image");
    var unapproved = await RenderProduct(true,404,88);
    Check(!unapproved.Html.Contains("data:image/jpeg;base64,") && !unapproved.Html.Contains("temporarily unavailable") &&
        unapproved.Html.Contains("product-placeholder.png"), "Unapproved product remains normal without extra image or error");
    var disabled = await RenderProduct(false,200);
    Check(disabled.Requests == 0 && !disabled.Html.Contains("data:image/jpeg;base64,"),
        "Web flag disabled makes no approved artwork request");
    var unavailable = await RenderProduct(true,503);
    Check(unavailable.Html.Contains("temporarily unavailable") && unavailable.Html.Contains("product-placeholder.png"),
        "Artwork API failure preserves Product Detail and original placeholder");

    // Synthetic bytes exercise create-only transport; pinned-source validation remains separate.
    var copy = Path.Combine(root,"source"); Directory.CreateDirectory(copy);
    await File.WriteAllBytesAsync(Path.Combine(copy,"artwork.jpg"),jpeg);
    await File.WriteAllTextAsync(Path.Combine(copy,"metadata.json"),"{}");
    var json = Encoding.UTF8.GetBytes("{}");
    handler.Blobs.Clear(); handler.Writes.Clear();
    await DynamoArtworkImporter.CopyCreateOnlyAsync(jpeg,json,container);
    Check(handler.Writes.SequenceEqual(new[]{"artwork/28071cca54564f78b62128c504a3440a/artwork.jpg","artwork/28071cca54564f78b62128c504a3440a/metadata.json"}), "Importer creates exact JPEG then metadata only");
    await Reject<IOException>(() => DynamoArtworkImporter.CopyCreateOnlyAsync(jpeg,json,container), "Existing objects block import without repair");
    Check(handler.Writes.Count == 2, "Replay does not write");
    await Reject<InvalidDataException>(() => DynamoArtworkImporter.ImportAsync(copy, () => throw new Exception("Must not resolve storage")), "Changed source denied before credentials/storage");
    handler.Blobs.Clear(); handler.Writes.Clear(); handler.Public=true;
    await Reject<InvalidDataException>(() => DynamoArtworkImporter.CopyCreateOnlyAsync(jpeg,json,container), "Import rejects public container");
    Check(handler.Writes.Count==0,"Public-container import creates nothing"); handler.Public=false;
    handler.FailName="artwork/28071cca54564f78b62128c504a3440a/metadata.json";
    await Reject<RequestFailedException>(() => DynamoArtworkImporter.CopyCreateOnlyAsync(jpeg,json,container), "Metadata upload failure is explicit");
    Check(handler.Writes.Count==1 && !handler.Blobs.ContainsKey(handler.FailName),"Failed import leaves only unapproved orphan; no manifest");
    handler.FailName=null;
    await Reject<IOException>(() => DynamoArtworkImporter.CopyCreateOnlyAsync(jpeg,json,container), "Partial import is not automatically repaired");
    Console.WriteLine($"{count} approved artwork checks passed. Offline only.");
    return 0;
}
catch(Exception ex) { Console.Error.WriteLine(ex); return 1; }
finally { Directory.Delete(root,true); }

sealed class TestEnvironment(string root) : IWebHostEnvironment
{
    public string ApplicationName {get;set;}="Offline";
    public string EnvironmentName {get;set;}="Production";
    public string ContentRootPath {get;set;}=root;
    public string WebRootPath {get;set;}=root;
    public IFileProvider ContentRootFileProvider {get;set;}=new NullFileProvider();
    public IFileProvider WebRootFileProvider {get;set;}=new NullFileProvider();
}
sealed class CaptureLogger<T>(List<string> messages) : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state) where TState:notnull => null;
    public bool IsEnabled(LogLevel level) => true;
    public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception,
        Func<TState,Exception?,string> formatter) => messages.Add(formatter(state,exception));
}
sealed class CaptureProvider(List<string> messages) : ILoggerProvider
{
    public ILogger CreateLogger(string category) => new CaptureLogger<object>(messages);
    public void Dispose() { }
}
sealed class ClientHandler : HttpMessageHandler
{
    public int Status=200; public byte[] Bytes=[255,216,255,7];
    public int Calls;
    public HttpMethod? Method; public string? Path; public bool LocalHeader;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
    {
        Calls++;
        Method=request.Method; Path=request.RequestUri!.AbsolutePath; LocalHeader=request.Headers.Contains("X-CraterIt-Local");
        var response=new HttpResponseMessage((HttpStatusCode)Status){Content=new ByteArrayContent(Bytes)};
        response.Content.Headers.ContentType=new("image/jpeg"); return Task.FromResult(response);
    }
}
sealed class DisplayProducts(int id) : IProductService
{
    public Task<List<Product>> GetProductsAsync(CancellationToken cancellationToken=default) =>
        Task.FromResult(new List<Product> { new() { ID=id,Name="Dynamo",ImageBlobPath=null } });
}
sealed class DisplayNavigation : NavigationManager
{
    public DisplayNavigation() => Initialize("https://offline.invalid/","https://offline.invalid/products/102");
    protected override void NavigateToCore(string uri,bool forceLoad) => throw new NotSupportedException();
}
sealed class OfflineHandler : HttpMessageHandler
{
    public Dictionary<string,byte[]> Blobs=[]; public List<string> Writes=[];
    public List<string> Methods=[]; public List<string> Paths=[];
    public bool Public; public int? Failure; public string? FailName;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if(request.RequestUri!.Host!="offline.invalid") throw new Exception("Live traffic forbidden");
        var path=request.RequestUri.AbsolutePath.TrimStart('/');
        if(!path.StartsWith("crater-artwork")) throw new Exception("Wrong container");
        var name=path.Length>15 ? path[15..] : "";
        Methods.Add(request.Method.Method); Paths.Add(name);
        HttpResponseMessage Response(int status,byte[]? bytes=null,string? code=null)
        {
            var response=new HttpResponseMessage((HttpStatusCode)status){Content=new ByteArrayContent(bytes??[])};
            response.Headers.TryAddWithoutValidation("ETag","\"offline\"");
            response.Headers.TryAddWithoutValidation("x-ms-request-id","offline");
            response.Headers.TryAddWithoutValidation("x-ms-blob-type","BlockBlob");
            response.Content.Headers.LastModified=DateTimeOffset.UtcNow;
            if(code is not null) response.Headers.TryAddWithoutValidation("x-ms-error-code",code);
            return response;
        }
        if(Failure is {} failure) return Response(failure,code:"AuthorizationFailure");
        if(name=="")
        {
            if(request.Method!=HttpMethod.Get || request.RequestUri.Query!="?restype=container") throw new Exception("Listing forbidden");
            var response=Response(200); if(Public)response.Headers.TryAddWithoutValidation("x-ms-blob-public-access","blob"); return response;
        }
        if(request.Method==HttpMethod.Put)
        {
            if(name==FailName)return Response(500,code:"InternalError");
            if(!request.Headers.TryGetValues("If-None-Match",out var values)||values.Single()!="*")throw new Exception("Overwrite forbidden");
            if(Blobs.ContainsKey(name))return Response(412,code:"ConditionNotMet");
            Blobs[name]=await request.Content!.ReadAsByteArrayAsync(token); Writes.Add(name); return Response(201);
        }
        if(request.Method!=HttpMethod.Get && request.Method!=HttpMethod.Head)throw new Exception("Unexpected write");
        return Blobs.TryGetValue(name,out var bytes)?Response(200,bytes):Response(404,code:"BlobNotFound");
    }
}
