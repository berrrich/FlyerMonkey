using System.Text.Json;
using FlyerMonkey.Api.Models;
using FlyerMonkey.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace FlyerMonkey.Api.Controllers;

[ApiController]
[Route("api/products/{productId:int}/approved-artwork/image")]
public sealed class ApprovedArtworkController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(int productId, [FromServices] IConfiguration configuration,
        [FromServices] IWebHostEnvironment environment, CancellationToken token)
    {
        Response.Headers.CacheControl = "no-store";
        var logger = HttpContext.RequestServices.GetRequiredService<ILogger<ApprovedArtworkController>>();
        using var scope = logger.BeginScope(new Dictionary<string, object>
            { ["ProductId"] = productId, ["RequestTrace"] = HttpContext.TraceIdentifier });
        var stage = "feature-flag";
        logger.LogInformation("Approved artwork stage {Stage}", "controller-entered");
        if (productId <= 0)
        {
            logger.LogWarning("Approved artwork 404 stage {Stage}", "invalid-product");
            return NotFound();
        }
        try
        {
            if (!configuration.GetValue<bool>("ApprovedArtwork:Enabled"))
            {
                logger.LogWarning("Approved artwork 404 stage {Stage}", "feature-disabled");
                return NotFound();
            }
            // Read each request: no stale approval cache after revocation. Path is never supplied by the caller.
            stage = "manifest-read";
            var path = Path.Combine(environment.ContentRootPath, "approved-artwork.json");
            var manifest = JsonSerializer.Deserialize<ApprovedArtworkManifest>(await System.IO.File.ReadAllTextAsync(path, token))
                ?? throw new InvalidDataException("Missing publication manifest.");
            stage = "manifest-validation";
            manifest.Validate();
            stage = "manifest-match";
            var approval = manifest.Entries.SingleOrDefault(x => x.ProductID == productId);
            if (approval is null)
            {
                logger.LogWarning("Approved artwork 404 stage {Stage}", "product-unapproved");
                return NotFound();
            }
            logger.LogInformation("Approved artwork stage {Stage}", "manifest-matched");
            // No credential or storage resolution for disabled or unapproved requests.
            stage = "reader-resolution";
            var reader = HttpContext.RequestServices.GetRequiredService<ApprovedArtworkReader>();
            stage = "blob-read";
            var bytes = await reader.ReadAsync(approval, token);
            if (bytes is null)
            {
                logger.LogWarning("Approved artwork 404 stage {Stage}", "approved-blob-missing");
                return NotFound();
            }
            logger.LogInformation("Approved artwork stage {Stage}", "jpeg-verified");
            return File(bytes, "image/jpeg");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Do not log exception text: SDK errors can include private Blob URLs and response bodies.
            logger.LogError("Approved artwork 503 stage {Stage}; failure type {FailureType}", stage, ex.GetType().Name);
            return StatusCode(503);
        }
    }
}
