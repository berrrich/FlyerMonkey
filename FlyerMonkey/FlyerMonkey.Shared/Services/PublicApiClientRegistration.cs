using Microsoft.Extensions.DependencyInjection;

namespace FlyerMonkey.Shared.Services;

/// <summary>Published read-only API routing. Local Crater clients remain independently gated.</summary>
public static class PublicApiClientRegistration
{
    public const string ApiBaseUrl = "https://flyermonkeyapi-g7htasdacxfzgbcd.australiaeast-01.azurewebsites.net/";

    public static string ProductImageUrl(int productId) => $"{ApiBaseUrl}api/products/{productId}/image";

    public static IServiceCollection AddMauiPublicApiClients(this IServiceCollection services, bool android)
    {
        static void Configure(HttpClient http)
        {
            http.BaseAddress = new Uri(ApiBaseUrl);
            http.Timeout = TimeSpan.FromSeconds(30);
        }

        services.AddHttpClient<IProductService, ProductApiService>(Configure)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddHttpClient<IOfferService, OfferApiService>(Configure)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddApprovedArtworkClient(enabled: android, apiBaseUrl: ApiBaseUrl);
        return services;
    }
}
