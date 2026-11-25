using System.Security.Claims;
using FlyerMonkey.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SQLServerConnection.Data;

namespace FlyerMonkey.Api.Controllers;

[ApiController]
[Route("api/customers/crater/{productId:int:min(1)}")]
[AllowAnonymous] // Explicitly enforce CustomerBearer, as in CustomersController; never operator fallback.
public sealed class CustomerCraterController(IConfiguration configuration, IWebHostEnvironment environment) : ControllerBase
{
    private async Task<(Guid Customer, bool Eligible, IActionResult? Error)> Access(bool checkGeneration, CancellationToken token)
    {
        var auth = await Microsoft.AspNetCore.Authentication.AuthenticationHttpContextExtensions.AuthenticateAsync(
            HttpContext, CustomerAuthentication.Scheme);
        if (!auth.Succeeded || auth.Principal is null) return (default, false, Unauthorized());
        var authorization = HttpContext.RequestServices.GetRequiredService<IAuthorizationService>();
        if (!(await authorization.AuthorizeAsync(auth.Principal, null, CustomerAuthentication.Policy)).Succeeded)
            return (default, false, StatusCode(403));
        var tenant = Guid.Parse(auth.Principal.FindFirstValue("tid")!);
        var subject = Guid.Parse(auth.Principal.FindFirstValue("oid")!);
        var repository = HttpContext.RequestServices.GetRequiredService<ICustomerRepository>();
        var account = await repository.GetAsync(tenant, subject, token);
        if (account is null || account.Status != "Active") return (default, false, StatusCode(403));
        if (account.CustomerId != CustomerCraterAccess.AllowedCustomer(configuration))
            return (default, false, StatusCode(403));
        // Existing private images remain owner-readable independently of generation roles.
        var grant = checkGeneration ? await repository.GetGenerationAccessAsync(tenant, subject, token) : null;
        return (account.CustomerId, account.CraterEnabled &&
            grant?.Allows(account.CustomerId, tenant, subject) == true, null);
    }
    [HttpGet("attempt")]
    public Task<IActionResult> Status(int productId, CancellationToken token) => Execute(productId, false, false, token);
    [HttpPost("attempt")]
    public Task<IActionResult> Create(int productId, CancellationToken token) => Execute(productId, true, false, token);
    [HttpPost("classic")]
    public Task<IActionResult> CreateClassic(int productId, CancellationToken token) =>
        Execute(productId, true, false, token, classic: true);
    [HttpGet("image")]
    public Task<IActionResult> Image(int productId, CancellationToken token) => Execute(productId, false, true, token);

    private async Task<IActionResult> Execute(int productId, bool create, bool image, CancellationToken token, bool classic = false)
    {
        try
        {
            Response.Headers.CacheControl = "no-store";
            if (!CustomerCraterAccess.IsLocalHttps(HttpContext, environment) ||
                !CustomerCraterAccess.PreviewEnabled(configuration)) return NotFound();
            var generationEnabled = CustomerCraterAccess.GenerationEnabled(configuration);
            var classicEnabled = generationEnabled && configuration.GetValue<bool>("CustomerCrater:ClassicEnabled");
            if (create && (!generationEnabled || classic && !classicEnabled)) return NotFound();
            var access = await Access(!image && generationEnabled, token);
            if (access.Error is not null) return access.Error;
            if (create && !access.Eligible) return StatusCode(403);
            var store = HttpContext.RequestServices.GetRequiredService<ICustomerCraterStore>();
            var generation = new CustomerCraterGeneration(store,
                retestLimit: configuration.GetValue<int>("CustomerCrater:CompletedAccountRetestLimit", 1),
                unlimited: CustomerCraterAccess.HasUnlimitedCraterIt(configuration, access.Customer));
            if (image)
            {
                if (!generation.Status(access.Customer, access.Eligible, productId).HasImage) return NotFound();
                var bytes = store.ReadImage(access.Customer, productId);
                return bytes is null ? NotFound() : File(bytes, "image/jpeg");
            }
            if (!create)
            {
                var status = generation.Status(access.Customer, access.Eligible, productId);
                return Ok(status with
                {
                    CanGenerateClassic = classicEnabled && access.Eligible &&
                        status.State != "Generating" && store.HasDailyCapacity(DateOnly.FromDateTime(DateTime.UtcNow)),
                    HasClassicImage = HttpContext.RequestServices.GetRequiredService<ClassicCraterArtwork>().Exists(productId)
                });
            }
            var product = await HttpContext.RequestServices.GetRequiredService<ProductService>().GetAsync(productId, token);
            if (product?.ID != productId) return NotFound();
            if (classic)
            {
                using var lease = store.Lock(access.Customer);
                if (lease is null) return Conflict();
                var artwork = HttpContext.RequestServices.GetRequiredService<ClassicCraterArtwork>();
                var saved = await artwork.GenerateAsync(product,
                    () => HttpContext.RequestServices.GetRequiredService<ICustomerCraterProvider>());
                return saved ? NoContent() : StatusCode(429);
            }
            int? expectedVersion = int.TryParse(Request.Query["version"], out var version) ? version : null;
            var result = await generation.GenerateAsync(access.Customer, product,
                () => HttpContext.RequestServices.GetRequiredService<ICustomerCraterProvider>(),
                repeat: Request.Query["repeat"] == "true", expectedVersion: expectedVersion);
            return result.State == "RequestStale" ? Conflict(result) : Ok(result);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Never emit credentials, claims, prompts or provider exception messages.
            return StatusCode(503, new ProblemDetails { Title = "Customer artwork storage or configuration is unavailable. No automatic retry is permitted." });
        }
    }
}
