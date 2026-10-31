using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using System.Text.Json;
using FlyerMonkey.Api.Services;
using OpenAI;

var checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    checks++;
    Console.WriteLine("PASS: " + message);
}

IConfiguration Config(string? model = null, bool includeModel = false, string? key = "offline-dummy-key")
{
    var entries = new Dictionary<string, string?> { ["OpenAI:ApiKey"] = key };
    if (includeModel) entries["OpenAI:ImageModel"] = model;
    return new ConfigurationBuilder().AddInMemoryCollection(entries).Build();
}

foreach (var model in new[] { (string?)null, "gpt-image-1.5", "  gpt-image-1-mini  " })
{
    // Real SDK and production service, dummy credentials and fake transport: no socket/network calls.
    using var handler = new OfflineImages();
    using var http = new HttpClient(handler);
    var options = new OpenAIClientOptions { Transport = new HttpClientPipelineTransport(http) };
    var service = new ImageGenerationService(Config(model, model is not null), options);
    Check(handler.Calls == 0, "Constructing the service never sends a request");
    var image = await service.GenerateAsync("Offline serialization test");
    var expectedModel = model?.Trim() ?? "gpt-image-1.5";
    using var payload = JsonDocument.Parse(handler.Body!);
    var body = payload.RootElement;
    Check(service.Model == expectedModel && body.GetProperty("model").GetString() == expectedModel,
        "SDK sends configured/default model: " + expectedModel);
    Check(body.GetProperty("size").GetString() == "1024x1024" &&
        body.GetProperty("output_format").GetString() == "jpeg" &&
        body.GetProperty("output_compression").GetInt32() == 80,
        "SDK preserves size, JPEG output and compression options");
    Check(handler.Calls == 1 && image.ToArray().SequenceEqual(new byte[] { 255, 216, 255 }),
        "SDK decodes mocked image response with one fake request");
}
foreach (var blank in new[] { "", "   " })
{
    try { _ = new ImageGenerationService(Config(blank, true)); throw new Exception("Expected blank model rejection"); }
    catch (InvalidOperationException ex) { Check(ex.Message.Contains("OpenAI:ImageModel"), "Explicit blank model fails before provider call"); }
}
try { _ = new ImageGenerationService(Config(key: null)); throw new Exception("Expected missing key rejection"); }
catch (InvalidOperationException ex) { Check(ex.Message == "OpenAI API key is not configured.", "Missing key fails without revealing secrets"); }
Console.WriteLine($"{checks} image configuration checks passed. Dummy credentials and in-memory transport only; no paid calls.");

sealed class OfflineImages : HttpMessageHandler
{
    public int Calls;
    public string? Body;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Post || request.RequestUri?.AbsolutePath != "/v1/images/generations")
            throw new Exception("Unexpected SDK endpoint");
        Calls++;
        Body = await request.Content!.ReadAsStringAsync(cancellationToken);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"created\":1,\"data\":[{\"b64_json\":\"/9j/\"}]}", Encoding.UTF8, "application/json")
        };
    }
}
