using System.Net.Http.Json;

namespace FlyerMonkey.Shared.Services;

public sealed class ImageGenerationApiService(HttpClient http)
{
    public bool IsEnabled => http.BaseAddress is { IsLoopback: true };

    public async Task<byte[]> GenerateAsync(string prompt, CancellationToken cancellationToken)
    {
        if (!IsEnabled)
            throw new InvalidOperationException("Generation is available only in local development.");

        using var request = new HttpRequestMessage(HttpMethod.Post, "api/images/generate")
        {
            Content = JsonContent.Create(new { Prompt = prompt })
        };
        // Prevent cross-origin browser forms from invoking the local paid endpoint.
        request.Headers.Add("X-CraterIt-Local", "1");
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (response.Content.Headers.ContentType?.MediaType != "image/jpeg" ||
            bytes.Length < 3 || bytes[0] != 0xff || bytes[1] != 0xd8 || bytes[2] != 0xff)
            throw new InvalidOperationException("The service did not return a JPEG image.");
        return bytes;
    }
}
