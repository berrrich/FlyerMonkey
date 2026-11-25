using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using FlyerMonkey.Shared.Model;
using FlyerMonkey.Shared.Services;
using FlyerMonkey.Web.Components.Pages;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Identity.Web;

namespace FlyerMonkey.Web.Services;

public static class CustomerCrater
{
    public static void MapCustomerCrater(this WebApplication app)
    {
        app.MapGet("/customer/crater/{productId:int:min(1)}", async (int productId, HttpContext context, IAntiforgery antiforgery) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var body = "";
            if (!Enabled(context)) return Html(productId, "<p>Not eligible: customer generation is disabled.</p>", showProduct: false);
            var classicPreview = await ReadClassicPreview(context, productId);
            using var response = await Call(context, HttpMethod.Get, "attempt", productId);
            if (response is null || !response.IsSuccessStatusCode)
                return Html(productId, body + classicPreview + "<p>Not eligible or status unavailable. Generation is disabled. Check sign-in, eligibility and local configuration.</p>");
            CustomerCraterAttempt? attempt;
            try { attempt = await response.Content.ReadFromJsonAsync<CustomerCraterAttempt>(); }
            catch (Exception) { return Html(productId, body + classicPreview + "<p>Status unavailable. Do not start another generation.</p>"); }
            if (attempt is null) return Html(productId, body + classicPreview + "<p>Status unavailable.</p>");
            var status = attempt.State switch
            {
                "DailyLimitReached" => "The shared daily generation limit has been reached. Try again after midnight UTC.",
                "AccountLimitReached" => "Your account has used its available Crater It attempts.",
                "Ready" => "Regular Crater It is ready to generate.",
                _ => attempt.State
            };
            body += "<p role='status'>" + HtmlEncoder.Default.Encode(status) + "</p>";
            var classicUnconfirmed = context.Request.Query["classic"] == "unavailable";
            if (classicUnconfirmed)
                body += "<p role='status'>The latest Classic Crater attempt could not be confirmed. Review the catalogue image before trying again.</p>";
            if (attempt.State == "Ready" && !attempt.CanGenerate) body += "<p>Not eligible: server permission is required.</p>";
            if (attempt.State is "Failed" or "Unknown") body += "<p>No further generation is allowed. The outcome may have incurred a charge. Ask the operator to inspect the attempt; refreshing status never generates an image.</p>";
            if (attempt.HasImage) body += $"<figure><img src='/customer/crater/{productId}/image' alt='Private customer product artwork' style='max-width:100%'><figcaption>Your private Crater It preview</figcaption></figure>";
            if (attempt.CanGenerateClassic)
            {
                var tokens = antiforgery.GetAndStoreTokens(context);
                body += $"<form id='classic-crater' method='post' action='/customer/crater/{productId}/classic' onsubmit='const button=this.querySelector(\"button\"); if(button.disabled) return false; button.disabled=true; document.documentElement.classList.add(\"classic-crater-pending\"); this.querySelector(\".classic-crater-status\").textContent=\"Classic Crater It in progress…\"'>" +
                    $"<input type='hidden' name='{HtmlEncoder.Default.Encode(tokens.FormFieldName)}' value='{HtmlEncoder.Default.Encode(tokens.RequestToken!)}'>" +
                    "<div class='d-flex flex-wrap align-items-center gap-3 mb-2'>" +
                    "<button type='submit' class='btn btn-link'>Classic Crater</button>" + classicPreview +
                    "</div><p>Uses the paid image API and replaces the public catalogue image for this product.</p><div class='classic-crater-status' role='status'></div></form>";
            }
            else body += classicPreview;
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
            if (outcome == "published")
                context.RequestServices.GetService<ClassicCraterImages>()?.Refresh();
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

    private static async Task<string> ReadClassicPreview(HttpContext context, int productId)
    {
        if (context.RequestServices.GetService<ClassicCraterImages>() is not { } images) return "";
        var url = images.Url(productId);
        using var http = context.RequestServices.GetRequiredService<IHttpClientFactory>().CreateClient("CustomerCraterApi");
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
            if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentType?.MediaType != "image/jpeg")
                return "";
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException) { return ""; }

        var imageUrl = HtmlEncoder.Default.Encode(url);
        return $"""
            <div class="d-flex align-items-center gap-2">
                <a href="{imageUrl}" aria-label="Open full-size Classic Crater image" aria-haspopup="dialog"
                   onclick="this.nextElementSibling.showModal(); return false;">
                    <img src="{imageUrl}" alt="Published Classic Crater artwork" class="rounded"
                         width="64" height="64" style="object-fit:contain">
                </a>
                <dialog class="rounded border bg-body text-body p-3" aria-label="Classic Crater image preview">
                    <div class="d-flex justify-content-end mb-2">
                        <button type="button" class="btn-close p-3" aria-label="Close image preview" autofocus
                                onclick="this.closest('dialog').close()"></button>
                    </div>
                    <img src="{imageUrl}" alt="Published Classic Crater artwork" class="d-block img-fluid"
                         style="max-height:75dvh;object-fit:contain">
                </dialog>
                <p class="mb-0" role="status">Classic Crater complete</p>
            </div>
            """;
    }

    internal static async Task<string> ReadAccountStatusAsync(HttpContext context, int productId = 139)
    {
        if (!Enabled(context)) return "Crater It generation is currently unavailable.";
        using var response = await Call(context, HttpMethod.Get, "attempt", productId);
        return (await CustomerCraterPreviewReader.ReadStatusAsync(response, context.RequestAborted, productId)).Message;
    }

    private static RazorComponentResult<CustomerCraterDocument> Html(int productId, string body, bool showProduct = true) =>
        new(new { ProductId = productId, CraterBody = body, ShowProduct = showProduct }) { PreventStreamingRendering = true };

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
