using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Reflection;
using FlyerMonkey.Shared.Services;
using FlyerMonkey.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;

// Offline cookie/OIDC challenge mechanics only. No Entra token issuance, login, callback or MFA is simulated as integration.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
builder.Logging.ClearProviders();
if (args.Contains("--diagnostic")) builder.Logging.AddConsole();
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Authentication:Enabled"] = "true",
    ["Authentication:TenantId"] = "11111111-1111-4111-8111-111111111111",
    ["Authentication:OperatorObjectId"] = "22222222-2222-4222-8222-222222222222",
    ["Authentication:ApiClientId"] = "33333333-3333-4333-8333-333333333333",
    ["Authentication:WebClientId"] = "44444444-4444-4444-8444-444444444444",
    ["Authentication:ApiBaseUrl"] = "https://localhost:7255/",
    ["Authentication:WebClientSecret"] = "offline-fixture-not-an-azure-credential"
});
builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
builder.Services.AddAntiforgery();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddOperatorAuthentication(builder.Configuration, builder.Environment);
var tokenAcquisition = DispatchProxy.Create<ITokenAcquisition, RecordingTokenAcquisition>();
var recordingTokens = (RecordingTokenAcquisition)tokenAcquisition;
// Keep Identity.Web's real internal lifecycle service for cookie/OIDC sign-out.
// Its default factory casts ITokenAcquisition, which the acquisition-only proxy cannot implement.
var realAcquisition = builder.Services.Last(descriptor => descriptor.ServiceType == typeof(ITokenAcquisition));
var internalAcquisition = builder.Services.Last(descriptor => descriptor.ServiceType.FullName == "Microsoft.Identity.Web.ITokenAcquisitionInternal");
builder.Services.Add(new ServiceDescriptor(internalAcquisition.ServiceType, services =>
    realAcquisition.ImplementationFactory?.Invoke(services) ??
    realAcquisition.ImplementationInstance ??
    ActivatorUtilities.CreateInstance(services, realAcquisition.ImplementationType!), realAcquisition.Lifetime));
builder.Services.AddSingleton(tokenAcquisition);
builder.Services.AddScoped<AuthenticationStateProvider, OfflineAuthenticationState>();
builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
builder.Services.PostConfigure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
{
    options.Configuration = new OpenIdConnectConfiguration
    {
        Issuer = "https://login.offline.invalid/",
        AuthorizationEndpoint = "https://login.offline.invalid/authorize",
        TokenEndpoint = "https://login.offline.invalid/token",
        EndSessionEndpoint = "https://login.offline.invalid/logout"
    };
    options.ConfigurationManager = new Microsoft.IdentityModel.Protocols.StaticConfigurationManager<OpenIdConnectConfiguration>(options.Configuration);
    options.Backchannel = new HttpClient(new NoNetwork());
});
// Default key-ring warm-up is unnecessary with ephemeral protection; never read host key stores.
foreach (var descriptor in builder.Services.Where(d => d.ServiceType == typeof(IHostedService) &&
    d.ImplementationType?.FullName?.Contains("DataProtectionHostedService", StringComparison.Ordinal) == true).ToArray())
    builder.Services.Remove(descriptor);
