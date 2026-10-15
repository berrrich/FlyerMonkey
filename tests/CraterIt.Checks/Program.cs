using System.Net;
using FlyerMonkey.Api.Controllers;
using FlyerMonkey.Api.Models;
using FlyerMonkey.Api.Services;
using FlyerMonkey.Shared.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;

var checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    Console.WriteLine($"PASS: {name}");
    checks++;
}

async Task<ActionResult> Call(string environment = "Development", bool enabled = true,
    string remote = "127.0.0.1", string host = "localhost:5000", bool header = true,
    string prompt = "product", ImageGenerationService? service = null,
    CancellationToken token = default)
{
    var context = new DefaultHttpContext();
    context.Connection.RemoteIpAddress = IPAddress.Parse(remote);
    context.Request.Host = new HostString(host);
    if (header) context.Request.Headers["X-CraterIt-Local"] = "1";
    // Missing services intentionally fail if a rejected request resolves the paid service.
    context.RequestServices = service is null ? new ServiceCollection().BuildServiceProvider()
        : new ServiceCollection().AddSingleton(service).BuildServiceProvider();
    var controller = new ImagesController { ControllerContext = new ControllerContext { HttpContext = context } };
    var configuration = new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["CraterIt:Enabled"] = enabled.ToString() }).Build();
    return await controller.Generate(new GenerateImageRequest { Prompt = prompt },
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
Check(await Call(service: fake) is FileContentResult { ContentType: "image/jpeg" }, "Local enabled request returns JPEG");
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

var handler = new FakeHandler();
using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000/") };
var client = new ImageGenerationApiService(http);
Check((await client.GenerateAsync("product", default)).Length == 3 && handler.Calls == 1,
    "Client posts once and accepts JPEG");
handler.InvalidImage = true;
try { await client.GenerateAsync("product", default); throw new Exception("Expected invalid image rejection"); }
catch (InvalidOperationException) { Check(true, "Non-JPEG response rejected"); }
try { await client.GenerateAsync("product", cancelled.Token); throw new Exception("Expected cancellation"); }
catch (OperationCanceledException) { Check(true, "Client cancellation honored"); }
using var disabledHttp = new HttpClient(handler);
Check(!new ImageGenerationApiService(disabledHttp).IsEnabled, "MAUI/default client disabled");
using var publicHttp = new HttpClient(handler) { BaseAddress = new Uri("https://public.example/") };
Check(!new ImageGenerationApiService(publicHttp).IsEnabled, "Public API address disabled");
Console.WriteLine($"{checks} checks passed. No provider calls or credentials used.");

sealed class FakeHandler : HttpMessageHandler
{
    public int Calls;
    public bool InvalidImage;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (request.Method != HttpMethod.Post || request.RequestUri?.AbsolutePath != "/api/images/generate" ||
            !request.Headers.Contains("X-CraterIt-Local")) throw new Exception("Invalid request");
        Calls++;
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(InvalidImage ? [1, 2, 3] : [255, 216, 255])
        };
        response.Content.Headers.ContentType = new("image/jpeg");
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
