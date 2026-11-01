using FlyerMonkey.Shared.Model;

namespace FlyerMonkey.Shared.Services;

// Platform-neutral presentation contract. Tokens and provider SDKs remain in platform adapters.
public interface ICustomerSession
{
    Task<CustomerSessionState> GetCustomerAsync() => Task.FromResult(new CustomerSessionState(false, false));
}
