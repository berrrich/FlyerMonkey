using System.Net.Http.Json;
using FlyerMonkey.Shared.Model;

namespace FlyerMonkey.Shared.Services;

public class OfferApiService : IOfferService
{
    private readonly HttpClient _httpClient;

    public OfferApiService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<List<OfferSummary>> GetOffersAsync(
    CancellationToken cancellationToken = default)
    {
        const int maxAttempts = 3;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await _httpClient.GetFromJsonAsync<List<OfferSummary>>(
                    "api/offers",
                    cancellationToken)
                    ?? new List<OfferSummary>();
            }
            catch (HttpRequestException) when (attempt < maxAttempts)
            {
                await Task.Delay(attempt * 1000, cancellationToken);
            }
        }

        throw new HttpRequestException("Offers service unavailable.");
    }
}