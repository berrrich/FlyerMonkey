using System.Security.Cryptography;
using System.Text.Json;
using FlyerMonkey.Api.Models;
using FlyerMonkey.Api.Services;
using FlyerMonkey.Shared.Model;
using FlyerMonkey.Shared.Services;
using Microsoft.AspNetCore.Mvc;

namespace FlyerMonkey.Api.Controllers;

[ApiController]
[Microsoft.AspNetCore.Authorization.Authorize(Policy = CraterPermissions.Admin)]
[Route("api/images/artwork/development-fixture")]
public sealed class DevelopmentCraterFixturesController : ControllerBase
{
    private static readonly SemaphoreSlim ImportSlot = CraterArtworkWriteSlot.Instance;

    [HttpPost]
    [RequestSizeLimit(1024)]
    public async Task<ActionResult> Import(ImportDevelopmentFixtureRequest request,
        [FromServices] IWebHostEnvironment environment, [FromServices] IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        // All guards run before product, storage or provider resolution. No image-generation service is used here.
        if (configuration.GetValue<bool>("Authentication:Enabled") ||
            !CraterItLocalAccess.Allows(HttpContext, environment, configuration) ||
            !configuration.GetValue<bool>("CraterIt:FixtureImportEnabled") ||
            configuration["CraterArt:Provider"] != "Azure" ||
            !int.TryParse(configuration["CraterIt:FixtureProductId"], out var allowedProduct) || allowedProduct <= 0)
            return NotFound();

        Response.Headers.CacheControl = "no-store";
        if (request.RequestId == Guid.Empty || request.ArtworkId == Guid.Empty || request.ProductID != allowedProduct)
            return BadRequest("Nonempty artwork/request IDs and the explicitly enabled fixture product are required.");
        if (!await ImportSlot.WaitAsync(0, cancellationToken)) return StatusCode(429);
        try
        {
            var store = HttpContext.RequestServices.GetRequiredService<ICraterArtworkStore>();
            var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
                { FixtureVersion = 1, request.ArtworkId, request.ProductID, DevelopmentCraterFixture.Sha256 })));
            var existing = await store.FindRequestAsync(request.RequestId, cancellationToken);
            if (existing is not null)
            {
                if (existing.ArtworkId != request.ArtworkId || existing.ProductID != request.ProductID || existing.RequestFingerprint != fingerprint)
                    return Conflict("Request ID already used by another artwork or payload.");
                var saved = await store.ReadAsync(existing.ArtworkId, cancellationToken);
                if (saved is null || Convert.ToHexString(SHA256.HashData(saved)) != DevelopmentCraterFixture.Sha256)
                    return StatusCode(503, new ProblemDetails { Status = 503, Title = "Known fixture is missing or differs; no repair or overwrite attempted." });
                return Ok(existing);
            }
            if (await store.ReadAsync(request.ArtworkId, cancellationToken) is not null)
                return Conflict("Artwork ID already exists; overwrite refused.");

            var product = await HttpContext.RequestServices.GetRequiredService<ProductService>()
                .GetAsync(request.ProductID, cancellationToken);
            if (product is null) return NotFound("Fixture product does not exist.");
            cancellationToken.ThrowIfCancellationRequested();
            var productName = "[DEV FIXTURE] " + product.Name;
            var artwork = new CraterArtwork(request.ArtworkId, request.RequestId, request.ProductID,
                productName[..Math.Min(productName.Length, 1000)], CraterStyle.Classic,
                "Development fixture (not generated)", DateTimeOffset.UtcNow, "image/jpeg", "artwork.jpg", 1, fingerprint);
            var jpeg = DevelopmentCraterFixture.GetJpeg();
            using var saveTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await store.SaveAsync(artwork, jpeg, saveTimeout.Token);
            return Ok(artwork);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException ||
            ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            HttpContext.RequestServices.GetRequiredService<ILogger<DevelopmentCraterFixturesController>>()
                .LogError(ex, "Development fixture import failed for request {RequestId}", request.RequestId);
            return StatusCode(503, new ProblemDetails { Status = 503, Title = "Fixture could not be saved. Inspect storage before any retry." });
        }
        finally { ImportSlot.Release(); }
    }
}
