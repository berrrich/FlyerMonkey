#if ANDROID
using FlyerMonkey.Shared.Services;
using Microsoft.Identity.Client;

namespace FlyerMonkey.Services;

// Nonsecret values only. Remains disabled until owner supplies real registration IDs.
public sealed record AndroidOperatorOptions(bool Enabled = false, string TenantId = "", string ClientId = "",
    string ApiClientId = "", string RedirectScheme = "flyermonkey-unconfigured",
    string ApiBaseUrl = PublicApiClientRegistration.ApiBaseUrl);

public sealed class AndroidOperatorAuthentication(AndroidOperatorOptions options) : IOperatorTokenProvider, IOperatorSession
{
    public bool IsEnabled => options.Enabled;
    private IPublicClientApplication? application;
    private IPublicClientApplication Application
    {
        get
        {
            if (!options.Enabled || new[] { options.TenantId, options.ClientId, options.ApiClientId }
                .Any(value => !Guid.TryParse(value, out var id) || id == Guid.Empty) ||
                options.RedirectScheme != $"msal{options.ClientId}")
                throw new InvalidOperationException("Android operator authentication is not configured.");
            return application ??= PublicClientApplicationBuilder.Create(options.ClientId)
                .WithAuthority($"https://login.microsoftonline.com/{options.TenantId}")
                .WithRedirectUri($"{options.RedirectScheme}://auth")
                .Build();
        }
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var app = Application;
        var accounts = (await app.GetAccountsAsync()).ToArray();
        if (accounts.Length != 1) throw new InvalidOperationException("Explicit operator sign-in is required.");
        // No automatic interactive UI from a background HTTP request.
        return (await app.AcquireTokenSilent([$"api://{options.ApiClientId}/Crater.Access"], accounts[0])
            .ExecuteAsync(cancellationToken)).AccessToken;
    }

    public async Task SignInAsync(CancellationToken cancellationToken = default)
    {
        await Application.AcquireTokenInteractive([$"api://{options.ApiClientId}/Crater.Access"])
            .WithParentActivityOrWindow(Platform.CurrentActivity)
            .WithUseEmbeddedWebView(false)
            .WithPrompt(Prompt.SelectAccount)
            .ExecuteAsync(cancellationToken);
        // The API, not the account picker or email, authorizes the operator.
    }

    public async Task SignOutAsync()
    {
        foreach (var account in await Application.GetAccountsAsync()) await Application.RemoveAsync(account);
        // Removes this app's cache, not the shared system-browser identity-provider session.
    }
}
#endif
