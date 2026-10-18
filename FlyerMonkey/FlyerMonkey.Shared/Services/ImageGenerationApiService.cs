using System.Net.Http.Json;
using FlyerMonkey.Shared.Model;

namespace FlyerMonkey.Shared.Services;

public sealed class ImageGenerationApiService(HttpClient http)
{
    public bool IsEnabled => http.BaseAddress is { IsLoopback: true };

    public async Task<CraterArtwork> GenerateAsync(Guid requestId, int productId, string productName,
        CraterStyle preset, string prompt, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post, "api/images/generate",
            new { RequestId = requestId, ProductID = productId, ProductName = productName, Preset = preset, Prompt = prompt }, cancellationToken);
        return await response.Content.ReadFromJsonAsync<CraterArtwork>(cancellationToken) ??
            throw new InvalidOperationException("Missing saved artwork metadata.");
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? content, CancellationToken token)
    {
        if (!IsEnabled)
            throw new InvalidOperationException("Generation is available only in local development.");

        using var request = new HttpRequestMessage(method, path)
        {
            Content = content is null ? null : JsonContent.Create(content)
        };
        // Prevent cross-origin browser forms from invoking the local paid endpoint.
        request.Headers.Add("X-CraterIt-Local", "1");
        var response = await http.SendAsync(request, token);
        try { response.EnsureSuccessStatusCode(); return response; }
        catch { response.Dispose(); throw; }
    }

}
