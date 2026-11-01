using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using FlyerMonkey.Shared.Model;
using FlyerMonkey.Shared.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Identity.Web;

namespace FlyerMonkey.Web.Services;

public static class CustomerCrater
{
    public static void MapCustomerCrater(this WebApplication app)
    {
        app.MapGet("/customer/crater/139", async (HttpContext context, IAntiforgery antiforgery) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var body = "<h1>Customer Crater It — Product 139</h1><p>LOCAL DEVELOPMENT. A generation attempt uses the paid image API. Artwork stays private.</p>";
            if (!Enabled(context)) return Html(body + "<p>Not eligible: customer generation is disabled.</p>");
            using var response = await Call(context, HttpMethod.Get, "attempt");
            if (response is null || !response.IsSuccessStatusCode)
                return Html(body + "<p>Not eligible or status unavailable. Generation is disabled. Check sign-in, eligibility and local configuration.</p>");
            CustomerCraterAttempt? attempt;
            try { attempt = await response.Content.ReadFromJsonAsync<CustomerCraterAttempt>(); }
            catch (Exception) { return Html(body + "<p>Status unavailable. Do not start another generation.</p>"); }
            if (attempt is null) return Html(body + "<p>Status unavailable.</p>");
            body += "<p role='status'>" + HtmlEncoder.Default.Encode(attempt.State) + "</p>";
            if (attempt.State == "Ready" && !attempt.CanGenerate) body += "<p>Not eligible: server permission is required.</p>";
            if (attempt.State is "Failed" or "Unknown") body += "<p>No further generation is allowed. The outcome may have incurred a charge. Ask the operator to inspect the attempt; refreshing status never generates an image.</p>";
            if (attempt.CanGenerate)
            {
                var tokens = antiforgery.GetAndStoreTokens(context);
                body += "<form method='post' action='/customer/crater/139/attempt' onsubmit='this.querySelector(\"button\").disabled=true'>" +
                    $"<input type='hidden' name='{HtmlEncoder.Default.Encode(tokens.FormFieldName)}' value='{HtmlEncoder.Default.Encode(tokens.RequestToken!)}'>" +
                    "<button type='submit'>Generate one image</button></form>";
            }
            else body += "<button disabled>Generation unavailable</button>";
            if (attempt.HasImage) body += "<img src='/customer/crater/139/image' alt='Private customer product artwork' style='max-width:100%'>";
            if (attempt.State == "Completed" && !attempt.HasImage) body += "<p>Preview unavailable. Refresh status or preview; never generate again.</p>";
            return Html(body + "<p><a href='/customer/crater/139'>Refresh status / retry preview</a></p>");
        }).RequireAuthorization(CustomerAuthentication.Policy);
        app.MapPost("/customer/crater/139/attempt", async (HttpContext context, IAntiforgery antiforgery) =>
        {
            try { await antiforgery.ValidateRequestAsync(context); }
            catch (AntiforgeryValidationException) { return Results.BadRequest(); }
            if (!Enabled(context)) return Results.NotFound();
            using var response = await Call(context, HttpMethod.Post, "attempt");
            // Redirect only to status: never retry a POST, including when transport outcome is unknown.
            return Results.LocalRedirect("/customer/crater/139");
        }).RequireAuthorization(CustomerAuthentication.Policy);
        app.MapGet("/customer/crater/139/image", async (HttpContext context) =>
        {
            if (!Enabled(context)) return Results.NotFound();
            using var response = await Call(context, HttpMethod.Get, "image");
            if (response is null || !response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType != "image/jpeg")
                return Results.NotFound();
            return Results.File(await response.Content.ReadAsByteArrayAsync(), "image/jpeg");
        }).RequireAuthorization(CustomerAuthentication.Policy);
    }
    private static IResult Html(string body) => Results.Content(body + "<p><a href='/products/139?crater=true'>Back to product</a></p>", "text/html");
    private static bool Enabled(HttpContext context) =>
        context.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment() &&
        context.RequestServices.GetRequiredService<IConfiguration>().GetValue<bool>("CustomerCrater:Enabled") &&
        context.RequestServices.GetRequiredService<IConfiguration>().GetValue<bool>("CraterIt:Enabled");
    private static async Task<HttpResponseMessage?> Call(HttpContext context, HttpMethod method, string operation)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            var token = await context.RequestServices.GetRequiredService<ITokenAcquisition>()
                .GetAccessTokenForUserAsync([CustomerIdentity.Scope], authenticationScheme: CustomerAuthentication.Oidc, user: context.User);
            var http = context.RequestServices.GetRequiredService<IHttpClientFactory>().CreateClient("CustomerCraterApi");
            using var request = new HttpRequestMessage(method, "api/customers/crater/139/" + operation);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return await http.SendAsync(request); // No browser cancellation can initiate a replacement request.
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or MicrosoftIdentityWebChallengeUserException)
        { return null; }
    }
}
