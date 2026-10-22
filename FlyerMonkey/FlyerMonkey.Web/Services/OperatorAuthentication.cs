using System.Security.Claims;
using FlyerMonkey.Shared.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Identity.Web;

namespace FlyerMonkey.Web.Services;

public static class OperatorAuthentication
{
    public static bool AddOperatorAuthentication(this IServiceCollection services, IConfiguration configuration,
        IWebHostEnvironment? environment = null)
    {
        if (!configuration.GetValue<bool>("Authentication:Enabled")) return false;
        foreach (var key in new[] { "TenantId", "WebClientId", "ApiClientId", "OperatorObjectId" })
            if (!Guid.TryParse(configuration[$"Authentication:{key}"], out var id) || id == Guid.Empty)
                throw new InvalidOperationException($"Configure Authentication:{key}.");
        if (string.IsNullOrWhiteSpace(configuration["Authentication:WebClientSecret"]))
            throw new InvalidOperationException("Configure Authentication:WebClientSecret in the Web project's local User Secrets before enabling authentication.");
        var apiBaseUri = RequiredApiBaseUri(configuration, environment);

        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                // Only /operator/signin may start OIDC. A protected request must never silently recreate a session.
                options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultForbidScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            })
            .AddMicrosoftIdentityWebApp(options =>
            {
                options.Instance = "https://login.microsoftonline.com/";
                options.TenantId = configuration["Authentication:TenantId"];
                options.ClientId = configuration["Authentication:WebClientId"];
                options.ClientSecret = configuration["Authentication:WebClientSecret"];
                options.CallbackPath = "/signin-oidc";
                options.SignedOutCallbackPath = "/signout-callback-oidc";
                options.RemoteSignOutPath = "/signout-oidc";
                options.ResponseType = "code";
                options.UsePkce = true;
                options.SaveTokens = false;
                options.MapInboundClaims = false;
                options.GetClaimsFromUserInfoEndpoint = false;
                options.Events.OnTokenValidated = context =>
                {
                    var principal = context.Principal!;
                    if (!Guid.TryParse(principal.FindFirstValue("tid"), out var tid) ||
                        tid != Guid.Parse(configuration["Authentication:TenantId"]!) ||
                        !Guid.TryParse(principal.FindFirstValue("oid"), out var oid) ||
                        oid != Guid.Parse(configuration["Authentication:OperatorObjectId"]!))
                        context.Fail("Operator access denied.");
                    return Task.CompletedTask;
                };
            })
            .EnableTokenAcquisitionToCallDownstreamApi([$"api://{configuration["Authentication:ApiClientId"]}/Crater.Access"])
            .AddInMemoryTokenCaches();
        services.Configure<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme, options =>
        {
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.ExpireTimeSpan = TimeSpan.FromHours(1);
            options.SlidingExpiration = false;
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.Headers.CacheControl = "no-store";
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "text/plain; charset=utf-8";
                return context.Response.WriteAsync("Sign in explicitly at /operator/signin.");
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.Headers.CacheControl = "no-store";
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "text/plain; charset=utf-8";
                return context.Response.WriteAsync("Operator access denied.");
            };
        });
        services.AddSingleton<OperatorTicketStore>();
        services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
            .Configure<OperatorTicketStore>((options, store) => options.SessionStore = store);
        services.AddAuthorization();
        services.AddCascadingAuthenticationState();
        services.AddScoped<IOperatorTokenProvider, WebOperatorTokens>();
        services.AddScoped(sp => new OperatorArtworkClient(
            new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }),
            sp.GetRequiredService<IOperatorTokenProvider>(),
            apiBaseUri));
        return true;
    }

    public static Uri RequiredApiBaseUri(IConfiguration configuration, IWebHostEnvironment? environment = null)
    {
        if (!Uri.TryCreate(configuration["Authentication:ApiBaseUrl"], UriKind.Absolute, out var uri) ||
            uri.Scheme != "https" || uri.AbsolutePath != "/" || uri.UserInfo != "" || uri.Query != "" || uri.Fragment != "" ||
            environment?.IsDevelopment() == true && !uri.IsLoopback)
            throw new InvalidOperationException("Configure Authentication:ApiBaseUrl as an explicit HTTPS API root; Development requires loopback.");
        return uri;
    }

    public static void MapOperatorAuthentication(this WebApplication app)
    {
        app.MapGet("/operator/signin", () => Results.Challenge(
            new AuthenticationProperties { RedirectUri = "/operator" }, [OpenIdConnectDefaults.AuthenticationScheme]));
        ProtectLogout(app.MapPost("/operator/signout", async (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            // Commit local deletion/revocation in this response, without invoking provider discovery/logout.
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.LocalRedirect("/operator/signed-out");
        }).RequireAuthorization());
        app.MapGet("/operator/signed-out", (HttpContext context, Microsoft.AspNetCore.Antiforgery.IAntiforgery antiforgery) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (context.User.Identity?.IsAuthenticated == true) return Results.LocalRedirect("/operator");
            var tokens = antiforgery.GetAndStoreTokens(context);
            return Results.Content($"<p>Signed out of FlyerMonkey. Microsoft account sign-out is optional.</p><p><a href='/'>Public catalogue</a> | <a href='/operator/signin'>Sign in explicitly</a></p><form method='post' action='/operator/signout/microsoft'><input type='hidden' name='{tokens.FormFieldName}' value='{System.Net.WebUtility.HtmlEncode(tokens.RequestToken)}'><button>Also sign out of Microsoft</button></form>", "text/html");
        }).AllowAnonymous();
        ProtectLogout(app.MapPost("/operator/signout/microsoft", async (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            // Also revoke locally if this optional endpoint was visited with an active session.
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.SignOut(new AuthenticationProperties { RedirectUri = "/" },
                [OpenIdConnectDefaults.AuthenticationScheme]);
        }).AllowAnonymous());
        app.MapGet("/operator", (HttpContext context, Microsoft.AspNetCore.Antiforgery.IAntiforgery antiforgery) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var tokens = antiforgery.GetAndStoreTokens(context);
            var accessLink = app.Environment.IsDevelopment() ? "<p><a href='/operator/access'>Check local identity and API permissions</a></p>" : "";
            return Results.Content($"<p>Operator signed in. Production generation remains disabled.</p>{accessLink}<form method='get' action='/operator/artwork'><label>Product ID <input type='number' name='productId' min='1' required></label><button>View my artwork</button></form><form method='post' action='/operator/signout'><input type='hidden' name='{tokens.FormFieldName}' value='{System.Net.WebUtility.HtmlEncode(tokens.RequestToken)}'><button>Sign out</button></form>", "text/html");
        }).RequireAuthorization();
        app.MapGet("/operator/artwork", async (int productId, HttpContext context, ITokenAcquisition acquisition, IConfiguration configuration) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (productId <= 0) return Results.BadRequest();
            try
            {
                var token = await acquisition.GetAccessTokenForUserAsync(
                    [$"api://{configuration["Authentication:ApiClientId"]}/Crater.Access"],
                    authenticationScheme: OpenIdConnectDefaults.AuthenticationScheme, user: context.User);
                using var client = new OperatorArtworkClient(new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }),
                    new RequestOperatorTokens(token), RequiredApiBaseUri(configuration, app.Environment));
                using var response = await client.GetAsync($"api/images/artwork?productId={productId}", context.RequestAborted);
                return Results.Content(await response.Content.ReadAsStringAsync(context.RequestAborted), "application/json");
            }
            catch (MicrosoftIdentityWebChallengeUserException)
            {
                return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Sign in explicitly at /operator/signin.");
            }
            catch (HttpRequestException) { return Results.StatusCode(503); }
        }).RequireAuthorization();
        if (app.Environment.IsDevelopment())
        {
            app.MapGet("/operator/access", async (HttpContext context, ITokenAcquisition acquisition, IConfiguration configuration) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                try
                {
                    var token = await acquisition.GetAccessTokenForUserAsync(
                        [$"api://{configuration["Authentication:ApiClientId"]}/Crater.Access"],
                        authenticationScheme: OpenIdConnectDefaults.AuthenticationScheme, user: context.User);
                    using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
                    using var request = new HttpRequestMessage(HttpMethod.Get,
                        new Uri(RequiredApiBaseUri(configuration, app.Environment), "api/operator/access"));
                    request.Headers.Authorization = new("Bearer", token);
                    using var response = await http.SendAsync(request, context.RequestAborted);
                    if (!response.IsSuccessStatusCode) return Results.StatusCode((int)response.StatusCode);
                    return Results.Content(await response.Content.ReadAsStringAsync(context.RequestAborted), "application/json");
                }
                catch (MicrosoftIdentityWebChallengeUserException)
                {
                    return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Sign in explicitly at /operator/signin.");
                }
                catch (HttpRequestException) { return Results.StatusCode(503); }
            }).RequireAuthorization();
        }
    }

    private static RouteHandlerBuilder ProtectLogout(RouteHandlerBuilder endpoint) =>
        endpoint.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            try
            {
                await context.HttpContext.RequestServices.GetRequiredService<Microsoft.AspNetCore.Antiforgery.IAntiforgery>()
                    .ValidateRequestAsync(context.HttpContext);
            }
            catch (Microsoft.AspNetCore.Antiforgery.AntiforgeryValidationException) { return Results.BadRequest(); }
            return await next(context);
        });
}

internal sealed class RequestOperatorTokens(string token) : IOperatorTokenProvider
{
    public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default) => Task.FromResult(token);
}

internal sealed class WebOperatorTokens(ITokenAcquisition acquisition, AuthenticationStateProvider state,
    IConfiguration configuration) : IOperatorTokenProvider
{
    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = (await state.GetAuthenticationStateAsync()).User;
        if (user.Identity?.IsAuthenticated != true) throw new UnauthorizedAccessException();
        return await acquisition.GetAccessTokenForUserAsync(
            [$"api://{configuration["Authentication:ApiClientId"]}/Crater.Access"],
            authenticationScheme: OpenIdConnectDefaults.AuthenticationScheme, user: user);
    }
}
