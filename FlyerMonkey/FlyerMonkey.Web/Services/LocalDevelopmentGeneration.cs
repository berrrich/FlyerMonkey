using System.Net.Http.Json;
using FlyerMonkey.Shared.Model;
using FlyerMonkey.Shared.Services;

namespace FlyerMonkey.Web.Services;

public sealed class LocalDevelopmentGeneration(IServiceProvider services, IConfiguration configuration,
    IWebHostEnvironment environment) : ILocalDevelopmentGeneration
{
    public bool Enabled => environment.IsDevelopment() &&
        configuration.GetValue<bool>("Authentication:Enabled") &&
        configuration.GetValue<bool>("CraterIt:Enabled") &&
        configuration.GetValue<bool>("CraterIt:LocalDevelopmentGenerationEnabled") &&
        Uri.TryCreate(configuration["Authentication:ApiBaseUrl"], UriKind.Absolute, out var uri) &&
        uri.IsLoopback && uri.Scheme == "https";

    public async Task<string> GenerateAsync(Guid requestId, Product product, string creativePrompt)
    {
        if (!Enabled) throw new InvalidOperationException("Local development generation is disabled.");
        if (creativePrompt.Length > 500) throw new ArgumentException("Use a prompt of at most 500 characters.");
        var token = await services.GetRequiredService<IOperatorTokenProvider>().GetAccessTokenAsync();
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = OperatorAuthentication.RequiredApiBaseUri(configuration, environment),
            Timeout = TimeSpan.FromMinutes(3)
        };
        http.DefaultRequestHeaders.Authorization = new("Bearer", token);
        http.DefaultRequestHeaders.Add("X-CraterIt-Local", "1");
        var subject = $"{product.Brand} {product.Name}, {product.Variant}, {product.PackSizeText}";
        subject = subject[..Math.Min(subject.Length, 900)];
        var prompt = $"Create playful imaginative product-inspired artwork featuring {subject}. " +
            (string.IsNullOrWhiteSpace(creativePrompt) ? "Send this product crashing through a supermarket aboard a comet." : creativePrompt.Trim());
        using var response = await http.PostAsJsonAsync("api/images/local-development/generate", new
        {
            RequestId = requestId, ProductID = product.ID, ProductName = subject,
            Preset = CraterStyle.CometCrush, Prompt = prompt
        });
        if (!response.IsSuccessStatusCode)
            throw new LocalDevelopmentGenerationException(response.StatusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized => "Operator sign-in expired. Sign in explicitly again.",
                System.Net.HttpStatusCode.Forbidden => "Local development generation is disabled or this operator/client is not allowlisted.",
                System.Net.HttpStatusCode.NotFound => "The local API generation endpoint is unavailable. Check Development mode and local flags.",
                System.Net.HttpStatusCode.TooManyRequests => "Another generation is already running. No new generation was started for this request.",
                System.Net.HttpStatusCode.ServiceUnavailable => "Artwork could not be saved or recovered. Check private storage.",
                _ => "The image service failed. Check the local API logs for provider or configuration errors."
            });
        var artwork = await response.Content.ReadFromJsonAsync<CraterArtwork>() ?? throw new InvalidOperationException("Missing artwork metadata.");
        if (artwork.ProductID != product.ID) throw new InvalidOperationException("Unexpected artwork product.");
        var bytes = await new CraterArtworkApiService(http).ReadAsync(artwork.ArtworkId, default);
        return "data:image/jpeg;base64," + Convert.ToBase64String(bytes);
    }
}
