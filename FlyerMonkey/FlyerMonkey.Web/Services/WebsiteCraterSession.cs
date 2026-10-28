using FlyerMonkey.Shared.Services;
using Microsoft.AspNetCore.Components.Authorization;

namespace FlyerMonkey.Web.Services;

public sealed class WebsiteCraterSession(IServiceProvider services) : IWebsiteCraterSession
{
    public async Task<bool> IsAuthenticatedAsync()
    {
        var state = services.GetService<AuthenticationStateProvider>();
        return state is not null && (await state.GetAuthenticationStateAsync()).User.Identity?.IsAuthenticated == true;
    }
}
