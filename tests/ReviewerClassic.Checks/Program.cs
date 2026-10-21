using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Azure.Core.Pipeline;
using Azure.Storage.Blobs;
using FlyerMonkey.Reviewer.Windows.Services;
using FlyerMonkey.Reviewer.Windows.Models;

try
{
var checks = 0;
void Check(bool value, string name)
{
    if (!value) throw new Exception(name);
    checks++;
}
async Task<Exception> Fails(Func<Task> action, string fragment)
{
    try { await action(); }
    catch (Exception ex)
    {
        Check(ex.Message.Contains(fragment), "Expected operation: " + fragment);
        Check(!ex.ToString().Contains("SECRET"), "Sensitive details excluded");
        return ex;
    }
    throw new Exception("Expected failure: " + fragment);
}
var encoder = new JpegBitmapEncoder();
encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgr24, null, new byte[3], 3)));
using var encoded = new MemoryStream();
encoder.Save(encoded);
var jpeg = encoded.ToArray();
var extracted = new ExtractedProduct { Brand = "Brand", ProductName = "Milk", Variant = "Full cream", PackSizeText = "2L" };
var classicPrompt = new ProductImageService().BuildImagePrompt(extracted);
Check(classicPrompt == "Create a clean product image for Brand Milk, Full cream, 2L.", "Existing Classic prompt behavior");
ClassicImageValidation.ValidateJpeg("IMAGE/JPEG", jpeg);
Check(true, "Real JPEG decodes");

ClassicImageGenerationService Service(HttpClient http) => new(() => "offline-test-key", http, TimeSpan.FromSeconds(10));
foreach (var status in new[] { 404, 401, 429, 500, 503, 302 })
{
    using var handler = new FakeHandler(_ => new HttpResponseMessage((HttpStatusCode)status)
        { Content = new StringContent("{\"error\":{\"message\":\"SECRET response\",\"type\":\"test\"}}", Encoding.UTF8, "application/json") });
    using var http = new HttpClient(handler);
    var ex = await Fails(() => Service(http).GenerateImageAsync("SECRET prompt"), $"HTTP {status}");
    Check(ex is HttpRequestException { StatusCode: not null }, "Preserve status");
    Check(ex.Message.Contains("POST https://api.openai.com/v1/images/generations"), "Provider endpoint diagnostic");
    Check(handler.Count == 1, "SDK makes exactly one attempt");
}
foreach (var sample in new[] { ("application/json", Encoding.UTF8.GetBytes("{\"SECRET\":1}")),
    ("image/jpeg", Encoding.UTF8.GetBytes("{\"SECRET\":1}")),
    ("image/png", jpeg), ("image/jpeg", new byte[] { 255, 216, 255, 0 }) })
    await Fails(() => { ClassicImageValidation.ValidateJpeg(sample.Item1, sample.Item2); return Task.CompletedTask; }, "validation failed");

using (var handler = new FakeHandler(_ => throw new HttpRequestException("SECRET transport")))
using (var http = new HttpClient(handler))
{
    await Fails(() => Service(http).GenerateImageAsync("SECRET"), "HTTP transport");
    Check(handler.Count == 1, "Transport failure has no SDK retry");
}
using (var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)))
using (var http = new HttpClient(handler))
{
    await Fails(() => { _ = new ClassicImageGenerationService(() => null, http, TimeSpan.FromSeconds(10)); return Task.CompletedTask; }, "FLYERMONKEY_OPENAI_KEY");
    Check(handler.Count == 0, "Missing credentials send no request");
    using var canceled = new CancellationTokenSource();
    canceled.Cancel();
    await Fails(() => Service(http).GenerateImageAsync("SECRET", canceled.Token), "canceled");
    Check(handler.Count == 0, "Pre-canceled call sends no request");
}
foreach (var body in new[] { "{\"data\":[{\"b64_json\":\"not base64 SECRET\"}]}",
    "{\"data\":[{\"b64_json\":\"e30=\"}]}", "{\"SECRET\":", "{\"data\":[{}]}", "{\"data\":[]}", "{}", "null" })
{
    using var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    using var http = new HttpClient(handler);
    await Fails(() => Service(http).GenerateImageAsync("SECRET"), "validation failed");
}
using (var handler = new FakeHandler(request => {
    Check(request.Method == HttpMethod.Post && request.RequestUri!.AbsoluteUri == "https://api.openai.com/v1/images/generations", "Direct fixed provider route");
    using var payload = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
    var root = payload.RootElement;
    Check(root.GetProperty("model").GetString() == "gpt-image-1", "Existing model");
    Check(root.GetProperty("prompt").GetString() == classicPrompt, "Prompt preserved exactly");
    Check(root.GetProperty("size").GetString() == "1024x1024" &&
        root.GetProperty("output_format").GetString() == "jpeg" && root.GetProperty("output_compression").GetInt32() == 80, "JPEG settings preserved");
    Check(!root.TryGetProperty("ProductID", out _), "No SQL ID required");
    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
        JsonSerializer.Serialize(new { created = 1, data = new[] { new { b64_json = Convert.ToBase64String(jpeg) } } }), Encoding.UTF8, "application/json") };
}))
using (var http = new HttpClient(handler))
    Check((await Service(http).GenerateImageAsync(classicPrompt)).SequenceEqual(jpeg), "SDK returns validated JPEG unchanged");

