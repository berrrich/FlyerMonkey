using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace FlyerMonkey.Api.Services;

public static class CraterPermissions
{
    public const string Generate = "Crater.Generate";
    public const string ViewOwn = "Crater.ViewOwn";
    public const string Approve = "Crater.Approve";
    public const string Admin = "Crater.Admin";
    public static readonly string[] All = [Generate, ViewOwn, Approve, Admin];

    public static bool Owns(ClaimsPrincipal user, FlyerMonkey.Shared.Model.CraterArtwork artwork) =>
        user.Identity?.IsAuthenticated == true &&
        Guid.TryParse(user.FindFirstValue("tid"), out var tenant) && tenant != Guid.Empty &&
        Guid.TryParse(user.FindFirstValue("oid"), out var subject) && subject != Guid.Empty &&
        artwork.OwnerTenantId == tenant && artwork.OwnerObjectId == subject;
}

public sealed record CraterPermission(string Name) : IAuthorizationRequirement;

public sealed class CraterPermissionHandler(IConfiguration configuration, IWebHostEnvironment environment)
    : AuthorizationHandler<CraterPermission>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, CraterPermission requirement)
    {
        // Preserve only the existing explicitly opted-in loopback development workflow.
        // Enabling identity disables this bypass, including for the local header.
        if (!configuration.GetValue<bool>("Authentication:Enabled"))
        {
            if (context.Resource is HttpContext http && CraterItLocalAccess.Allows(http, environment, configuration))
                context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var user = context.User;
        // A local read-only pilot cannot acquire write/admin grants from stale configuration entries.
        if (configuration.GetValue<bool>("Authentication:ViewOwnOnly") && requirement.Name != CraterPermissions.ViewOwn)
            return Task.CompletedTask;
        if (user.Identity?.IsAuthenticated == true && user.FindFirstValue("ver") == "2.0" &&
            Guid.TryParse(configuration["Authentication:TenantId"], out var expectedTenant) && expectedTenant != Guid.Empty &&
            Guid.TryParse(configuration["Authentication:OperatorObjectId"], out var expectedOperator) && expectedOperator != Guid.Empty &&
            Guid.TryParse(user.FindFirstValue("tid"), out var tenant) && tenant == expectedTenant &&
            Guid.TryParse(user.FindFirstValue("oid"), out var subject) && subject == expectedOperator &&
            user.FindFirstValue("scp")?.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("Crater.Access", StringComparer.Ordinal) == true &&
            configuration.GetSection("Authentication:AllowedClientIds").Get<string[]>()?.Any(client =>
                Guid.TryParse(client, out var allowed) && allowed != Guid.Empty &&
                Guid.TryParse(user.FindFirstValue("azp"), out var actual) && allowed == actual) == true &&
            configuration.GetSection("Authentication:OperatorPermissions").Get<string[]>()?.Contains(requirement.Name, StringComparer.Ordinal) == true)
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
