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

    private Task<CustomerSessionState>? _customer;
    // Persist only presentation booleans across prerender -> circuit; never an identity or authorization grant.
    [PersistentState]
    public CustomerSessionState? CustomerPresentation { get; set; }
    public Task<CustomerSessionState> GetCustomerAsync() => CustomerPresentation is { } state
        ? Task.FromResult(state) : _customer ??= ReadCustomer();

    private async Task<CustomerSessionState> ReadCustomer()
    {
        var enabled = services.GetService<IConfiguration>()?.GetValue<bool>("CustomerAuthentication:Enabled") == true;
        var context = services.GetService<IHttpContextAccessor>()?.HttpContext;
        if (!enabled || context is null) return CustomerPresentation = new(enabled, false);
        var authentication = await context.AuthenticateAsync(CustomerAuthentication.Cookie);
        return CustomerPresentation = new(true, authentication.Succeeded && CustomerAuthentication.IsCustomer(authentication.Principal));
    }
}