foreach (var cancelByCaller in new[] { false, true })
{
    using var handler = new BlockingHandler();
    using var http = new HttpClient(handler);
    using var caller = new CancellationTokenSource();
    var service = new ClassicImageGenerationService(() => "offline-test-key", http,
        cancelByCaller ? TimeSpan.FromSeconds(10) : TimeSpan.FromMilliseconds(100));
    if (cancelByCaller) caller.CancelAfter(100);
    var ex = await Fails(() => service.GenerateImageAsync("SECRET", caller.Token), cancelByCaller ? "canceled" : "timed out");
    Check(cancelByCaller ? ex is OperationCanceledException : ex is TimeoutException, "Cancellation distinguished from timeout");
    Check(handler.Count == 1, "Cancellation and timeout do not retry");
}

var folder = Path.Combine(Path.GetTempPath(), "ReviewerClassic-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
try
{
    await Fails(() => ClassicImageFileService.SaveAsync(folder, Encoding.UTF8.GetBytes("SECRET JSON")), "validation failed");
    Check(!Directory.EnumerateFiles(folder).Any(), "JSON never written");
    var path = await ClassicImageFileService.SaveAsync(folder, jpeg);
    Check(File.ReadAllBytes(path).SequenceEqual(jpeg), "Valid JPEG saved unchanged");
    await Fails(() => ClassicImageFileService.SaveAsync(path, jpeg), "local saving failed");
    using var handler = new FakeHandler(_ => {
        var response = new HttpResponseMessage(HttpStatusCode.NotFound)
            { Content = new StringContent("<Error><Code>ContainerNotFound</Code><Message>SECRET</Message></Error>", Encoding.UTF8, "application/xml") };
        response.Headers.Add("x-ms-error-code", "ContainerNotFound");
        return response;
    });
    using var http = new HttpClient(handler);
    var options = new BlobClientOptions { Transport = new HttpClientTransport(http) };
    options.Retry.MaxRetries = 0;
    var client = new BlobServiceClient(new Uri("https://offline.invalid/"), options);
    var ex = await Fails(() => new ProductImageUploadService(client).UploadAsync(path), "Azure Blob upload");
    Check(ex.Message.Contains("404") && ex.Message.Contains("ContainerNotFound"), "Safe Blob status and code");
}
finally
{
    foreach (var file in Directory.EnumerateFiles(folder)) File.Delete(file);
    Directory.Delete(folder);
}
Console.WriteLine($"PASS: {checks} offline Reviewer Classic checks; no network requests.");
}
catch (Exception ex)
{
    Console.Error.WriteLine("FAIL: " + ex.Message);
    Environment.ExitCode = 1;
}

sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public int Count { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        Count++;
        return Task.FromResult(respond(request));
    }
}

sealed class BlockingHandler : HttpMessageHandler
{
    public int Count { get; private set; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        Count++;
        await Task.Delay(Timeout.InfiniteTimeSpan, token);
        throw new InvalidOperationException("Unreachable");
    }
}
