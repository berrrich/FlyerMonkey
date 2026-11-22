#if ANDROID
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using FlyerMonkey.Shared.Model;
using FlyerMonkey.Shared.Services;
using Microsoft.Identity.Client;

namespace FlyerMonkey.Services;

public sealed class AndroidCustomerAuthentication : IMobileCustomerSession
{
    private const string CacheKey = "FlyerMonkey.Customer.Msal.v1";
    private readonly SemaphoreSlim gate = new(1, 1);
    private IPublicClientApplication? application;
    public bool SignedIn { get; private set; }
    public string? DisplayName { get; private set; }
    public string? Email { get; private set; }
    public CustomerAccount? Account { get; private set; }
    public string? Message { get; private set; }
    public CustomerCraterPreview? CraterPreview { get; private set; }

    private static string Setting(string key) => typeof(AndroidCustomerAuthentication).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>().SingleOrDefault(x => x.Key == key)?.Value ?? "";

    internal static Uri? CustomerApiRoot =>
        Uri.TryCreate(Setting("CustomerApiBaseUrl"), UriKind.Absolute, out var root) &&
        root.Scheme == "https" && root.AbsolutePath == "/" && root.UserInfo == "" && root.Query == "" && root.Fragment == ""
            ? root : null;

    private IPublicClientApplication Application
    {
        get
        {
            if (application is not null) return application;
            var clientId = Setting("CustomerClientId");
            if (!Guid.TryParse(clientId, out var id) || id == Guid.Empty ||
                clientId == CustomerIdentity.WebClientId || clientId == CustomerIdentity.ApiClientId ||
                Setting("CustomerRedirectScheme") != $"msal{clientId}")
                throw new InvalidOperationException("Customer sign-in needs the approved mobile Entra registration and Android redirect configuration.");
            application = PublicClientApplicationBuilder.Create(clientId)
                .WithAuthority($"https://flyermonkeycustomers.ciamlogin.com/{CustomerIdentity.TenantId}")
                .WithRedirectUri($"msal{clientId}://auth").Build();
            // Android MSAL owns its secure persistent cache. Custom serialization callbacks
            // are unsupported on mobile and throw during first client initialization.
            return application;
        }
    }

    public Task RestoreAsync() => Run(false);
    public Task LoadAccountAsync(int? productId = null) => Run(false, statusOnly: true, productId: productId);
    public Task SignInAsync(bool includeCraterStatus = false) => Run(true, statusOnly: includeCraterStatus);
    public Task LoadCraterPreviewAsync(int productId) => Run(false, productId: productId);
    public Task GenerateCraterAsync(int productId, bool classic = false) =>
        Run(false, generate: true, productId: productId, classic: classic);

