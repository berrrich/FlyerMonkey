using System.Security.Claims;
using FlyerMonkey.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlyerMonkey.Api.Controllers;

/// <summary>Local smoke-test evidence only. Evaluates policies without executing any private action.</summary>
[ApiController]
[Route("api/operator/access")]
[Authorize(Policy = CraterPermissions.ViewOwn)]
public sealed class OperatorAccessController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromServices] IWebHostEnvironment environment,
        [FromServices] IConfiguration configuration, [FromServices] IAuthorizationService authorization)
    {
        Response.Headers.CacheControl = "no-store";
        if (!environment.IsDevelopment() || !configuration.GetValue<bool>("Authentication:Enabled")) return NotFound();
        var permissions = new Dictionary<string, bool>();
        foreach (var permission in CraterPermissions.All)
            permissions[permission] = (await authorization.AuthorizeAsync(User, HttpContext, permission)).Succeeded;
        return Ok(new
        {
            Authenticated = User.Identity?.IsAuthenticated == true,
            TenantMatches = Guid.TryParse(User.FindFirstValue("tid"), out var tenant) &&
                Guid.TryParse(configuration["Authentication:TenantId"], out var expectedTenant) && tenant == expectedTenant,
            OperatorMatches = Guid.TryParse(User.FindFirstValue("oid"), out var subject) &&
                Guid.TryParse(configuration["Authentication:OperatorObjectId"], out var expectedSubject) && subject == expectedSubject,
            Permissions = permissions,
            ProductionGenerationHardBlocked = true
        });
    }
}
