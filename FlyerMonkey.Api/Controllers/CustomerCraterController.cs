using System.Security.Claims;
using FlyerMonkey.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SQLServerConnection.Data;

namespace FlyerMonkey.Api.Controllers;

[ApiController]
[Route("api/customers/crater/139")]
[AllowAnonymous] // Explicitly enforce CustomerBearer, as in CustomersController; never operator fallback.
public sealed class CustomerCraterController(IConfiguration configuration, IWebHostEnvironment environment) : ControllerBase
{
    private bool LocalGate => environment.IsDevelopment() &&
        configuration.GetValue<bool>("CustomerAuthentication:Enabled") &&
        configuration.GetValue<bool>("CustomerCrater:Enabled") &&
        configuration.GetValue<bool>("CraterIt:Enabled") &&
        HttpContext.Connection.RemoteIpAddress is { } ip && System.Net.IPAddress.IsLoopback(ip) &&
        HttpContext.Request.IsHttps && (HttpContext.Request.Host.Host is "localhost" or "127.0.0.1" or "::1");

    private async Task<(Guid Customer, bool Eligible, IActionResult? Error)> Access(CancellationToken token)
    {
        Response.Headers.CacheControl = "no-store";
        if (!LocalGate) return (default, false, NotFound());
        var auth = await Microsoft.AspNetCore.Authentication.AuthenticationHttpContextExtensions.AuthenticateAsync(
            HttpContext, CustomerAuthentication.Scheme);
        if (!auth.Succeeded || auth.Principal is null) return (default, false, Unauthorized());
        var authorization = HttpContext.RequestServices.GetRequiredService<IAuthorizationService>();
        if (!(await authorization.AuthorizeAsync(auth.Principal, null, CustomerAuthentication.Policy)).Succeeded)
            return (default, false, StatusCode(403));
        var account = await HttpContext.RequestServices.GetRequiredService<ICustomerRepository>().GetAsync(
            Guid.Parse(auth.Principal.FindFirstValue("tid")!), Guid.Parse(auth.Principal.FindFirstValue("oid")!), token);
        if (account is null || account.Status != "Active") return (default, false, StatusCode(403));
        if (!Guid.TryParse(configuration["CustomerCrater:AllowedCustomerId"], out var allowed) ||
            allowed == Guid.Empty || account.CustomerId != allowed) return (default, false, StatusCode(403));
        return (account.CustomerId, account.CraterEnabled, null);
    }
    [HttpGet("attempt")]
    public Task<IActionResult> Status(CancellationToken token) => Execute(false, false, token);
    [HttpPost("attempt")]
    public Task<IActionResult> Create(CancellationToken token) => Execute(true, false, token);
    [HttpGet("image")]
    public Task<IActionResult> Image(CancellationToken token) => Execute(false, true, token);

    private async Task<IActionResult> Execute(bool create, bool image, CancellationToken token)
    {
        try
        {
            var access = await Access(token);
            if (access.Error is not null) return access.Error;
            if (create && !access.Eligible) return StatusCode(403);
            var store = HttpContext.RequestServices.GetRequiredService<ICustomerCraterStore>();
            var generation = new CustomerCraterGeneration(store);
            if (image)
            {
                if (!generation.Status(access.Customer, access.Eligible).HasImage) return NotFound();
                var bytes = store.ReadImage(access.Customer, 139);
                return bytes is null ? NotFound() : File(bytes, "image/jpeg");
            }
            if (!create) return Ok(generation.Status(access.Customer, access.Eligible));
            var product = await HttpContext.RequestServices.GetRequiredService<ProductService>().GetAsync(139, token);
            if (product is null) return NotFound();
            return Ok(await generation.GenerateAsync(access.Customer, product,
                () => HttpContext.RequestServices.GetRequiredService<ICustomerCraterProvider>()));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Never emit credentials, claims, prompts or provider exception messages.
            return StatusCode(503, new ProblemDetails { Title = "Customer artwork storage or configuration is unavailable. No automatic retry is permitted." });
        }
    }
}
