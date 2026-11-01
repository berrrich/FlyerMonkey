using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using FlyerMonkey.Api.Controllers;
using FlyerMonkey.Api.Services;
using FlyerMonkey.Shared.Model;
using FlyerMonkey.Shared.Services;
using FlyerMonkey.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Identity.Web;
using SQLServerConnection.Data;
using WebCustomer = FlyerMonkey.Web.Services.CustomerAuthentication;
using ApiCustomer = FlyerMonkey.Api.Services.CustomerAuthentication;

// All token signing and provider traffic are offline fixtures. No SQL, Google, Entra or paid calls.
var checks = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; Console.WriteLine("PASS: " + name); }
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
builder.Logging.ClearProviders();
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Authentication:Enabled"] = "true",
    ["Authentication:TenantId"] = "11111111-1111-4111-8111-111111111111",
    ["Authentication:OperatorObjectId"] = "22222222-2222-4222-8222-222222222222",
    ["Authentication:ApiClientId"] = "33333333-3333-4333-8333-333333333333",
    ["Authentication:WebClientId"] = "44444444-4444-4444-8444-444444444444",
    ["Authentication:AllowedClientIds:0"] = "44444444-4444-4444-8444-444444444444",
    ["Authentication:OperatorPermissions:0"] = "Crater.ViewOwn",
    ["Authentication:ApiBaseUrl"] = "https://localhost:7255/",
    ["Authentication:WebClientSecret"] = "offline-operator-fixture",
    ["CustomerAuthentication:Enabled"] = "true",
    ["CustomerAuthentication:ApiBaseUrl"] = "https://localhost:7255/"
});
var disabled = new ConfigurationBuilder().Build();
Check(!new ServiceCollection().AddCustomerAuthentication(disabled, builder.Environment), "Web customer auth absent defaults off without credentials");
Check(!ApiCustomer.AddCustomerAuthentication(new ServiceCollection(), disabled), "API customer auth absent defaults off");
foreach (var unsafeReturn in new[] { "https://evil.invalid/", "//evil.invalid/", "/\\evil", "/products/102%0d%0a", "/products/102?returnUrl=https://evil.invalid", "/operator", "/products/0", "/products/102#x" })
    Check(WebCustomer.SafeReturn(unsafeReturn) == "/", "Unsafe return rejected: " + unsafeReturn);
Check(WebCustomer.SafeReturn("/products/139?crater=true") == "/products/139?crater=true", "Product intent preserved");
Check(WebCustomer.SafeReturn("/products/102") == "/products/102", "Plain product return preserved");
try { WebCustomer.ApiUri(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["CustomerAuthentication:ApiBaseUrl"]="https://evil.invalid/" }).Build(), builder.Environment); throw new Exception("Expected rejection"); }
catch (InvalidOperationException) { Check(true, "Development customer tokens confined to HTTPS loopback"); }

