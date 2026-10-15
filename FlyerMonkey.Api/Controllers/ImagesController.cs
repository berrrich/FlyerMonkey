using FlyerMonkey.Api.Models;
using FlyerMonkey.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace FlyerMonkey.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ImagesController : ControllerBase
{
    private static readonly SemaphoreSlim GenerationSlot = new(1, 1);

    [HttpPost("generate")]
    public async Task<ActionResult> Generate(
        GenerateImageRequest request,
        [FromServices] IWebHostEnvironment environment,
        [FromServices] IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        // Reject before resolving the paid service or reading its credentials.
        if (!environment.IsDevelopment() ||
            !configuration.GetValue<bool>("CraterIt:Enabled") ||
            HttpContext.Connection.RemoteIpAddress is not { } remoteAddress ||
            !System.Net.IPAddress.IsLoopback(remoteAddress) ||
            !Uri.TryCreate($"http://{Request.Host}", UriKind.Absolute, out var host) ||
            !host.IsLoopback || Request.Headers["X-CraterIt-Local"] != "1")
            return NotFound();

        if (string.IsNullOrWhiteSpace(request.Prompt) || request.Prompt.Length > 2000)
            return BadRequest("Prompt must contain between 1 and 2000 characters.");

        if (!await GenerationSlot.WaitAsync(0, cancellationToken))
            return StatusCode(StatusCodes.Status429TooManyRequests);

        try
        {
            var service = HttpContext.RequestServices.GetRequiredService<ImageGenerationService>();
            var result = await service.GenerateAsync(request.Prompt, cancellationToken);
            Response.Headers.CacheControl = "no-store";
            return File(result.ToArray(), "image/jpeg");
        }
        finally
        {
            GenerationSlot.Release();
        }
    }
}
