using FlyerMonkey.Api.Models;
using FlyerMonkey.Api.Services;
using Microsoft.AspNetCore.Mvc;
using FlyerMonkey.Shared.Model;
using FlyerMonkey.Shared.Services;
using System.Security.Cryptography;
using System.Text.Json;
using System.Diagnostics;
using System.Globalization;

namespace FlyerMonkey.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ImagesController : ControllerBase
{
    private static readonly SemaphoreSlim GenerationSlot = CraterArtworkWriteSlot.Instance;

    [HttpPost("generate")]
    public async Task<ActionResult> Generate(
        GenerateImageRequest request,
        [FromServices] IWebHostEnvironment environment,
        [FromServices] IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        // Reject before resolving the paid service or reading its credentials.
        if (!CraterItLocalAccess.Allows(HttpContext, environment, configuration))
            return NotFound();

        Response.Headers.CacheControl = "no-store";
        if (request.RequestId == Guid.Empty || request.ProductID <= 0 ||
            string.IsNullOrWhiteSpace(request.ProductName) || request.ProductName.Length > 1000 ||
            !CraterItPromptBuilder.Presets.Any(p => p.Style == request.Preset))
            return BadRequest("Invalid artwork metadata.");

        if (string.IsNullOrWhiteSpace(request.Prompt) || request.Prompt.Length > 2000)
            return BadRequest("Prompt must contain between 1 and 2000 characters.");

        if (!await GenerationSlot.WaitAsync(0, cancellationToken))
            return StatusCode(StatusCodes.Status429TooManyRequests);

        try
        {
            var store = HttpContext.RequestServices.GetRequiredService<ICraterArtworkStore>();
            var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
                { request.ProductID, request.ProductName, request.Preset, request.Prompt })));
            var existing = await store.FindRequestAsync(request.RequestId, cancellationToken);
            if (existing is not null)
            {
                if (existing.RequestFingerprint != fingerprint) return Conflict("Request ID already used.");
                if (await store.ReadAsync(existing.ArtworkId, cancellationToken) is null)
                    return StatusCode(503, new ProblemDetails { Status = 503, Title = "Previously saved artwork is missing. No new image was generated." });
                Timings("replay", 0, 0);
                return Ok(existing);
            }
            var service = HttpContext.RequestServices.GetRequiredService<ImageGenerationService>();
            var phase = Stopwatch.StartNew();
            var result = await service.GenerateAsync(request.Prompt, cancellationToken);
            var generationMs = phase.Elapsed.TotalMilliseconds;
            var artwork = new CraterArtwork(Guid.NewGuid(), request.RequestId, request.ProductID,
                request.ProductName, request.Preset, CraterItPromptBuilder.GetPreset(request.Preset).Name,
                DateTimeOffset.UtcNow, "image/jpeg", "artwork.jpg", 1, fingerprint);
            // Once bytes exist, browser cancellation must not discard them. Bound disk work independently.
            using var saveTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            phase.Restart();
            await store.SaveAsync(artwork, result.ToArray(), saveTimeout.Token);
            Timings("fresh", generationMs, phase.Elapsed.TotalMilliseconds);
            return Ok(artwork);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException ||
            ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            HttpContext.RequestServices.GetRequiredService<ILogger<ImagesController>>()
                .LogError(ex, "Crater artwork persistence failed for request {RequestId}", request.RequestId);
            return StatusCode(503, new ProblemDetails { Status = 503, Title = "Artwork could not be saved. No success was recorded." });
        }
        finally
        {
            GenerationSlot.Release();
        }

        void Timings(string outcome, double generationMs, double saveMs)
        {
            Response.Headers["X-CraterIt-Outcome"] = outcome;
            Response.Headers["X-CraterIt-Generation-Ms"] = generationMs.ToString("F1", CultureInfo.InvariantCulture);
            Response.Headers["X-CraterIt-Save-Ms"] = saveMs.ToString("F1", CultureInfo.InvariantCulture);
        }
    }

    [HttpGet("artwork")]
    public async Task<ActionResult> List(int productId, [FromServices] IWebHostEnvironment environment,
        [FromServices] IConfiguration configuration, CancellationToken cancellationToken)
    {
        if (!CraterItLocalAccess.Allows(HttpContext, environment, configuration)) return NotFound();
        Response.Headers.CacheControl = "no-store";
        if (productId <= 0) return BadRequest("Invalid product ID.");
        var store = HttpContext.RequestServices.GetRequiredService<ICraterArtworkStore>();
        return Ok(await store.ListAsync(productId, cancellationToken));
    }

    [HttpGet("artwork/{artworkId}/image")]
    public async Task<ActionResult> Image(string artworkId, [FromServices] IWebHostEnvironment environment,
        [FromServices] IConfiguration configuration, CancellationToken cancellationToken)
    {
        if (!CraterItLocalAccess.Allows(HttpContext, environment, configuration)) return NotFound();
        Response.Headers.CacheControl = "no-store";
        if (!Guid.TryParseExact(artworkId, "N", out var id) || id == Guid.Empty) return NotFound();
        var store = HttpContext.RequestServices.GetRequiredService<ICraterArtworkStore>();
        var bytes = await store.ReadAsync(id, cancellationToken);
        return bytes is null ? NotFound() : File(bytes, "image/jpeg");
    }
}