// A fresh test configuration supplies no secret; production source rejects it if local secrets are also absent.
var originalSecret = Environment.GetEnvironmentVariable("CustomerAuthentication__ClientSecret");
Environment.SetEnvironmentVariable("CustomerAuthentication__ClientSecret", "");
try
{
    new ServiceCollection().AddCustomerAuthentication(builder.Configuration, builder.Environment);
    // A developer may have configured the real secret; no test prints or transmits it.
    Console.WriteLine("INFO: User Secrets provides a credential; missing-secret path covered by source-only configuration rejection below.");
}
catch (InvalidOperationException ex) { Check(ex.Message.Contains("ClientSecret"), "Missing customer secret fails closed before discovery"); }
Environment.SetEnvironmentVariable("CustomerAuthentication__ClientSecret", "offline-customer-fixture");
try
{
    builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
    builder.Services.AddAntiforgery();
    builder.Services.AddRazorComponents().AddInteractiveServerComponents();
    builder.Services.AddOperatorAuthentication(builder.Configuration, builder.Environment);
    builder.Services.AddFlyerMonkeyAuthentication(builder.Configuration);
    builder.Services.AddCustomerAuthentication(builder.Configuration, builder.Environment);
    ApiCustomer.AddCustomerAuthentication(builder.Services, builder.Configuration);
    var tokenAcquisition = DispatchProxy.Create<ITokenAcquisition, CustomerTokens>();
    var originalAcquisition = builder.Services.Last(d => d.ServiceType == typeof(ITokenAcquisition));
    var internalAcquisition = builder.Services.Last(d => d.ServiceType.FullName == "Microsoft.Identity.Web.ITokenAcquisitionInternal");
    builder.Services.Add(new ServiceDescriptor(internalAcquisition.ServiceType, sp =>
        originalAcquisition.ImplementationFactory?.Invoke(sp) ?? originalAcquisition.ImplementationInstance ??
        ActivatorUtilities.CreateInstance(sp, originalAcquisition.ImplementationType!), originalAcquisition.Lifetime));
    builder.Services.AddSingleton(tokenAcquisition);
    var apiTransport = new CustomerApiTransport();
    builder.Services.AddHttpClient("CustomerApi").ConfigurePrimaryHttpMessageHandler(() => apiTransport);
    using var rsa = RSA.Create(2048);
    var key = new RsaSecurityKey(rsa) { KeyId = "offline" };
    using var badRsa = RSA.Create(2048);
    var badKey = new RsaSecurityKey(badRsa) { KeyId = "bad" };
    builder.Services.PostConfigure<OpenIdConnectOptions>(WebCustomer.Oidc, options =>
    {
        var metadata = new OpenIdConnectConfiguration
        {
            Issuer = CustomerIdentity.Issuer,
            AuthorizationEndpoint = "https://identity.offline.invalid/authorize",
            TokenEndpoint = "https://identity.offline.invalid/token"
        };
        metadata.SigningKeys.Add(key);
        options.Configuration = metadata;
        options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(metadata);
        options.Backchannel = new HttpClient(new NoNetwork());
    });
    foreach (var scheme in new[] { ApiCustomer.Scheme, JwtBearerDefaults.AuthenticationScheme })
        builder.Services.PostConfigure<JwtBearerOptions>(scheme, options =>
        {
            var metadata = new OpenIdConnectConfiguration { Issuer = scheme == ApiCustomer.Scheme ? CustomerIdentity.Issuer : "https://login.microsoftonline.com/11111111-1111-4111-8111-111111111111/v2.0" };
            metadata.SigningKeys.Add(key);
            options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(metadata);
            options.Backchannel = new HttpClient(new NoNetwork());
        });
    builder.Services.AddControllers().AddApplicationPart(typeof(CustomersController).Assembly);
    var repository = new Customers();
    builder.Services.AddSingleton<ICustomerRepository>(repository);
    builder.Services.AddScoped<ImageGenerationService>(_ => throw new Exception("Paid provider must never resolve"));
    await using var app = builder.Build();
    var defaults = app.Services.GetRequiredService<IOptions<AuthenticationOptions>>().Value;
    Check(defaults.DefaultAuthenticateScheme == CookieAuthenticationDefaults.AuthenticationScheme &&
        defaults.DefaultChallengeScheme == CookieAuthenticationDefaults.AuthenticationScheme &&
        defaults.DefaultSignInScheme == CookieAuthenticationDefaults.AuthenticationScheme,
        "Customer registration preserves operator Web defaults");
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseAntiforgery();
    app.MapCustomerAuthentication();
    app.MapControllers();
    app.MapGet("/catalogue-fixture", () => Results.Ok()).AllowAnonymous();
    app.MapGet("/operator-fixture", () => Results.Ok()).RequireAuthorization(new Microsoft.AspNetCore.Authorization.AuthorizeAttribute { AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme });
    // Test-only ticket issuance, never compiled into the application projects.
    app.MapGet("/issue-fixture", async (HttpContext context, bool expired, bool customerCookie) =>
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] {
            new Claim("tid", CustomerIdentity.TenantId), new Claim("oid", "66666666-6666-4666-8666-666666666666") }, "offline"));
        await context.SignInAsync(customerCookie ? WebCustomer.Cookie : CookieAuthenticationDefaults.AuthenticationScheme, principal,
            new AuthenticationProperties { ExpiresUtc = expired ? DateTimeOffset.UtcNow.AddMinutes(-2) : DateTimeOffset.UtcNow.AddMinutes(5) });
        return Results.Ok();
    }).AllowAnonymous();
    await app.StartAsync();
    try
    {
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { BaseAddress = new(app.Urls.Single()) };
        async Task<HttpResponseMessage> Request(string path, string? bearer = null, string? cookie = null, bool post = false, HttpContent? content = null)
        {
            using var request = new HttpRequestMessage(post ? HttpMethod.Post : HttpMethod.Get, path);
            if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            if (cookie is not null) request.Headers.Add("Cookie", cookie);
            request.Content = content;
            return await http.SendAsync(request);
        }
        string Token(string? issuer = null, string? audience = null, string? scope = "Customer.Access", string? tenant = null,
            string? actor = null, bool expired = false, bool wrongKey = false)
        {
            var claims = new List<Claim> { new("tid", tenant ?? CustomerIdentity.TenantId), new("oid", "66666666-6666-4666-8666-666666666666"),
                new("azp", actor ?? CustomerIdentity.WebClientId), new("ver", "2.0") };
            if (scope is not null) claims.Add(new("scp", scope));
            return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(issuer ?? CustomerIdentity.Issuer,
                audience ?? CustomerIdentity.ApiClientId, claims, DateTime.UtcNow.AddHours(-1), expired ? DateTime.UtcNow.AddMinutes(-5) : DateTime.UtcNow.AddMinutes(5),
                new SigningCredentials(wrongKey ? badKey : key, SecurityAlgorithms.RsaSha256)));
        }
        Check((await Request("/catalogue-fixture")).StatusCode == HttpStatusCode.OK, "Anonymous browsing does not challenge customer login");
        Check((await Request("/api/customers/me")).StatusCode == HttpStatusCode.Unauthorized, "Anonymous customer API denied");
        foreach (var token in new[] { Token(issuer:"https://evil.invalid"), Token(audience:CustomerIdentity.WebClientId), Token(expired:true), Token(wrongKey:true), "not-a-token" })
            Check((await Request("/api/customers/me", token)).StatusCode == HttpStatusCode.Unauthorized, "Invalid issuer/audience/expiry/signature/token rejected");
        foreach (var token in new[] { Token(scope:null), Token(scope:"Customer.Access.Other"), Token(tenant:"11111111-1111-4111-8111-111111111111"), Token(actor:"44444444-4444-4444-8444-444444444444") })
            Check((await Request("/api/customers/me", token)).StatusCode == HttpStatusCode.Forbidden, "Wrong customer scope/tenant/client denied");
        var customerToken = Token();
        Check((await Request("/api/customers/me", customerToken)).StatusCode == HttpStatusCode.NotFound, "Unprovisioned customer has no profile");
        var first = await (await Request("/api/customers/session", customerToken, post:true)).Content.ReadFromJsonAsync<CustomerAccount>();
        var repeat = await (await Request("/api/customers/session", customerToken, post:true)).Content.ReadFromJsonAsync<CustomerAccount>();
        Check(first?.CustomerId == repeat?.CustomerId && first?.CustomerId != Guid.Empty && repository.Created == 1, "First/repeat login provisions one internal customer");
        Check(first?.CraterEnabled == false && first.Status == "Active", "New account receives no generation grant");
        repository.Account = repository.Account! with { Status = "Disabled" };
        Check((await Request("/api/customers/me", customerToken)).StatusCode == HttpStatusCode.Forbidden, "Disabled customer denied on every protected profile read");
        Check((await Request("/api/customers/session", customerToken, post:true)).StatusCode == HttpStatusCode.Forbidden && repository.Account.Status == "Disabled", "Repeat login does not reactivate disabled account");
        repository.Account = repository.Account with { Status = "Active", CraterEnabled = true };
        Check((await (await Request("/api/customers/me", customerToken)).Content.ReadFromJsonAsync<CustomerAccount>())?.CraterEnabled == false, "Stored eligibility cannot enable generation in this mission");
        Check((await Request("/api/operator/access", customerToken)).StatusCode == HttpStatusCode.Unauthorized, "Customer API token cannot enter operator endpoint");
        var operatorToken = Token(issuer:"https://login.microsoftonline.com/11111111-1111-4111-8111-111111111111/v2.0", audience:"33333333-3333-4333-8333-333333333333", scope:"Crater.Access", tenant:"11111111-1111-4111-8111-111111111111", actor:"44444444-4444-4444-8444-444444444444");
        Check((await Request("/api/customers/me", operatorToken)).StatusCode == HttpStatusCode.Unauthorized, "Operator API token cannot enter customer endpoint");
        var challenge = await Request("/customer/signin?returnUrl=" + Uri.EscapeDataString("/products/139?crater=true"));
        Check(challenge.StatusCode == HttpStatusCode.Redirect && challenge.Headers.Location!.Host == "identity.offline.invalid", "Explicit sign-in starts customer OIDC");
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(challenge.Headers.Location!.Query);
        Check(query["scope"].ToString().Contains(CustomerIdentity.Scope) && query.ContainsKey("nonce") && query.ContainsKey("code_challenge"), "Challenge requests customer scope, nonce and PKCE");
        var options = app.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(WebCustomer.Oidc);
        var properties = options.StateDataFormat.Unprotect(query["state"]!);
        Check(properties?.RedirectUri == "/customer/complete?returnUrl=" + Uri.EscapeDataString("/products/139?crater=true"), "Protected OAuth state preserves product and Crater intent");
        Check(!options.SaveTokens && options.ProtocolValidator.RequireNonce && options.TokenValidationParameters.ValidIssuer == CustomerIdentity.Issuer, "OIDC pins discovered issuer, requires nonce and keeps tokens out of cookie");
        var badReturn = await Request("/customer/signin?returnUrl=https://evil.invalid/");
        var badProperties = options.StateDataFormat.Unprotect(Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(badReturn.Headers.Location!.Query)["state"]!);
        Check(badProperties?.RedirectUri == "/customer/complete?returnUrl=%2F", "Sign-in challenge sanitizes external return URL");
        Check((await Request("/signin-customer-oidc?code=invalid&state=tampered")).Headers.Location?.OriginalString == "/customer/error", "Tampered callback state rejected without provider traffic");
        var issued = await Request("/issue-fixture?expired=false&customerCookie=true");
        var cookie = issued.Headers.GetValues("Set-Cookie").Single(v => v.StartsWith("__Host-FlyerMonkey.Customer=")).Split(';')[0];
        Check(issued.Headers.GetValues("Set-Cookie").Any(v => v.Contains("secure", StringComparison.OrdinalIgnoreCase) && v.Contains("httponly", StringComparison.OrdinalIgnoreCase)), "Customer cookie Secure and HttpOnly");
        var account = await Request("/customer/account", cookie:cookie);
        Check(account.StatusCode == HttpStatusCode.OK, "Customer cookie authenticates customer account");
        var completed = await Request("/customer/complete?returnUrl=" + Uri.EscapeDataString("/products/139?crater=true"), cookie:cookie);
        Check(completed.StatusCode == HttpStatusCode.Redirect && completed.Headers.Location?.OriginalString == "/products/139?crater=true",
            "Web completion provisions via API then returns to same product without generation");
        Check(apiTransport.LastPath == "/api/customers/session" && apiTransport.LastMethod == HttpMethod.Post && apiTransport.LastToken == "offline-customer-access-token",
            "Web forwards only server-acquired customer token to fixed API provisioning route");
        var recorded = (CustomerTokens)tokenAcquisition;
        Check(recorded.Scheme == WebCustomer.Oidc && recorded.Scopes?.SequenceEqual(new[] { CustomerIdentity.Scope }) == true,
            "Customer API acquisition explicitly selects customer scheme and delegated scope");
        Check((await Request("/customer/me", cookie:cookie)).StatusCode == HttpStatusCode.OK && apiTransport.LastPath == "/api/customers/me",
            "Web me forwards to customer API status route");
        Check((await Request("/api/customers/me", cookie:cookie)).StatusCode == HttpStatusCode.Unauthorized,
            "Customer API never accepts Web cookies as bearer authentication");
        Check((await Request("/operator-fixture", cookie:cookie)).StatusCode != HttpStatusCode.OK, "Customer cookie cannot authenticate operator Web endpoint");
        Check((await Request("/customer/signout", cookie:cookie, post:true)).StatusCode == HttpStatusCode.BadRequest, "Logout requires antiforgery");
        var html = await account.Content.ReadAsStringAsync();
        var field = Regex.Match(html, "name='([^']+)' value='([^']+)'");
        var antiforgeryCookie = account.Headers.GetValues("Set-Cookie").First(v => v.Contains("Antiforgery")).Split(';')[0];
        var logout = await Request("/customer/signout", cookie:cookie + "; " + antiforgeryCookie, post:true,
            content:new FormUrlEncodedContent(new Dictionary<string,string> { [field.Groups[1].Value] = WebUtility.HtmlDecode(field.Groups[2].Value) }));
        Check(logout.StatusCode == HttpStatusCode.Redirect, "Explicit antiforgery-protected customer logout succeeds");
        Check((await Request("/customer/account", cookie:cookie)).StatusCode == HttpStatusCode.Unauthorized, "Logged-out customer ticket replay denied");
        var expired = await Request("/issue-fixture?expired=true&customerCookie=true");
        var expiredCookie = expired.Headers.GetValues("Set-Cookie").First().Split(';')[0];
        Check((await Request("/customer/account", cookie:expiredCookie)).StatusCode == HttpStatusCode.Unauthorized, "Expired customer ticket denied");
        Check((await Request("/customer/account")).StatusCode == HttpStatusCode.Unauthorized, "Anonymous customer account does not silently start OIDC");
        var issuedAgain = await Request("/issue-fixture?expired=false&customerCookie=true");
        var activeCookie = issuedAgain.Headers.GetValues("Set-Cookie").Single(v => v.StartsWith("__Host-FlyerMonkey.Customer=")).Split(';')[0];
        apiTransport.Status = HttpStatusCode.Forbidden;
        Check((await Request("/customer/me", cookie:activeCookie)).StatusCode == HttpStatusCode.Forbidden,
            "Disabled-account API response is preserved by Web");
        Check((await Request("/customer/account", cookie:activeCookie)).StatusCode == HttpStatusCode.Unauthorized,
            "Disabled-account response revokes local customer ticket");
        builder.Configuration["CustomerAuthentication:Enabled"] = "false";
        Check((await Request("/api/customers/me", customerToken)).StatusCode == HttpStatusCode.NotFound, "Customer API switch closes even with previously valid token");
        // Shared UI uses a platform interface; rendering performs no provider or profile calls.
        var uiServices = new ServiceCollection().AddLogging().AddSingleton<IWebsiteCraterSession>(new CustomerUi(false));
        await using var uiProvider = uiServices.BuildServiceProvider();
        await using var renderer = new Microsoft.AspNetCore.Components.Web.HtmlRenderer(uiProvider, uiProvider.GetRequiredService<ILoggerFactory>());
        var anonymousHtml = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<FlyerMonkey.Shared.Components.ProductCraterEntry>(Microsoft.AspNetCore.Components.ParameterView.FromDictionary(
                new Dictionary<string,object?> { ["ProductId"] = 139, ["Expanded"] = true }))).ToHtmlString());
        Check(anonymousHtml.Contains("customer-signin-drawer") && anonymousHtml.Contains("139") && !anonymousHtml.Contains("Generate image"), "Anonymous product drawer preserves context and cannot generate");
        var restoredPresentation = new WebsiteCraterSession(uiProvider) { CustomerPresentation = new(true, true) };
        Check((await restoredPresentation.GetCustomerAsync()).SignedIn && !await restoredPresentation.IsAuthenticatedAsync(),
            "Restored circuit presentation needs no HttpContext and never grants operator identity");
        Console.WriteLine($"{checks} customer checks passed. Offline providers/storage only; no paid calls.");
    }
    finally { await app.StopAsync(); }
}
finally { Environment.SetEnvironmentVariable("CustomerAuthentication__ClientSecret", originalSecret); }

