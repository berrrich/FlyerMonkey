using System.Security.Claims;
using FlyerMonkey.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SQLServerConnection.Data;

namespace FlyerMonkey.Api.Controllers;

[ApiController]
[Route("api/customers")]
[AllowAnonymous] // Select and enforce only CustomerBearer below, never the operator fallback policy.
public sealed class CustomersController : ControllerBase
{
    [HttpGet("me")]
    public Task<IActionResult> Me(IConfiguration configuration, CancellationToken token) => Account(configuration, false, token);

    [HttpPost("session")]
    public Task<IActionResult> Session(IConfiguration configuration, CancellationToken token) => Account(configuration, true, token);

    private async Task<IActionResult> Account(IConfiguration configuration, bool provision, CancellationToken token)
    {
        Response.Headers.CacheControl = "no-store";
        // Disabled deployments have no customer scheme/policy and must fail before resolving either.
        if (!configuration.GetValue<bool>("CustomerAuthentication:Enabled")) return NotFound();
        var authorization = HttpContext.RequestServices.GetRequiredService<IAuthorizationService>();
        var authentication = await Microsoft.AspNetCore.Authentication.AuthenticationHttpContextExtensions
            .AuthenticateAsync(HttpContext, CustomerAuthentication.Scheme);
        if (!authentication.Succeeded || authentication.Principal is null) return Unauthorized();
        if (!(await authorization.AuthorizeAsync(authentication.Principal, null, CustomerAuthentication.Policy)).Succeeded)
            return Forbid(CustomerAuthentication.Scheme);
        var user = authentication.Principal;
        var repository = HttpContext.RequestServices.GetRequiredService<ICustomerRepository>();
        try
        {
            var tenant = Guid.Parse(user.FindFirstValue("tid")!);
            var subject = Guid.Parse(user.FindFirstValue("oid")!);
            var account = provision ? await repository.ProvisionAsync(tenant, subject, token) : await repository.GetAsync(tenant, subject, token);
            if (account is null) return NotFound();
            if (account.Status != "Active") return StatusCode(403);
            // Stored CraterEnabled is intentionally not a grant in this mission.
            return Ok(account with { CraterEnabled = false });
        }
        catch (Microsoft.Data.SqlClient.SqlException)
        {
            return StatusCode(503, new ProblemDetails { Title = "Customer account storage is unavailable." });
        }
    }
}