await using var app = builder.Build();
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapOperatorAuthentication();
// Mirror production route composition: HTTP endpoints AND all shared Blazor pages.
app.MapRazorComponents<FlyerMonkey.Web.Components.App>().AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(FlyerMonkey.Shared._Imports).Assembly);
// Test-only cookie minting; never part of FlyerMonkey Web.
app.MapGet("/test/cookie", async (HttpContext context) =>
{
    await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
        new ClaimsPrincipal(new ClaimsIdentity([new("name", "offline-cookie-fixture")], "fixture")));
});
app.MapGet("/test/forbidden", () => Results.Forbid(authenticationSchemes: [CookieAuthenticationDefaults.AuthenticationScheme]));
var checks = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; }
try
{
    await app.StartAsync();
    var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>();
    Check(endpoints.Count(endpoint => endpoint.RoutePattern.RawText == "/operator") == 1, "Exactly one /operator endpoint with shared Blazor routes loaded");
    Check(endpoints.Any(endpoint => endpoint.RoutePattern.RawText == "/operator/mobile"), "Mobile operator route remains separately available");
    using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { BaseAddress = new(app.Urls.Single()) };
    var cookies = new Dictionary<string, string>();
    void SaveCookies(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("Set-Cookie", out var headers))
            foreach (var header in headers) { var pair = header.Split(';')[0].Split('=', 2); cookies[pair[0]] = pair[1]; }
    }
    async Task<HttpResponseMessage> Send(HttpMethod method, string path, HttpContent? content = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        if (cookies.Count > 0) request.Headers.Add("Cookie", string.Join("; ", cookies.Select(c => c.Key + "=" + c.Value)));
        var response = await http.SendAsync(request);
        SaveCookies(response);
        return response;
    }
    using var signin = await Send(HttpMethod.Get, "/operator/signin");
    Check(signin.StatusCode == HttpStatusCode.Redirect && signin.Headers.Location?.Host == "login.offline.invalid", $"OIDC challenge uses static offline metadata (status {(int)signin.StatusCode})");
    Check(signin.Headers.Location!.Query.Contains("response_type=code") && signin.Headers.Location.Query.Contains("code_challenge="), "Code flow with PKCE");
    Check(signin.Headers.Location.Query.Contains("signin-oidc"), "Expected callback");
    var challengeQuery = WebUtility.UrlDecode(signin.Headers.Location.Query);
    Check(challengeQuery.Contains("api://33333333-3333-4333-8333-333333333333/Crater.Access"), "Web requests the API delegated scope");
    Check(!challengeQuery.Contains("User.Read") && !challengeQuery.Contains("graph.microsoft.com"), "No unnecessary Graph permission requested");
    var oidcOptions = app.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(OpenIdConnectDefaults.AuthenticationScheme);
    var state = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(signin.Headers.Location.Query)["state"].ToString();
    Check(oidcOptions.StateDataFormat.Unprotect(state)?.RedirectUri == "/operator", "Sign-in returns to the single protected Web operator route");
    Check(!oidcOptions.GetClaimsFromUserInfoEndpoint && !oidcOptions.SaveTokens, "No UserInfo call or browser token storage");
    var authenticationOptions = app.Services.GetRequiredService<IOptions<AuthenticationOptions>>().Value;
    Check(authenticationOptions.DefaultAuthenticateScheme == CookieAuthenticationDefaults.AuthenticationScheme &&
        authenticationOptions.DefaultChallengeScheme == CookieAuthenticationDefaults.AuthenticationScheme,
        "Protected requests use local cookie rejection, not automatic OIDC login");
    foreach (var protectedPath in new[] { "/operator", "/operator/access", "/operator/artwork?productId=1" })
    {
        using var anonymous = await Send(HttpMethod.Get, protectedPath);
        Check(anonymous.StatusCode == HttpStatusCode.Unauthorized && anonymous.Headers.Location is null,
            "Anonymous access rejected without provider redirect: " + protectedPath);
    }
    using (var forbidden = await Send(HttpMethod.Get, "/test/forbidden"))
        Check(forbidden.StatusCode == HttpStatusCode.Forbidden && forbidden.Headers.Location is null,
            "Forbidden access remains 403 without login or status-page redirects");
    using var cookie = await Send(HttpMethod.Get, "/test/cookie");
    Check(cookie.Headers.GetValues("Set-Cookie").Any(c => c.Contains("secure", StringComparison.OrdinalIgnoreCase) && c.Contains("httponly", StringComparison.OrdinalIgnoreCase)), "Secure HttpOnly cookie");
    using var page = await Send(HttpMethod.Get, "/operator");
    Check(page.StatusCode == HttpStatusCode.OK && page.Headers.CacheControl?.NoStore == true, "Cookie-protected operator page has no-store");
    using (var stillSignedIn = await Send(HttpMethod.Get, "/operator/signed-out"))
        Check(stillSignedIn.StatusCode == HttpStatusCode.Redirect && stillSignedIn.Headers.Location?.OriginalString == "/operator", "Confirmation page never falsely claims an active session is signed out");
    foreach (var path in new[] { "/operator/access", "/operator/artwork?productId=1" })
    {
        var before = recordingTokens.Calls;
        using var access = await Send(HttpMethod.Get, path);
        Check(access.StatusCode == HttpStatusCode.Unauthorized && access.Headers.Location is null,
            "Token interaction requirement never silently reauthenticates: " + path);
        Check(recordingTokens.Calls == before + 1, "HTTP token acquisition uses explicit OIDC scheme: " + path);
    }
    using (var scope = app.Services.CreateScope())
    {
        var before = recordingTokens.Calls;
        try { await scope.ServiceProvider.GetRequiredService<IOperatorTokenProvider>().GetAccessTokenAsync(); throw new Exception("Expected offline token boundary"); }
        catch (MicrosoftIdentityWebChallengeUserException) { Check(recordingTokens.Calls == before + 1, "Blazor token provider uses explicit OIDC scheme"); }
    }
    Check((await Send(HttpMethod.Post, "/operator/signout", new FormUrlEncodedContent([]))).StatusCode == HttpStatusCode.BadRequest, "Logout requires antiforgery token");
    Check((await Send(HttpMethod.Post, "/operator/signout/microsoft", new FormUrlEncodedContent([]))).StatusCode == HttpStatusCode.BadRequest, "Forged provider logout cannot revoke an active session");
    Check((await Send(HttpMethod.Get, "/operator")).StatusCode == HttpStatusCode.OK, "Rejected forged logout preserves current session");
    var html = await page.Content.ReadAsStringAsync();
    var match = Regex.Match(html, "name='([^']+)' value='([^']+)'");
    Check(match.Success, "Logout form includes antiforgery field");
    var cookieOptions = app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(CookieAuthenticationDefaults.AuthenticationScheme);
    var oldCookie = cookies[cookieOptions.Cookie.Name!];
    var acquisitionsBeforeLogout = recordingTokens.Calls;
    using var logout = await Send(HttpMethod.Post, "/operator/signout", new FormUrlEncodedContent(new Dictionary<string, string>
        { [match.Groups[1].Value] = WebUtility.HtmlDecode(match.Groups[2].Value) }));
    Check(logout.StatusCode == HttpStatusCode.Redirect && logout.Headers.Location?.OriginalString == "/operator/signed-out", "Local logout completes without contacting Microsoft");
    var logoutCookieName = cookieOptions.Cookie.Name!;
    Check(logout.Headers.GetValues("Set-Cookie").Any(value => value.StartsWith(logoutCookieName + "=;", StringComparison.Ordinal)), "Logout expires the local cookie before redirecting");
    using var afterLogout = await Send(HttpMethod.Get, "/operator/access");
    Check(afterLogout.StatusCode == HttpStatusCode.Unauthorized && afterLogout.Headers.Location is null,
        $"Logged-out access must not trigger Entra reauthentication (actual {(int)afterLogout.StatusCode}, providerRedirect={afterLogout.Headers.Location?.Host == "login.offline.invalid"})");
    foreach (var protectedPath in new[] { "/operator", "/operator/artwork?productId=1" })
    {
        using var denied = await Send(HttpMethod.Get, protectedPath);
        Check(denied.StatusCode == HttpStatusCode.Unauthorized && denied.Headers.Location is null, "Local logout blocks protected access without Microsoft completion: " + protectedPath);
    }
    cookies[logoutCookieName] = oldCookie;
    using (var replay = await Send(HttpMethod.Get, "/operator/access"))
        Check(replay.StatusCode == HttpStatusCode.Unauthorized && replay.Headers.Location is null, "Old cookie replay denied by server-side ticket revocation");
    cookies.Remove(logoutCookieName);
    Check(recordingTokens.Calls == acquisitionsBeforeLogout, "Logged-out requests never attempt token acquisition");
    using var signedOut = await Send(HttpMethod.Get, "/operator/signed-out");
    Check(signedOut.StatusCode == HttpStatusCode.OK && signedOut.Headers.CacheControl?.NoStore == true, "Signed-out confirmation is anonymous and no-store");
    Check((await Send(HttpMethod.Post, "/operator/signout/microsoft", new FormUrlEncodedContent([]))).StatusCode == HttpStatusCode.BadRequest, "Optional Microsoft logout requires antiforgery");
    var remoteForm = Regex.Match(await signedOut.Content.ReadAsStringAsync(), "name='([^']+)' value='([^']+)'");
    Check(remoteForm.Success, "Optional Microsoft logout has a new anonymous antiforgery token");
    HttpContent RemoteLogoutForm() => new FormUrlEncodedContent(new Dictionary<string, string>
        { [remoteForm.Groups[1].Value] = WebUtility.HtmlDecode(remoteForm.Groups[2].Value) });
    using var microsoftLogout = await Send(HttpMethod.Post, "/operator/signout/microsoft", RemoteLogoutForm());
    Check(microsoftLogout.StatusCode == HttpStatusCode.Redirect && microsoftLogout.Headers.Location?.Host == "login.offline.invalid", "Only explicit optional logout redirects to the provider");
    Check((await Send(HttpMethod.Get, "/operator/access")).StatusCode == HttpStatusCode.Unauthorized, "Cancelled Microsoft flow cannot restore local access");
    var logoutState = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(microsoftLogout.Headers.Location!.Query)["state"].ToString();
    using var completedLogout = await Send(HttpMethod.Get, "/signout-callback-oidc?state=" + Uri.EscapeDataString(logoutState));
    Check(completedLogout.StatusCode == HttpStatusCode.Redirect && completedLogout.Headers.Location?.OriginalString == "/", "Provider logout callback returns to the public root");
    Check((await Send(HttpMethod.Get, "/operator/access")).StatusCode == HttpStatusCode.Unauthorized, "Completed provider flow does not recreate local session");
    var previousSignOutEvent = oidcOptions.Events.OnRedirectToIdentityProviderForSignOut;
    oidcOptions.Events.OnRedirectToIdentityProviderForSignOut = _ => throw new InvalidOperationException("Offline provider logout failure");
    using var failedProvider = await Send(HttpMethod.Post, "/operator/signout/microsoft", RemoteLogoutForm());
    Check(failedProvider.StatusCode == HttpStatusCode.InternalServerError, "Offline provider logout failure exercised");
    oidcOptions.Events.OnRedirectToIdentityProviderForSignOut = previousSignOutEvent;
    using (var failedFlowAccess = await Send(HttpMethod.Get, "/operator/access"))
        Check(failedFlowAccess.StatusCode == HttpStatusCode.Unauthorized && failedFlowAccess.Headers.Location is null, "Provider failure cannot restore the revoked local session");
    Check((await Send(HttpMethod.Get, "/operator/signin")).Headers.Location?.Host == "login.offline.invalid", "Explicit sign-in is still available after logout");
    Check(cookieOptions.SessionStore is not null, "Cookie authentication uses revocable server-side sessions");
    Check(cookieOptions.ExpireTimeSpan == TimeSpan.FromHours(1) && !cookieOptions.SlidingExpiration, "Bounded cookie lifetime");
    var disabled = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Authentication:Enabled"] = "false" }).Build();
    Check(!new ServiceCollection().AddOperatorAuthentication(disabled), "Authentication stays disabled without identity/credential setup");
    var incomplete = new ConfigurationBuilder().AddInMemoryCollection(builder.Configuration.AsEnumerable()).Build();
    incomplete["Authentication:WebClientSecret"] = "";
    try { new ServiceCollection().AddOperatorAuthentication(incomplete); throw new Exception("Expected missing credential rejection"); }
    catch (InvalidOperationException ex) { Check(ex.Message.Contains("User Secrets"), "Enabled authentication requires external credential"); }
    foreach (var badApi in new[] { "http://localhost:7255/", "https://localhost:7255/path/", "https://localhost:7255/?x=1", "https://user@localhost:7255/" })
    {
        incomplete["Authentication:ApiBaseUrl"] = badApi;
        try { OperatorAuthentication.RequiredApiBaseUri(incomplete); throw new Exception("Expected API root rejection"); }
        catch (InvalidOperationException) { Check(true, "Unsafe API root rejected"); }
    }
    var priorEnvironment = app.Environment.EnvironmentName;
    app.Environment.EnvironmentName = "Development";
    incomplete["Authentication:ApiBaseUrl"] = "https://production.invalid/";
    try { OperatorAuthentication.RequiredApiBaseUri(incomplete, app.Environment); throw new Exception("Expected production target rejection"); }
    catch (InvalidOperationException) { Check(true, "Development private client rejects production target"); }
    app.Environment.EnvironmentName = priorEnvironment;
    var repository = new DirectoryInfo(AppContext.BaseDirectory);
    while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "FlyerMonkey.Api", "FlyerMonkey.Api.csproj"))) repository = repository.Parent;
    if (repository is null) throw new Exception("Repository configuration not found");
    // Load only the explicit repository development files; never load or list real User Secrets.
    var apiDevelopment = new ConfigurationBuilder().AddJsonFile(Path.Combine(repository.FullName, "FlyerMonkey.Api", "appsettings.Development.json")).Build();
    var webDevelopment = new ConfigurationBuilder().AddJsonFile(Path.Combine(repository.FullName, "FlyerMonkey", "FlyerMonkey.Web", "appsettings.Development.json")).Build();
    Check(apiDevelopment.GetChildren().Select(section => section.Key).SequenceEqual(["Logging"]) &&
        webDevelopment.GetChildren().Select(section => section.Key).SequenceEqual(["Logging"]), "Tracked Development files remain logging-only templates");
    IConfiguration LocalProfile(string path)
    {
        var launch = new ConfigurationBuilder().AddJsonFile(Path.Combine(repository.FullName, path)).Build();
        return new ConfigurationBuilder().AddInMemoryCollection(launch.GetSection("profiles:operator-local:environmentVariables")
            .GetChildren().Select(entry => new KeyValuePair<string, string?>(entry.Key.Replace("__", ":"), entry.Value))).Build();
    }
    var apiProfile = LocalProfile("FlyerMonkey.Api/Properties/launchSettings.json");
    var webProfile = LocalProfile("FlyerMonkey/FlyerMonkey.Web/Properties/launchSettings.json");
    Check(apiProfile.GetValue<bool>("Authentication:Enabled") && webProfile.GetValue<bool>("Authentication:Enabled"), "Explicit operator-local profiles enable authentication");
    Check(apiProfile["Authentication:TenantId"] == webProfile["Authentication:TenantId"] &&
        apiProfile["Authentication:ApiClientId"] == webProfile["Authentication:ApiClientId"] &&
        apiProfile.GetSection("Authentication:AllowedClientIds").Get<string[]>() is [var allowed] && allowed == webProfile["Authentication:WebClientId"],
        "Actual Web/API tenant, resource and allowed client agree");
    Check(apiProfile.GetSection("Authentication:OperatorPermissions").Get<string[]>() is ["Crater.ViewOwn"] &&
        apiProfile.GetValue<bool>("Authentication:ViewOwnOnly"), "Local pilot grants/caps only ViewOwn");
    Check(apiProfile["Authentication:OperatorObjectId"] is null && webProfile["Authentication:OperatorObjectId"] is null &&
        webProfile["Authentication:WebClientSecret"] is null, "Owner ID and Web credential stay outside launch profiles");
    async Task<string> RenderCrater(bool authenticated, bool expanded, int productId = 102)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<IWebsiteCraterSession>(new CraterSession(authenticated));
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new Microsoft.AspNetCore.Components.Web.HtmlRenderer(provider,
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<FlyerMonkey.Shared.Components.ProductCraterEntry>(
                Microsoft.AspNetCore.Components.ParameterView.FromDictionary(new Dictionary<string, object?>
                { ["ProductId"] = productId, ["Expanded"] = expanded }));
            return WebUtility.HtmlDecode(component.ToHtmlString());
        });
    }
    var anonymousEntry = await RenderCrater(false, false);
    Check(anonymousEntry.Contains("☄️ Crater It") && anonymousEntry.Contains("type=\"submit\""), "Anonymous product entry has a working submit button");
    Check(anonymousEntry.Contains("method=\"get\" action=\"/products/102\"") &&
        anonymousEntry.Contains("name=\"crater\" value=\"true\""), "Crater click opens explanation on the same product without a private API request");
    var anonymousExplanation = await RenderCrater(false, true);
    Check(anonymousExplanation.Contains("Public customer sign-in is coming later") &&
        anonymousExplanation.Contains("keep browsing products and offers"), "Anonymous explanation describes future customer sign-in and continued public browsing");
    Check(!anonymousExplanation.Contains("/operator") && !anonymousExplanation.Contains("login.microsoftonline"), "Customer entry never links or redirects to private operator login");
    Check(anonymousExplanation.Contains("Product ID: 102") && anonymousExplanation.Contains("href=\"/products/102\""), "Product ID and canonical local return destination are preserved");
    var operatorEntry = await RenderCrater(true, false);
    var operatorExplanation = await RenderCrater(true, true);
    Check(operatorEntry.Contains("Generation is disabled") && operatorExplanation.Contains("does not grant Generate permission"), "Operator ViewOwn never implies generation permission");
    Check(operatorExplanation.Contains("disabled") && operatorExplanation.Contains("Generation unavailable"), "Operator generation action remains disabled");
    var otherProduct = await RenderCrater(false, true, 139);
    Check(otherProduct.Contains("action=\"/products/139\"") && otherProduct.Contains("href=\"/products/139\"") &&
        !otherProduct.Contains("/products/102"), "Return navigation follows the selected product and uses no caller-supplied URL");
    try { await RenderCrater(false, true, -1); throw new Exception("Expected invalid product rejection"); }
    catch (ArgumentOutOfRangeException) { Check(true, "Invalid product context is rejected"); }
    using (var publicServices = new ServiceCollection().BuildServiceProvider())
        Check(!await new WebsiteCraterSession(publicServices).IsAuthenticatedAsync(), "Web entry works anonymously with authentication services disabled");
    Console.WriteLine($"{checks} offline Web challenge/cookie/antiforgery checks passed. No real Entra sign-in tested.");
    return 0;
}
catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
finally { await app.StopAsync(); }

