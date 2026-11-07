using FlyerMonkey.Shared.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Authentication;
using FlyerMonkey.Shared.Model;
using Microsoft.AspNetCore.Components;

namespace FlyerMonkey.Web.Services;

public sealed class WebsiteCraterSession(IServiceProvider services) : IWebsiteCraterSession
{
    public async Task<bool> IsAuthenticatedAsync()
    {
        var state = services.GetService<AuthenticationStateProvider>();
        if (state is null) return false;
        var user = (await state.GetAuthenticationStateAsync()).User;
        return user.Identity?.IsAuthenticated == true && !CustomerAuthentication.IsCustomer(user);
    }

    // Persist only presentation booleans across prerender -> circuit; never an identity or authorization grant.
    [PersistentState]
    public CustomerSessionState? CustomerPresentation { get; set; }
    public Task<CustomerSessionState> GetCustomerAsync() => Task.FromResult(CustomerPresentation ??
        (!CustomerEnabled ? new(false, false) : throw new InvalidOperationException(
            "Customer presentation was not captured before rendering. Reload the page to establish a new session.")));

    private bool CustomerEnabled => services.GetService<IConfiguration>()?.GetValue<bool>("CustomerAuthentication:Enabled") == true;

    // Called by request middleware, never by a component or an interactive circuit.
    public async Task CaptureCustomerAsync(HttpContext context)
    {
        if (!CustomerEnabled) { CustomerPresentation = new(false, false); return; }
        if (context.Response.HasStarted)
            throw new InvalidOperationException("Customer authentication must be captured before the response starts.");
        var authentication = await context.AuthenticateAsync(CustomerAuthentication.Cookie);
        CustomerPresentation = new(true, authentication.Succeeded && CustomerAuthentication.IsCustomer(authentication.Principal));
    }
}
