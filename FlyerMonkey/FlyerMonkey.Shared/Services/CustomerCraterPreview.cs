using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FlyerMonkey.Shared.Model;

namespace FlyerMonkey.Shared.Services;

public sealed record CustomerCraterPreview(string Message, string? Image = null, bool CanGenerate = false,
    int? ProductId = null, bool CanGenerateClassic = false, bool ClassicPublished = false, int? AttemptVersion = null);

public static class CustomerCraterPreviewReader
{
    public static async Task<CustomerCraterPreview> GenerateAsync(HttpClient http, string token, bool repeat = false,
        int productId = 139, bool classic = false, int? expectedVersion = null)
    {
        var action = classic ? "classic" : repeat ? "attempt?repeat=true" : "attempt";
        if (!classic && expectedVersion is { } version)
            action += (repeat ? "&" : "?") + $"version={version}";
        using var request = Request(action, token, productId);
        request.Method = HttpMethod.Post;
        using var response = await http.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.Conflict && !classic)
        {
            var current = await ReadAsync(http, token, productId);
            return current with { Message = "The attempt status changed. Check the latest preview before trying again." };
        }
        if (!response.IsSuccessStatusCode) return Error(response.StatusCode);
        var preview = await ReadAsync(http, token, productId);
        return classic ? preview with { Message = "Classic Crater artwork is now the default catalogue image.",
            ClassicPublished = true } : preview;
    }

    public static async Task<CustomerCraterPreview> ReadAsync(HttpClient http, string token, int productId = 139)
    {
        using var statusRequest = Request("attempt", token, productId);
        using var statusResponse = await http.SendAsync(statusRequest);
        if (!statusResponse.IsSuccessStatusCode) return Error(statusResponse.StatusCode);
        var attempt = await statusResponse.Content.ReadFromJsonAsync<CustomerCraterAttempt>();
        if (attempt is null) throw new InvalidDataException("Missing attempt status.");
        var preview = await ReadPreviewAsync(http, token, productId, attempt);
        return preview with
        {
            ProductId = productId,
            CanGenerateClassic = attempt.CanGenerateClassic,
            AttemptVersion = attempt.AttemptVersion
        };
    }

    private static async Task<CustomerCraterPreview> ReadPreviewAsync(HttpClient http, string token, int productId,
        CustomerCraterAttempt attempt)
    {
        if (attempt.State is "Pending" or "Generating") return new("Your artwork is pending. Refresh account status to check again.");
        if (attempt.State == "DailyLimitReached") return new("The shared daily generation limit has been reached. Try again after midnight UTC.");
        if (attempt.State == "AccountLimitReached") return new("Your account has used its available Crater It attempts.");
        if (attempt.State == "Ready") return attempt.CanGenerate
            ? new("Ready to generate one private image.", CanGenerate: true)
            : new("Generation is unavailable for your customer account.");
        if (attempt.State is "Failed" or "Unknown" && !attempt.HasImage)
            return new("The existing attempt has no completed preview available.");
        if (attempt.State is not ("Completed" or "Failed" or "Unknown")) throw new InvalidDataException("Invalid attempt status.");
        if (!attempt.HasImage) return new("The completed artwork image is unavailable.");
        using var imageRequest = Request("image", token, productId);
        using var imageResponse = await http.SendAsync(imageRequest);
        if (!imageResponse.IsSuccessStatusCode) return Error(imageResponse.StatusCode);
        var bytes = await imageResponse.Content.ReadAsByteArrayAsync();
        if (imageResponse.Content.Headers.ContentType?.MediaType != "image/jpeg" ||
            bytes.Length < 3 || bytes.Length > 10 * 1024 * 1024 ||
            bytes[0] != 255 || bytes[1] != 216 || bytes[2] != 255)
            throw new InvalidDataException("Invalid private JPEG.");
        var message = attempt.State == "Completed" ? "Completed artwork" : "Showing your previous artwork.";
        return new(message, "data:image/jpeg;base64," + Convert.ToBase64String(bytes), attempt.CanGenerate);
    }

    public static async Task<CustomerCraterPreview> ReadStatusAsync(HttpClient http, string token, int productId = 139)
    {
        using var request = Request("attempt", token, productId);
        using var response = await http.SendAsync(request);
        return await ReadStatusAsync(response, productId: productId);
    }

    public static async Task<CustomerCraterPreview> ReadStatusAsync(HttpResponseMessage? response,
        CancellationToken cancellationToken = default, int productId = 139)
    {
        if (response is null) return new("Crater It status is temporarily unavailable.");
        if (!response.IsSuccessStatusCode) return Error(response.StatusCode);
        try
        {
            var attempt = await response.Content.ReadFromJsonAsync<CustomerCraterAttempt>(cancellationToken);
            return attempt?.State switch
            {
                "Ready" when attempt.CanGenerate => new($"Your account is eligible for a Crater It attempt on Product {productId}."),
                "Ready" => new("Generation is unavailable for your customer account."),
                "DailyLimitReached" => new("The shared daily generation limit has been reached. Try again after midnight UTC."),
                "AccountLimitReached" => new("Your account has used its available Crater It attempts."),
                "Pending" or "Generating" => new("Your Crater It artwork is pending."),
                "Completed" => new("Your Crater It attempt is complete."),
                "Failed" or "Unknown" => new("Your Crater It attempt has no completed preview."),
                _ => new("Crater It status is temporarily unavailable.")
            };
        }
        catch (System.Text.Json.JsonException) { return new("Crater It status is temporarily unavailable."); }
        catch (HttpRequestException) { return new("Crater It status is temporarily unavailable."); }
    }

    private static HttpRequestMessage Request(string action, string token, int productId = 139)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"api/customers/crater/{productId}/{action}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static CustomerCraterPreview Error(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => new("Your customer session was rejected. Sign out and sign in again."),
        HttpStatusCode.Forbidden => new("Access to this private artwork was denied."),
        HttpStatusCode.NotFound => new("Private artwork or the customer preview service is unavailable."),
        HttpStatusCode.TooManyRequests => new("The shared daily generation limit has been reached."),
        HttpStatusCode.Conflict => new("An artwork request is already in progress."),
        _ => new("The private preview service is unavailable. Refresh account status later.")
    };
}
