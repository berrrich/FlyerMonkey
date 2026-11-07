using FlyerMonkey.Shared.Model;

namespace FlyerMonkey.Shared.Services;

// Deliberately separate from operator tokens and website cookies.
public interface IMobileCustomerSession
{
    bool SignedIn { get; }
    CustomerAccount? Account { get; }
    string? Message { get; }
    Task RestoreAsync();
    Task SignInAsync();
    Task SignOutAsync();
}
