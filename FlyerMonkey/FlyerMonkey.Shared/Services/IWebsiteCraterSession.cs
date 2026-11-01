namespace FlyerMonkey.Shared.Services;

/// <summary>Web-only presentation state; never grants an API permission.</summary>
public interface IWebsiteCraterSession : ICustomerSession
{
    Task<bool> IsAuthenticatedAsync();
}
