using FlyerMonkey.Shared.Model;

namespace FlyerMonkey.Shared.Services;

// Deliberately separate from operator tokens and website cookies.
public interface IMobileCustomerSession
{
    bool SignedIn { get; }
    string? DisplayName { get; }
    string? Email { get; }
    CustomerAccount? Account { get; }
    string? Message { get; }
    CustomerCraterPreview? CraterPreview { get; }
    Task LoadAccountAsync(int? productId = null);
    Task LoadCraterPreviewAsync(int productId);
    Task GenerateCraterAsync(int productId, bool classic = false);
    Task RestoreAsync();
    Task SignInAsync(bool includeCraterStatus = false);
    Task SignOutAsync();
}
