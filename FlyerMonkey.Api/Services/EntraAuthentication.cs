using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace FlyerMonkey.Api.Services;

public static class EntraAuthentication
{
    public static IServiceCollection AddFlyerMonkeyAuthentication(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.RequireHttpsMetadata = true;
            options.IncludeErrorDetails = false;
            if (configuration.GetValue<bool>("Authentication:Enabled"))
            {
                var tenant = RequiredId(configuration, "Authentication:TenantId");
                var audience = RequiredId(configuration, "Authentication:ApiClientId");
                _ = RequiredId(configuration, "Authentication:OperatorObjectId");
                var clients = configuration.GetSection("Authentication:AllowedClientIds").Get<string[]>() ?? [];
                if (clients.Length == 0 || clients.Any(c => !Guid.TryParse(c, out var id) || id == Guid.Empty))
                    throw new InvalidOperationException("Configure explicit allowed client IDs.");
                var permissions = configuration.GetSection("Authentication:OperatorPermissions").Get<string[]>() ?? [];
                if (permissions.Any(p => !CraterPermissions.All.Contains(p, StringComparer.Ordinal)))
                    throw new InvalidOperationException("Unknown operator permission.");
                options.Authority = $"https://login.microsoftonline.com/{tenant}/v2.0";
                options.Audience = audience;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = options.Authority,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateIssuerSigningKey = true,
                    RequireSignedTokens = true,
                    RequireExpirationTime = true,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256]
                };
            }
            else
            {
                // No discovery or credentials when unconfigured. Every bearer token is rejected.
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true,
                    RequireSignedTokens = true, ValidateIssuerSigningKey = true,
                    IssuerSigningKeyResolver = (_, _, _, _) => []
                };
            }
        });
        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
            foreach (var permission in CraterPermissions.All)
                options.AddPolicy(permission, policy => policy.AddRequirements(new CraterPermission(permission)));
        });
        services.AddSingleton<IAuthorizationHandler, CraterPermissionHandler>();
        return services;
    }

    private static string RequiredId(IConfiguration configuration, string key) =>
        Guid.TryParse(configuration[key], out var id) && id != Guid.Empty ? id.ToString("D") :
            throw new InvalidOperationException($"Configure {key} with a nonempty GUID.");
}
