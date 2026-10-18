using Microsoft.Extensions.DependencyInjection;

namespace FlyerMonkey.Shared.Services;

public static class CraterClientRegistration
{
    public static IServiceCollection AddWebCraterClients(this IServiceCollection services,
        bool development, bool enabled, string? apiBaseUrl)
    {
        Uri? address = null;
        if (development && enabled && Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out var uri) &&
            uri.IsLoopback && uri.Scheme is "http" or "https") address = uri;
        services.AddHttpClient<ImageGenerationApiService>(http =>
        {
            http.BaseAddress = address;
            http.Timeout = TimeSpan.FromMinutes(3);
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        AddGallery(services, address);
        return services;
    }

    public static IServiceCollection AddMauiCraterClients(this IServiceCollection services, bool androidDebug)
    {
        // Never assign a generation address in MAUI, including Debug builds with gallery access.
        services.AddHttpClient<ImageGenerationApiService>();
        // adb reverse tcp:5145 tcp:5145 forwards device localhost to the loopback-bound host API.
        AddGallery(services, androidDebug ? new Uri("http://localhost:5145/") : null);
        return services;
    }

    private static void AddGallery(IServiceCollection services, Uri? address) =>
        services.AddHttpClient<CraterArtworkApiService>(http =>
        {
            http.BaseAddress = address;
            http.Timeout = TimeSpan.FromSeconds(30);
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
}
