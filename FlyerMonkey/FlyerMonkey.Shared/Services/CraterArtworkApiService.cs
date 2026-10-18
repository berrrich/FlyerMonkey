using System.Net.Http.Json;
using FlyerMonkey.Shared.Model;

namespace FlyerMonkey.Shared.Services;

/// <summary>Saved-artwork viewing only. This client has no generation or upload operation.</summary>
public sealed class CraterArtworkApiService(HttpClient http)
{
    public bool IsEnabled => http.BaseAddress is { IsLoopback: true } uri && uri.Scheme is "http" or "https";

    public async Task<List<CraterArtwork>> ListAsync(int productId, CancellationToken token)
    {
        if (productId <= 0) throw new ArgumentOutOfRangeException(nameof(productId));
        using var response = await GetAsync($"api/images/artwork?productId={productId}", token);
        return await response.Content.ReadFromJsonAsync<List<CraterArtwork>>(token) ?? [];
    }

    public async Task<byte[]> ReadAsync(Guid artworkId, CancellationToken token)
    {
        if (artworkId == Guid.Empty) throw new ArgumentException("Artwork ID must be nonempty.", nameof(artworkId));
        using var response = await GetAsync($"api/images/artwork/{artworkId:N}/image", token);
        var bytes = await response.Content.ReadAsByteArrayAsync(token);
        if (response.Content.Headers.ContentType?.MediaType != "image/jpeg" ||
            bytes.Length < 3 || bytes[0] != 255 || bytes[1] != 216 || bytes[2] != 255)
            throw new InvalidOperationException("The service did not return a JPEG image.");
        return bytes;
    }

    private async Task<HttpResponseMessage> GetAsync(string path, CancellationToken token)
    {
        if (!IsEnabled) throw new InvalidOperationException("Saved artwork is available only through an enabled local development API.");
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-CraterIt-Local", "1");
        var response = await http.SendAsync(request, token);
        try { response.EnsureSuccessStatusCode(); return response; }
        catch { response.Dispose(); throw; }
    }
}