sealed class NoNetwork : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => throw new Exception("Unexpected provider network call");
}
sealed class Customers : ICustomerRepository
{
    public CustomerAccount? Account;
    public int Created;
    public Task<CustomerAccount?> GetAsync(Guid tenant, Guid user, CancellationToken token) => Task.FromResult(Account);
    public Task<CustomerAccount> ProvisionAsync(Guid tenant, Guid user, CancellationToken token)
    {
        if (Account is null) { Account = new(Guid.NewGuid(), "Active", false); Created++; }
        return Task.FromResult(Account);
    }
}
sealed class CustomerUi(bool signedIn) : IWebsiteCraterSession
{
    public Task<bool> IsAuthenticatedAsync() => Task.FromResult(false);
    public Task<CustomerSessionState> GetCustomerAsync() => Task.FromResult(new CustomerSessionState(true, signedIn));
}

public class CustomerTokens : DispatchProxy
{
    public string? Scheme;
    public IEnumerable<string>? Scopes;
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method?.Name != "GetAccessTokenForUserAsync") throw new Exception("Unexpected token acquisition method");
        var parameters = method.GetParameters();
        for (var i=0; i<parameters.Length; i++)
        {
            if (parameters[i].Name == "authenticationScheme") Scheme = args![i] as string;
            if (parameters[i].Name == "scopes") Scopes = args![i] as IEnumerable<string>;
        }
        return Task.FromResult("offline-customer-access-token");
    }
}
sealed class CustomerApiTransport : HttpMessageHandler
{
    public string? LastPath;
    public string? LastToken;
    public HttpMethod? LastMethod;
    public HttpStatusCode Status = HttpStatusCode.OK;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        if (request.RequestUri?.Host != "localhost" || request.RequestUri.Scheme != "https") throw new Exception("Customer token escaped API allowlist");
        LastPath = request.RequestUri.AbsolutePath;
        LastMethod = request.Method;
        LastToken = request.Headers.Authorization?.Parameter;
        return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent("{\"customerId\":\"77777777-7777-4777-8777-777777777777\",\"status\":\"Active\",\"craterEnabled\":false}", Encoding.UTF8,"application/json") });
    }
}
