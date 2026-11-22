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
        app.MapGet("/customer/crater/{productId:int:min(1)}", async (int productId, HttpContext context, IAntiforgery antiforgery) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var body = $"<h1>Customer Crater It — Product {productId}</h1><p>LOCAL DEVELOPMENT. A generation attempt uses the paid image API. Artwork stays private.</p>";
            if (!Enabled(context)) return Html(productId, body + "<p>Not eligible: customer generation is disabled.</p>");
            using var response = await Call(context, HttpMethod.Get, "attempt", productId);
            if (response is null || !response.IsSuccessStatusCode)
                return Html(productId, body + "<p>Not eligible or status unavailable. Generation is disabled. Check sign-in, eligibility and local configuration.</p>");
            CustomerCraterAttempt? attempt;
            try { attempt = await response.Content.ReadFromJsonAsync<CustomerCraterAttempt>(); }
            catch (Exception) { return Html(productId, body + "<p>Status unavailable. Do not start another generation.</p>"); }
            if (attempt is null) return Html(productId, body + "<p>Status unavailable.</p>");
            var status = attempt.State switch
            {
                "DailyLimitReached" => "The shared daily generation limit has been reached. Try again after midnight UTC.",
                "AccountLimitReached" => "Your account has used its available Crater It attempts.",
                _ => attempt.State
            };
            body += "<p role='status'>" + HtmlEncoder.Default.Encode(status) + "</p>";
            if (context.Request.Query["classic"] == "published")
                body += "<p role='status'>Classic Crater artwork is now the default catalogue image.</p>";
            if (context.Request.Query["classic"] == "unavailable")
                body += "<p role='status'>Classic Crater could not be confirmed. Refresh the product image before trying again.</p>";
            if (attempt.State == "Ready" && !attempt.CanGenerate) body += "<p>Not eligible: server permission is required.</p>";
            if (attempt.State is "Failed" or "Unknown") body += "<p>No further generation is allowed. The outcome may have incurred a charge. Ask the operator to inspect the attempt; refreshing status never generates an image.</p>";
            if (attempt.HasImage) body += $"<figure><img src='/customer/crater/{productId}/image' alt='Private customer product artwork' style='max-width:100%'><figcaption>Your private Crater It preview</figcaption></figure>";
            if (attempt.CanGenerateClassic)
            {
                var tokens = antiforgery.GetAndStoreTokens(context);
                body += $"<form id='classic-crater' method='post' action='/customer/crater/{productId}/classic' onsubmit='this.querySelector(\"button\").disabled=true'>" +
                    $"<input type='hidden' name='{HtmlEncoder.Default.Encode(tokens.FormFieldName)}' value='{HtmlEncoder.Default.Encode(tokens.RequestToken!)}'>" +
                    "<button type='submit' class='btn btn-link'>Classic Crater</button><p>Uses the paid image API and replaces the public catalogue image for this product.</p></form>";
            }
            if (attempt.CanGenerate)
            {
                var tokens = antiforgery.GetAndStoreTokens(context);
                var action = attempt.HasImage ? $"/customer/crater/{productId}/attempt?repeat=true" : $"/customer/crater/{productId}/attempt";
                if (attempt.AttemptVersion is { } version)
                    action += (attempt.HasImage ? "&amp;" : "?") + $"version={version}";
                var label = attempt.HasImage ? "Crater It" : "Generate one image";
                body += $"<form method='post' action='{action}' onsubmit='this.querySelector(\"button\").disabled=true'>" +
                    $"<input type='hidden' name='{HtmlEncoder.Default.Encode(tokens.FormFieldName)}' value='{HtmlEncoder.Default.Encode(tokens.RequestToken!)}'>" +
                    $"<button type='submit' class='btn btn-link'>{label}</button></form>";
            }
            else body += "<button disabled>Generation unavailable</button>";
            if (attempt.State == "Completed" && !attempt.HasImage) body += "<p>Preview unavailable. Refresh status or preview; never generate again.</p>";
            return Html(productId, body + $"<p><a href='/customer/crater/{productId}'>Refresh status / retry preview</a></p>");
        }).RequireAuthorization(CustomerAuthentication.Policy);
        app.MapPost("/customer/crater/{productId:int:min(1)}/classic", async (int productId, HttpContext context, IAntiforgery antiforgery) =>
        {
            try { await antiforgery.ValidateRequestAsync(context); }
            catch (AntiforgeryValidationException) { return Results.BadRequest(); }
            if (!Enabled(context)) return Results.NotFound();
            using var response = await Call(context, HttpMethod.Post, "classic", productId);
            var outcome = response?.IsSuccessStatusCode == true ? "published" : "unavailable";
            return Results.LocalRedirect($"/customer/crater/{productId}?classic={outcome}");
        }).RequireAuthorization(CustomerAuthentication.Policy);
        app.MapPost("/customer/crater/{productId:int:min(1)}/attempt", async (int productId, HttpContext context, IAntiforgery antiforgery) =>
        {
            try { await antiforgery.ValidateRequestAsync(context); }
            catch (AntiforgeryValidationException) { return Results.BadRequest(); }
            if (!Enabled(context)) return Results.NotFound();
            var repeat = context.Request.Query["repeat"] == "true";
            var operation = repeat ? "attempt?repeat=true" : "attempt";
            if (int.TryParse(context.Request.Query["version"], out var version) && version >= 0)
                operation += (repeat ? "&" : "?") + $"version={version}";
            using var response = await Call(context, HttpMethod.Post, operation, productId);
            // Redirect only to status: never retry a POST, including when transport outcome is unknown.
            return Results.LocalRedirect($"/customer/crater/{productId}");
        }).RequireAuthorization(CustomerAuthentication.Policy);
        app.MapGet("/customer/crater/{productId:int:min(1)}/image", async (int productId, HttpContext context) =>
        {
            if (!Enabled(context)) return Results.NotFound();
            using var response = await Call(context, HttpMethod.Get, "image", productId);
            if (response is null || !response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType != "image/jpeg")
                return Results.NotFound();
            return Results.File(await response.Content.ReadAsByteArrayAsync(), "image/jpeg");
        }).RequireAuthorization(CustomerAuthentication.Policy);
    }

    internal static async Task<string> ReadAccountStatusAsync(HttpContext context, int productId = 139)
    {
        if (!Enabled(context)) return "Crater It generation is currently unavailable.";
        using var response = await Call(context, HttpMethod.Get, "attempt", productId);
        return (await CustomerCraterPreviewReader.ReadStatusAsync(response, context.RequestAborted, productId)).Message;
    }

    private static IResult Html(int productId, string body) => Results.Content($"""
        <!DOCTYPE html>
        <html lang="en" data-bs-theme="dark">
        <head>
            <meta charset="utf-8">
            <meta name="color-scheme" content="dark">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Crater It | FlyerMonkey</title>
            <script src="/_content/FlyerMonkey.Shared/theme.js"></script>
            <link rel="stylesheet" href="/_content/FlyerMonkey.Shared/lib/bootstrap/dist/css/bootstrap.min.css">
            <link rel="stylesheet" href="/_content/FlyerMonkey.Shared/app.css">
        </head>
        <body>
            <main class="container py-4">
                <div class="d-flex justify-content-end gap-1">
                    <flyermonkey-theme-picker></flyermonkey-theme-picker>
                    <a href="/customer/account?returnUrl=%2Fcustomer%2Fcrater%2F{productId}" class="fm-account-icon" aria-label="Account"><span aria-hidden="true"></span></a>
                </div>
                {body}
                <p><a href="/products/{productId}?crater=true">Back to product</a></p>
            </main>
        </body>
        </html>
        """, "text/html");
    private static bool Enabled(HttpContext context) =>
        context.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment() &&
        context.RequestServices.GetRequiredService<IConfiguration>().GetValue<bool>("CustomerCrater:Enabled") &&
        context.RequestServices.GetRequiredService<IConfiguration>().GetValue<bool>("CraterIt:Enabled");
    private static async Task<HttpResponseMessage?> Call(HttpContext context, HttpMethod method, string operation, int productId)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            var token = await context.RequestServices.GetRequiredService<ITokenAcquisition>()
                .GetAccessTokenForUserAsync([CustomerIdentity.Scope], authenticationScheme: CustomerAuthentication.Oidc, user: context.User);
            var http = context.RequestServices.GetRequiredService<IHttpClientFactory>().CreateClient("CustomerCraterApi");
            using var request = new HttpRequestMessage(method, $"api/customers/crater/{productId}/{operation}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return await http.SendAsync(request); // No browser cancellation can initiate a replacement request.
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or MicrosoftIdentityWebChallengeUserException)
        { return null; }
    }
}
