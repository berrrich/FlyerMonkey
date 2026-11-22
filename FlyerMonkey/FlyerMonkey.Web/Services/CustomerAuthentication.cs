using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using FlyerMonkey.Shared.Model;
using FlyerMonkey.Shared.Services;
using FlyerMonkey.Web.Components.Pages;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Tokens;

namespace FlyerMonkey.Web.Services;

public static class CustomerAuthentication
{
    public const string Cookie = "CustomerCookie";
    public const string Oidc = "CustomerOidc";
    public const string Policy = "CustomerSession";
    private const string AnonymousDefault = "CustomerAnonymousDefault";
    private const string AccountPath = "/customer/account";

    // Deliberately narrow local destinations. No browser-supplied host, fragment or nested redirect.
    public static string SafeReturn(string? value) => value is not null &&
        (value is "/" or AccountPath ||
            Regex.IsMatch(value, @"\A/products/[1-9][0-9]{0,8}(\?crater=true)?\z", RegexOptions.CultureInvariant) ||
            IsCraterPage(value))
        ? value : "/";

    private static bool IsCraterPage(string path) =>
        Regex.IsMatch(path, @"\A/customer/crater/[1-9][0-9]{0,9}\z", RegexOptions.CultureInvariant);

    private static int? ProductFromReturn(string destination)
    {
        var path = destination.Split('?', 2)[0];
        return int.TryParse(path.AsSpan(path.LastIndexOf('/') + 1), out var id) && id > 0 ? id : null;
    }

    public static Uri ApiUri(IConfiguration configuration, IWebHostEnvironment environment)
    {
        if (!Uri.TryCreate(configuration["CustomerAuthentication:ApiBaseUrl"], UriKind.Absolute, out var uri) ||
            uri.Scheme != "https" || uri.AbsolutePath != "/" || uri.UserInfo != "" || uri.Query != "" || uri.Fragment != "" ||
            environment.IsDevelopment() && !uri.IsLoopback)
            throw new InvalidOperationException("Configure CustomerAuthentication:ApiBaseUrl as an HTTPS API root; Development requires loopback.");
        return uri;
    }

