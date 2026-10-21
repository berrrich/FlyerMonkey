using System.Net;
using Azure.Core.Pipeline;
using Azure.Storage.Blobs;
using FlyerMonkey.Api.Services;
using FlyerMonkey.Shared.Model;
using FlyerMonkey.Shared.Services;
using SQLServerConnection.Data;

var checks = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; }
var handler = new OfflineBlobHandler();
using var transportHttp = new HttpClient(handler);
var blob = new BlobServiceClient(new Uri("https://offline.invalid/"), new BlobClientOptions
    { Transport = new HttpClientTransport(transportHttp), Retry = { MaxRetries = 0 } });
var images = new ProductImageService(blob);
var repository = new Products();
var builder = WebApplication.CreateBuilder();
builder.Logging.ClearProviders();
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Services.AddControllers();
builder.Services.AddSingleton<IProductRepository>(repository);
builder.Services.AddSingleton<ProductService>();
builder.Services.AddSingleton(images);
builder.Services.AddSingleton<IOfferRepository, Offers>();
await using var app = builder.Build();
app.MapControllers();
try
{
    await app.StartAsync();
    using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
    foreach (var path in new[] { "api/products", "api/products/1", "api/offers", "api/offers/current", "api/offers/previous" })
        Check((await http.GetAsync(path)).StatusCode == HttpStatusCode.OK, "Anonymous GET " + path);
    foreach (var payload in new[] { "{}", "{\"Name\":\"Injected\",\"ImageBlobPath\":\"crater-artwork/artwork/private/artwork.jpg\"}" })
    {
        using var response = await http.PostAsync("api/products", new StringContent(payload, System.Text.Encoding.UTF8, "application/json"));
        Check(response.StatusCode == HttpStatusCode.MethodNotAllowed, "POST disabled");
    }
    Check(repository.Writes == 0, "No SQL writes");
    foreach (var invalid in new[] { "crater-artwork/artwork/private/artwork.jpg", "other/image.jpg", "", "product-images/", "/product-images/a.jpg", "PRODUCT-IMAGES/a.jpg", "product-images/../crater-artwork/a.jpg", "product-images/a/../../a", "product-images/%2e%2e/a", "product-images/a%2fb", "product-images/a\\b", "product-images//a", "product-images/./a", "product-images/a?sig=x", "product-images/a#x", "https://offline.invalid/product-images/a", "product-images/ a", "product-images/a\n" })
    {
        repository.Path = invalid;
        var before = handler.Calls;
        Check((await http.GetAsync("api/products/1/image")).StatusCode == HttpStatusCode.NotFound, "Reject " + invalid);
        Check(handler.Calls == before, "Rejected paths never reach storage");
    }
    foreach (var valid in new[] { "product-images/photo.jpg", "product-images/folder/product photo.jpg" })
    {
        repository.Path = valid;
        using var response = await http.GetAsync("api/products/1/image");
        Check(response.StatusCode == HttpStatusCode.OK && response.Content.Headers.ContentType?.MediaType == "image/jpeg" &&
            (await response.Content.ReadAsByteArrayAsync()).SequenceEqual(new byte[] { 255, 216, 255, 217 }), "Canonical JPEG bytes preserved");
    }
    repository.Path = "product-images/missing.jpg";
    Check((await http.GetAsync("api/products/1/image")).StatusCode == HttpStatusCode.NotFound, "Missing canonical image");
    Console.WriteLine($"{checks} checks passed; fake SQL and offline Blob transport only.");
    return 0;
}
catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
finally { await app.StopAsync(); }

sealed class Products : IProductRepository
{
    public string Path = "product-images/photo.jpg";
    public int Writes;
    public Task<List<Product>> GetProductsAsync(CancellationToken cancellationToken = default) => Task.FromResult(new List<Product> { new() { ID = 1, Name = "Test", ImageBlobPath = Path } });
    public Task<int> AddProductAsync(Product product, CancellationToken cancellationToken = default) { Writes++; throw new Exception("Writes forbidden"); }
}
sealed class Offers : IOfferRepository
{
    public Task<List<OfferSummary>> GetOfferSummariesAsync(CancellationToken cancellationToken = default) => Task.FromResult(new List<OfferSummary> { new() { ProductID = 1, ProductName = "Test" } });
    public Task<List<OfferSummary>> GetCurrentOfferSummariesAsync(CancellationToken cancellationToken = default) => GetOfferSummariesAsync(cancellationToken);
    public Task<List<OfferSummary>> GetPreviousOfferSummariesAsync(CancellationToken cancellationToken = default) => GetOfferSummariesAsync(cancellationToken);
}
sealed class OfflineBlobHandler : HttpMessageHandler
{
    public int Calls;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        Calls++;
        if (request.RequestUri!.Host != "offline.invalid" || !request.RequestUri.AbsolutePath.StartsWith("/product-images/", StringComparison.Ordinal) ||
            request.Method != HttpMethod.Head && request.Method != HttpMethod.Get) throw new Exception("Forbidden storage operation");
        var response = new HttpResponseMessage(request.RequestUri.AbsolutePath.EndsWith("missing.jpg") ? HttpStatusCode.NotFound : HttpStatusCode.OK)
            { Content = new ByteArrayContent([255, 216, 255, 217]) };
        response.Headers.TryAddWithoutValidation("ETag", "\"test\"");
        if (response.StatusCode == HttpStatusCode.NotFound)
            response.Headers.TryAddWithoutValidation("x-ms-error-code", "BlobNotFound");
        response.Headers.TryAddWithoutValidation("x-ms-blob-type", "BlockBlob");
        response.Content.Headers.TryAddWithoutValidation("Last-Modified", "Wed, 21 Oct 2026 00:00:00 GMT");
        response.Content.Headers.ContentLength = 4;
        return Task.FromResult(response);
    }
}