sealed class CraterSession(bool authenticated) : IWebsiteCraterSession
{
    public Task<bool> IsAuthenticatedAsync() => Task.FromResult(authenticated);
}

sealed class NoNetwork : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        throw new Exception("No identity network calls allowed");
}

public class RecordingTokenAcquisition : DispatchProxy
{
    public int Calls;
    protected override object? Invoke(MethodInfo? method, object?[]? arguments)
    {
        if (method?.Name != nameof(ITokenAcquisition.GetAccessTokenForUserAsync) || arguments is null)
            throw new InvalidOperationException("Unexpected offline token operation");
        var parameters = method.GetParameters();
        object? Argument(string name) => arguments[Array.FindIndex(parameters, parameter => parameter.Name == name)];
        if (Argument("authenticationScheme") as string != OpenIdConnectDefaults.AuthenticationScheme ||
            Argument("user") is not ClaimsPrincipal { Identity.IsAuthenticated: true } ||
            Argument("scopes") is not IEnumerable<string> scopes ||
            !scopes.SequenceEqual(["api://33333333-3333-4333-8333-333333333333/Crater.Access"]))
            throw new InvalidOperationException("Expected explicit OpenIdConnect, authenticated user and exact API scope");
        Calls++;
        // Never issue a token or contact the API. Exercise the app's real reauthentication branch.
        return Task.FromException<string>(new MicrosoftIdentityWebChallengeUserException(
            new MsalUiRequiredException("offline_interaction_required", "Offline test boundary"), [], null));
    }
}

sealed class OfflineAuthenticationState : AuthenticationStateProvider
{
    public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(
        new ClaimsPrincipal(new ClaimsIdentity([new("name", "offline-component-fixture")], "fixture"))));
}