    public static bool AddCustomerAuthentication(this IServiceCollection services, IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        services.AddHttpContextAccessor();
        if (!configuration.GetValue<bool>("CustomerAuthentication:Enabled")) return false;
        _ = ApiUri(configuration, environment);
        // This credential source deliberately excludes appsettings, command line and tracked configuration.
        var secret = Environment.GetEnvironmentVariable("CustomerAuthentication__ClientSecret") ??
            new ConfigurationBuilder().AddUserSecrets(typeof(CustomerAuthentication).Assembly, optional: true)
                .Build()["CustomerAuthentication:ClientSecret"];
        if (string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException("Set CustomerAuthentication:ClientSecret in Web User Secrets or CustomerAuthentication__ClientSecret in the environment.");

        services.AddAuthentication().AddCookie(AnonymousDefault)
            .AddMicrosoftIdentityWebApp(options =>
            {
                options.Instance = "https://flyermonkeycustomers.ciamlogin.com/";
                options.TenantId = CustomerIdentity.TenantId;
                options.ClientId = CustomerIdentity.WebClientId;
                options.ClientSecret = secret;
                options.Authority = CustomerIdentity.Authority;
                options.MetadataAddress = CustomerIdentity.MetadataAddress;
                options.CallbackPath = "/signin-customer-oidc";
                options.SignedOutCallbackPath = "/signout-customer-callback";
                options.RemoteSignOutPath = "/signout-customer-oidc";
                options.ResponseType = "code";
                options.UsePkce = true;
                options.SaveTokens = false;
                options.MapInboundClaims = false;
                options.GetClaimsFromUserInfoEndpoint = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true, ValidIssuer = CustomerIdentity.Issuer,
                    IssuerValidator = (issuer, _, _) => issuer == CustomerIdentity.Issuer ? issuer :
                        throw new SecurityTokenInvalidIssuerException("Customer issuer rejected."),
                    ValidateAudience = true, ValidAudience = CustomerIdentity.WebClientId,
                    ValidateLifetime = true, RequireExpirationTime = true,
                    RequireSignedTokens = true, ValidateIssuerSigningKey = true,
                    ClockSkew = TimeSpan.FromSeconds(30), ValidAlgorithms = [SecurityAlgorithms.RsaSha256]
                };
                options.ProtocolValidator.RequireNonce = true;
                options.Events.OnTokenValidated = context =>
                {
                    if (!IsCustomer(context.Principal)) context.Fail("Customer identity rejected.");
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToIdentityProvider = context =>
                {
                    context.ProtocolMessage.Prompt = "select_account";
                    return Task.CompletedTask;
                };
                options.Events.OnRemoteFailure = context =>
                {
                    context.HandleResponse();
                    var retry = context.Properties?.GetString("customerReturn");
                    context.Response.Redirect(retry is null ? "/customer/error" :
                        "/customer/error?returnUrl=" + Uri.EscapeDataString(SafeReturn(retry)));
                    return Task.CompletedTask;
                };
            }, openIdConnectScheme: Oidc, cookieScheme: Cookie)
            .EnableTokenAcquisitionToCallDownstreamApi([CustomerIdentity.Scope]).AddInMemoryTokenCaches();
        // A customer cookie must NEVER become the default operator principal, even with operator auth disabled.
        services.PostConfigure<AuthenticationOptions>(options =>
        {
            options.DefaultAuthenticateScheme ??= AnonymousDefault;
            options.DefaultChallengeScheme ??= AnonymousDefault;
            options.DefaultSignInScheme ??= AnonymousDefault;
        });
        services.AddKeyedSingleton<ITicketStore>(Cookie, (_, _) => new OperatorTicketStore());
        services.AddOptions<CookieAuthenticationOptions>(Cookie).Configure<IServiceProvider>((options, sp) =>
        {
            options.Cookie.Name = "__Host-FlyerMonkey.Customer";
            options.Cookie.Path = "/";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.ExpireTimeSpan = TimeSpan.FromHours(1);
            options.SlidingExpiration = false;
            options.SessionStore = sp.GetRequiredKeyedService<ITicketStore>(Cookie);
            options.Events.OnValidatePrincipal = context =>
            {
                if (!IsCustomer(context.Principal)) context.RejectPrincipal();
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToLogin = context =>
            {
                var path = context.Request.Path.Value;
                if (HttpMethods.IsGet(context.Request.Method) && path is not null && IsCraterPage(path))
                    context.Response.Redirect(AccountPath + "?returnUrl=" + Uri.EscapeDataString(path));
                else context.Response.StatusCode = 401;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
        });
        services.AddAuthorization(options => options.AddPolicy(Policy, policy =>
            policy.AddAuthenticationSchemes(Cookie).RequireAuthenticatedUser().RequireAssertion(c => IsCustomer(c.User))));
        services.AddHttpClient("CustomerApi", client =>
        {
            client.BaseAddress = ApiUri(configuration, environment);
            client.Timeout = TimeSpan.FromSeconds(15);
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddHttpClient("CustomerCraterApi", client =>
        {
            client.BaseAddress = ApiUri(configuration, environment);
            client.Timeout = TimeSpan.FromMinutes(3);
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        return true;
    }

    public static bool IsCustomer(ClaimsPrincipal? principal) => principal?.Identity?.IsAuthenticated == true &&
        principal.FindFirstValue("tid") == CustomerIdentity.TenantId &&
        Guid.TryParse(principal.FindFirstValue("oid"), out var user) && user != Guid.Empty;

    public static void MapCustomerAuthentication(this WebApplication app)
    {
        app.MapGet("/customer/signin", (string? returnUrl) =>
        {
            var destination = SafeReturn(returnUrl);
            var properties = new AuthenticationProperties
            {
                RedirectUri = "/customer/complete?returnUrl=" + Uri.EscapeDataString(destination)
            };
            properties.Items["customerReturn"] = destination;
            return Results.Challenge(properties, [Oidc]);
        }).AllowAnonymous();
        app.MapGet("/customer/complete", async (HttpContext context, string? returnUrl) =>
        {
            using var response = await CallApi(context, true);
            if (response?.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
                return Results.LocalRedirect("/customer/error?returnUrl=" + Uri.EscapeDataString(SafeReturn(returnUrl)));
            if (response is null || !response.IsSuccessStatusCode)
                return Results.LocalRedirect(AccountPath + "?returnUrl=" + Uri.EscapeDataString(SafeReturn(returnUrl)));
            return Results.LocalRedirect(SafeReturn(returnUrl));
        }).RequireAuthorization(Policy);
        app.MapGet("/customer/me", async (HttpContext context) =>
        {
            using var response = await CallApi(context, false);
            if (response is null) return Results.StatusCode(503);
            if (!response.IsSuccessStatusCode) return Results.StatusCode((int)response.StatusCode);
            return Results.Content(await response.Content.ReadAsStringAsync(context.RequestAborted), "application/json");
        }).RequireAuthorization(Policy);
        app.MapGet(AccountPath, async (HttpContext context, string? returnUrl,
            Microsoft.AspNetCore.Antiforgery.IAntiforgery antiforgery) =>
        {
            var destination = SafeReturn(returnUrl ?? AccountPath);
            var productId = ProductFromReturn(destination);
            var authentication = await context.AuthenticateAsync(Cookie);
            if (!authentication.Succeeded || !IsCustomer(authentication.Principal))
                return AccountPage("Sign in to FlyerMonkey to check your account and Crater It access.", destination, 401);

            context.User = authentication.Principal!;
            using var response = await CallApi(context, false);
            if (response?.StatusCode == HttpStatusCode.Unauthorized)
                return AccountPage("Your customer session has expired. Sign in again to continue.", destination, 401);
            if (response?.StatusCode == HttpStatusCode.Forbidden)
                return AccountPage("Customer access was denied. You can still browse products and offers.", destination, 403);

            CustomerAccount? account = null;
            if (response?.IsSuccessStatusCode == true)
            {
                try
                {
                    account = await response.Content.ReadFromJsonAsync<CustomerAccount>(context.RequestAborted);
                    if (account is not { Status: "Active" } || account.CustomerId == Guid.Empty) account = null;
                }
                catch (System.Text.Json.JsonException) { }
                catch (HttpRequestException) { }
            }
            var token = antiforgery.GetAndStoreTokens(context);
            return new RazorComponentResult<CustomerAccountDocument>(new
            {
                DisplayName = context.User.FindFirstValue("name"),
                Email = CustomerIdentity.DisplayEmail(context.User),
                Account = account,
                CraterStatus = account is null ? null : await CustomerCrater.ReadAccountStatusAsync(context, productId ?? 139),
                SignOutToken = (token.FormFieldName, token.RequestToken!),
                Message = response?.StatusCode == HttpStatusCode.NotFound
                    ? "No active customer profile was found. Crater It is unavailable for this account."
                    : account is null ? "Customer status is temporarily unavailable. Refresh this page to try again." : null,
                ReturnPath = destination,
                ProductId = productId
            });
        }).AllowAnonymous();
        app.MapPost("/customer/signout", async (HttpContext context, string? returnUrl) =>
        {
            try { await context.RequestServices.GetRequiredService<Microsoft.AspNetCore.Antiforgery.IAntiforgery>().ValidateRequestAsync(context); }
            catch (Microsoft.AspNetCore.Antiforgery.AntiforgeryValidationException) { return Results.BadRequest(); }
            await context.SignOutAsync(Cookie);
            return Results.LocalRedirect("/customer/signed-out?returnUrl=" +
                Uri.EscapeDataString(SafeReturn(returnUrl ?? AccountPath)));
        }).RequireAuthorization(Policy);
        app.MapGet("/customer/signed-out", (string? returnUrl) =>
            AccountPage("Signed out of FlyerMonkey in this browser.", SafeReturn(returnUrl ?? AccountPath))).AllowAnonymous();
        app.MapGet("/customer/error", (string? returnUrl) =>
            AccountPage("Customer sign-in was cancelled or could not be completed. You can try again or keep browsing.",
                SafeReturn(returnUrl ?? AccountPath))).AllowAnonymous();
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/customer")) context.Response.Headers.CacheControl = "no-store";
            await next(context);
        });
    }

    private static RazorComponentResult<CustomerAccountDocument> AccountPage(string message, string destination, int status = 200) =>
        new(new { Message = message, ReturnPath = destination }) { StatusCode = status };

    private static async Task<HttpResponseMessage?> CallApi(HttpContext context, bool provision)
    {
        try
        {
            var acquisition = context.RequestServices.GetRequiredService<ITokenAcquisition>();
            var token = await acquisition.GetAccessTokenForUserAsync([CustomerIdentity.Scope],
                authenticationScheme: Oidc, user: context.User);
            var client = context.RequestServices.GetRequiredService<IHttpClientFactory>().CreateClient("CustomerApi");
            using var request = new HttpRequestMessage(provision ? HttpMethod.Post : HttpMethod.Get,
                provision ? "api/customers/session" : "api/customers/me");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var response = await client.SendAsync(request, context.RequestAborted);
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
                await context.SignOutAsync(Cookie);
            return response;
        }
        catch (MicrosoftIdentityWebChallengeUserException)
        {
            await context.SignOutAsync(Cookie);
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        }
        catch (HttpRequestException) { return null; }
        catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested) { return null; }
    }
}
