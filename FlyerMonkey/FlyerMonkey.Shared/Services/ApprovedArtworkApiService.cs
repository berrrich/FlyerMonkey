using System.Net;
using Microsoft.Extensions.DependencyInjection;

namespace FlyerMonkey.Shared.Services;

public sealed class ApprovedArtworkApiService(HttpClient http)
{
    public bool IsEnabled => http.BaseAddress is { Scheme: "https", IsLoopback: false };

    public async Task<byte[]?> ReadAsync(int productId, CancellationToken token)
    {
        if (!IsEnabled) return null;
        if (productId <= 0) throw new ArgumentOutOfRangeException(nameof(productId));
        // ResponseHeadersRead ends HttpClient's timeout at the headers; bound the body too.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        token = timeout.Token;
        using var response = await http.GetAsync($"api/products/{productId}/approved-artwork/image", HttpCompletionOption.ResponseHeadersRead, token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        const int limit = 2 * 1024 * 1024;
        if (response.Content.Headers.ContentType?.MediaType != "image/jpeg" || response.Content.Headers.ContentLength > limit)
            throw new InvalidDataException("Invalid approved artwork response.");
        using var stream = await response.Content.ReadAsStreamAsync(token);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer, token)) != 0)
        {
            if (output.Length + count > limit) throw new InvalidDataException("Approved artwork too large.");
            output.Write(buffer, 0, count);
        }
        var bytes = output.ToArray();
        if (bytes.Length < 3 || bytes[0] != 255 || bytes[1] != 216 || bytes[2] != 255)
            throw new InvalidDataException("Approved artwork is not JPEG.");
        return bytes;
    }
}

public static class ApprovedArtworkClientRegistration
{
    public static IServiceCollection AddApprovedArtworkClient(this IServiceCollection services, bool enabled = false, string? apiBaseUrl = null)
    {
        Uri? address = null;
        if (enabled)
        {
            if (!Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.IsLoopback ||
                uri.AbsolutePath != "/" || uri.Query != "" || uri.Fragment != "" || uri.UserInfo != "")
                throw new InvalidOperationException("Approved artwork requires an explicit HTTPS API root.");
            address = uri;
        }
        services.AddHttpClient<ApprovedArtworkApiService>(http =>
        {
            http.BaseAddress = address;
            http.Timeout = TimeSpan.FromSeconds(30);
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        return services;
    }
}
