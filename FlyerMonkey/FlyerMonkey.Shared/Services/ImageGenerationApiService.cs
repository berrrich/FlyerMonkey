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

    public async Task<List<CraterArtwork>> ListAsync(int productId, CancellationToken token)
    {
        using var response = await SendAsync(HttpMethod.Get, $"api/images/artwork?productId={productId}", null, token);
        return await response.Content.ReadFromJsonAsync<List<CraterArtwork>>(token) ?? [];
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

    public async Task<byte[]> ReadAsync(Guid artworkId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, $"api/images/artwork/{artworkId:N}/image", null, cancellationToken);
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (response.Content.Headers.ContentType?.MediaType != "image/jpeg" ||
            bytes.Length < 3 || bytes[0] != 0xff || bytes[1] != 0xd8 || bytes[2] != 0xff)
            throw new InvalidOperationException("The service did not return a JPEG image.");
        return bytes;
    }
}
