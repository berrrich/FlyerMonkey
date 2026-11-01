using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using FlyerMonkey.Shared.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Tokens;

namespace FlyerMonkey.Web.Services;

public static class CustomerAuthentication
{
    public const string Cookie = "CustomerCookie";
    public const string Oidc = "CustomerOidc";
    public const string Policy = "CustomerSession";
    private const string AnonymousDefault = "CustomerAnonymousDefault";

    // Deliberately narrow local destinations. No browser-supplied host, fragment or nested redirect.
    public static string SafeReturn(string? value) => value is not null &&
        (value == "/" || Regex.IsMatch(value, @"\A/products/[1-9][0-9]{0,8}(\?crater=true)?\z", RegexOptions.CultureInvariant))
        ? value : "/";

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
                options.Events.OnRemoteFailure = context =>
                {
                    context.HandleResponse();
                    context.Response.Redirect("/customer/error");
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
            options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
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
        app.MapGet("/customer/signin", (string? returnUrl) => Results.Challenge(new AuthenticationProperties
        {
            RedirectUri = "/customer/complete?returnUrl=" + Uri.EscapeDataString(SafeReturn(returnUrl))
        }, [Oidc])).AllowAnonymous();
        app.MapGet("/customer/complete", async (HttpContext context, string? returnUrl) =>
        {
            using var response = await CallApi(context, true);
            if (response?.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
                return Results.LocalRedirect("/customer/error");
            if (response is null || !response.IsSuccessStatusCode)
                return Results.LocalRedirect("/customer/account?status=unavailable");
            return Results.LocalRedirect(SafeReturn(returnUrl));
        }).RequireAuthorization(Policy);
        app.MapGet("/customer/me", async (HttpContext context) =>
        {
            using var response = await CallApi(context, false);
            if (response is null) return Results.StatusCode(503);
            if (!response.IsSuccessStatusCode) return Results.StatusCode((int)response.StatusCode);
            return Results.Content(await response.Content.ReadAsStringAsync(context.RequestAborted), "application/json");
        }).RequireAuthorization(Policy);
        app.MapGet("/customer/account", (HttpContext context,
            Microsoft.AspNetCore.Antiforgery.IAntiforgery antiforgery) =>
        {
            var token = antiforgery.GetAndStoreTokens(context);
            return Results.Content("<h1>Customer account</h1><p>Signed in to FlyerMonkey. " +
                "Customer generation is disabled. <a href='/customer/me'>Check account status</a>. " +
                "If account storage is unavailable, complete the local database setup.</p>" +
                $"<form method='post' action='/customer/signout'><input type='hidden' name='{HtmlEncoder.Default.Encode(token.FormFieldName)}' " +
                $"value='{HtmlEncoder.Default.Encode(token.RequestToken!)}'><button>Sign out of FlyerMonkey</button></form><a href='/'>Browse offers</a>", "text/html");
        }).RequireAuthorization(Policy);
        app.MapPost("/customer/signout", async (HttpContext context) =>
        {
            try { await context.RequestServices.GetRequiredService<Microsoft.AspNetCore.Antiforgery.IAntiforgery>().ValidateRequestAsync(context); }
            catch (Microsoft.AspNetCore.Antiforgery.AntiforgeryValidationException) { return Results.BadRequest(); }
            await context.SignOutAsync(Cookie);
            return Results.LocalRedirect("/customer/signed-out");
        }).RequireAuthorization(Policy);
        app.MapGet("/customer/signed-out", () => Results.Content("<p>Signed out of FlyerMonkey on this browser. Your identity-provider session may still be active.</p><a href='/'>Browse offers</a>", "text/html")).AllowAnonymous();
        app.MapGet("/customer/error", () => Results.Content("<p>Customer sign-in could not be completed. Try signing in again or return to the catalogue.</p><a href='/customer/signin'>Sign in</a> | <a href='/'>Browse offers</a>", "text/html")).AllowAnonymous();
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/customer")) context.Response.Headers.CacheControl = "no-store";
            await next(context);
        });
    }

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
        catch (MicrosoftIdentityWebChallengeUserException) { await context.SignOutAsync(Cookie); return null; }
        catch (HttpRequestException) { return null; }
        catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested) { return null; }
    }
}
