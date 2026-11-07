using System.Security.Claims;
using FlyerMonkey.Shared.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace FlyerMonkey.Api.Services;

public static class CustomerAuthentication
{
    public const string Scheme = "CustomerBearer";
    public const string Policy = "Customer.Access";

    public static bool AddCustomerAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        if (!configuration.GetValue<bool>("CustomerAuthentication:Enabled")) return false;
        services.AddAuthentication().AddJwtBearer(Scheme, options =>
        {
            options.Authority = CustomerIdentity.Authority;
            options.MetadataAddress = CustomerIdentity.MetadataAddress;
            options.MapInboundClaims = false;
            options.RequireHttpsMetadata = true;
            options.IncludeErrorDetails = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true, ValidIssuer = CustomerIdentity.Issuer,
                IssuerValidator = (issuer, _, _) => issuer == CustomerIdentity.Issuer ? issuer :
                    throw new SecurityTokenInvalidIssuerException("Customer issuer rejected."),
                ValidateAudience = true,
                // Only the two registered identifiers for this one API; v2 tokens normally use its GUID.
                ValidAudiences = [CustomerIdentity.ApiClientId, "api://" + CustomerIdentity.ApiClientId],
                ValidateIssuerSigningKey = true, RequireSignedTokens = true,
                ValidateLifetime = true, RequireExpirationTime = true,
                ClockSkew = TimeSpan.FromSeconds(30), ValidAlgorithms = [SecurityAlgorithms.RsaSha256]
            };
        });
        services.AddAuthorization(options => options.AddPolicy(Policy, policy =>
            policy.AddAuthenticationSchemes(Scheme).RequireAuthenticatedUser().RequireAssertion(context =>
                context.User.FindFirstValue("tid") == CustomerIdentity.TenantId &&
                Guid.TryParse(context.User.FindFirstValue("oid"), out var user) && user != Guid.Empty &&
                context.User.FindFirstValue("ver") == "2.0" &&
                IsAllowedClient(context.User.FindFirstValue("azp"), configuration) &&
                context.User.FindFirstValue("scp")?.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Contains("Customer.Access", StringComparer.Ordinal) == true)));
        return true;
    }

    public static bool IsAllowedClient(string? client, IConfiguration configuration) =>
        client == CustomerIdentity.WebClientId ||
        (Guid.TryParse(configuration["CustomerAuthentication:MobileClientId"], out var mobile) &&
         mobile != Guid.Empty && mobile.ToString() != CustomerIdentity.ApiClientId &&
         Guid.TryParse(client, out var actor) && actor == mobile);
}