    private async Task Run(bool interactive, bool generate = false, bool statusOnly = false, int? productId = null,
        bool classic = false)
    {
        await gate.WaitAsync();
        try
        {
            if (generate && (CraterPreview is not { } preview || preview.ProductId != productId ||
                !(classic ? preview.CanGenerateClassic : preview.CanGenerate))) return;
            var repeat = generate && CraterPreview?.Image is not null;
            var expectedVersion = CraterPreview?.AttemptVersion;
            Account = null;
            SignedIn = false;
            DisplayName = Email = null;
            Message = null;
            CraterPreview = null;
            var app = Application;
            AuthenticationResult result;
            if (interactive)
            {
                AndroidAuthDiagnostics.Write("interactive.start");
                result = await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    var parent = Platform.CurrentActivity;
                    if (parent is not MainActivity || parent.IsFinishing || parent.IsDestroyed)
                        throw new InvalidOperationException("Return to FlyerMonkey and select customer sign-in again.");
                    return await app.AcquireTokenInteractive([CustomerIdentity.Scope])
                        .WithParentActivityOrWindow(parent).WithUseEmbeddedWebView(false)
                        .WithPrompt(Prompt.SelectAccount).ExecuteAsync();
                });
                AndroidAuthDiagnostics.Write("interactive.completed");
            }
            else
            {
                var accounts = (await app.GetAccountsAsync()).ToArray();
                if (accounts.Length != 1) return;
                result = await app.AcquireTokenSilent([CustomerIdentity.Scope], accounts[0]).ExecuteAsync();
            }
            SignedIn = true;
            DisplayName = result.ClaimsPrincipal?.FindFirst("name")?.Value;
            Email = CustomerIdentity.DisplayEmail(result.ClaimsPrincipal);
            await ReadAccount(result.AccessToken, productId, generate, statusOnly, repeat, classic, expectedVersion);
        }
        catch (MsalUiRequiredException) { AndroidAuthDiagnostics.Write("session.ui-required"); Message = "Your customer session has expired. Sign in to continue."; }
        catch (MsalClientException ex) when (ex.ErrorCode == "authentication_canceled") { AndroidAuthDiagnostics.Write("interactive.cancelled"); Message = "Sign-in was cancelled."; }
        catch (MsalException) { AndroidAuthDiagnostics.Write("session.msal-failed"); Message = "Customer sign-in could not be completed. Check your connection and the mobile Entra configuration."; }
        catch (InvalidOperationException ex) { AndroidAuthDiagnostics.Write("session.configuration-or-storage-failed"); Message = ex.Message; }
        catch (Exception) { AndroidAuthDiagnostics.Write("session.failed"); Message = "Customer sign-in or secure token storage is unavailable. Try signing in again."; SignedIn = false; }
        finally
        {
            AndroidAuthDiagnostics.Write(Account is not null ? "session.profile-ready" : "session.no-profile");
            gate.Release();
        }
    }

    private async Task ReadAccount(string token, int? productId, bool generate, bool statusOnly, bool repeat, bool classic,
        int? expectedVersion)
    {
        var root = CustomerApiRoot ?? throw new InvalidOperationException("Configure the customer API as a trusted HTTPS root.");
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            { BaseAddress = root, Timeout = TimeSpan.FromSeconds(generate ? 180 : 20), MaxResponseContentBufferSize = 10 * 1024 * 1024 };
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/customers/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        try
        {
            using var response = await http.SendAsync(request);
            switch (response.StatusCode)
            {
                case HttpStatusCode.Unauthorized:
                    SignedIn = false; Message = "The API rejected your customer session. Sign in again."; return;
                case HttpStatusCode.Forbidden:
                    Message = "Customer access was denied. The account may be disabled or mobile API permission is not configured."; return;
                case HttpStatusCode.NotFound:
                    Message = "Customer account or API authentication is not available. Complete account setup on the website; no customer record was created by mobile."; return;
            }
            if (!response.IsSuccessStatusCode)
            { Message = $"Customer API returned an error ({(int)response.StatusCode}). Try refreshing account status later."; return; }
            var account = await response.Content.ReadFromJsonAsync<CustomerAccount>();
            if (account is null || account.CustomerId == Guid.Empty || account.Status != "Active")
            { Message = "The API returned an invalid customer account."; return; }
            Account = account;
            if (statusOnly) CraterPreview = await CustomerCraterPreviewReader.ReadStatusAsync(http, token,
                productId is > 0 ? productId.Value : 139);
            else if (productId is { } selected)
            {
                var preview = generate
                    ? await CustomerCraterPreviewReader.GenerateAsync(http, token, repeat, selected, classic, expectedVersion)
                    : await CustomerCraterPreviewReader.ReadAsync(http, token, selected);
                CraterPreview = preview with { ProductId = selected };
            }
        }
        catch (HttpRequestException) { Message = "Cannot connect securely to the customer API. Check network access, API availability and HTTPS trust."; }
        catch (OperationCanceledException) { Message = "Customer API timed out. Refresh account status later."; }
        catch (System.Text.Json.JsonException) { Message = "The customer API returned an invalid response."; }
        catch (InvalidDataException) { Message = "The customer API returned an invalid private preview."; }
    }

    public async Task SignOutAsync()
    {
        await gate.WaitAsync();
        try
        {
            if (application is not null)
                foreach (var account in await application.GetAccountsAsync()) await application.RemoveAsync(account);
            SecureStorage.Default.Remove(CacheKey);
            Message = "Signed out of FlyerMonkey on this device. Your browser identity-provider session may still be active.";
        }
        catch { Message = "Customer sign-out could not clear secure storage. Try again before sharing this device."; }
        finally { SignedIn = false; DisplayName = Email = null; Account = null; CraterPreview = null; gate.Release(); }
    }
}
#endif
