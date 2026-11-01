namespace FlyerMonkey.Shared.Services;

public static class CustomerIdentity
{
    public const string TenantId = "bc2e39c7-a794-4172-b5a6-ad9bdb5cd7ab";
    public const string WebClientId = "75ed8832-9bd7-45f3-8701-499ad59cdcb7";
    public const string ApiClientId = "c47581be-9d3d-4ed3-b0ce-7518fb3094c6";
    public const string Authority = "https://flyermonkeycustomers.ciamlogin.com/" + TenantId + "/v2.0";
    public const string MetadataAddress = Authority + "/.well-known/openid-configuration";
    // Verified against this tenant's public discovery document, November 1, 2026.
    public const string Issuer = "https://" + TenantId + ".ciamlogin.com/" + TenantId + "/v2.0";
    public const string Scope = "api://" + ApiClientId + "/Customer.Access";
}
