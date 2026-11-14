using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FlyerMonkey.Shared.Model;

namespace FlyerMonkey.Shared.Services;

public sealed record CustomerCraterPreview(string Message, string? Image = null, bool CanGenerate = false);

public static class CustomerCraterPreviewReader
{
    public static async Task<CustomerCraterPreview> GenerateAsync(HttpClient http, string token)
    {
        using var request = Request("attempt", token);
        request.Method = HttpMethod.Post;
        using var response = await http.SendAsync(request);
        if (!response.IsSuccessStatusCode) return Error(response.StatusCode);
        return await ReadAsync(http, token);
    }

    public static async Task<CustomerCraterPreview> ReadAsync(HttpClient http, string token)
    {
        using var statusRequest = Request("attempt", token);
        using var statusResponse = await http.SendAsync(statusRequest);
        if (!statusResponse.IsSuccessStatusCode) return Error(statusResponse.StatusCode);
        var attempt = await statusResponse.Content.ReadFromJsonAsync<CustomerCraterAttempt>();
        if (attempt is null) throw new InvalidDataException("Missing attempt status.");
        if (attempt.State is "Pending" or "Generating") return new("Your artwork is pending. Refresh account status to check again.");
        if (attempt.State == "Ready") return attempt.CanGenerate
            ? new("Ready to generate one private image.", CanGenerate: true)
            : new("Generation is unavailable for your customer account.");
        if (attempt.State is "Failed" or "Unknown") return new("The existing attempt has no completed preview available.");
        if (attempt.State != "Completed") throw new InvalidDataException("Invalid attempt status.");
        if (!attempt.HasImage) return new("The completed artwork image is unavailable.");
        using var imageRequest = Request("image", token);
        using var imageResponse = await http.SendAsync(imageRequest);
        if (!imageResponse.IsSuccessStatusCode) return Error(imageResponse.StatusCode);
        var bytes = await imageResponse.Content.ReadAsByteArrayAsync();
        if (imageResponse.Content.Headers.ContentType?.MediaType != "image/jpeg" ||
            bytes.Length < 3 || bytes.Length > 10 * 1024 * 1024 ||
            bytes[0] != 255 || bytes[1] != 216 || bytes[2] != 255)
            throw new InvalidDataException("Invalid private JPEG.");
        return new("Completed artwork", "data:image/jpeg;base64," + Convert.ToBase64String(bytes));
    }

    private static HttpRequestMessage Request(string action, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"api/customers/crater/139/{action}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static CustomerCraterPreview Error(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => new("Your customer session was rejected. Sign out and sign in again."),
        HttpStatusCode.Forbidden => new("Access to this private artwork was denied."),
        HttpStatusCode.NotFound => new("Private artwork or the customer preview service is unavailable."),
        _ => new("The private preview service is unavailable. Refresh account status later.")
    };
}
