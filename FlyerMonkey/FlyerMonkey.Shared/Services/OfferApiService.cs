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

    public Task<List<OfferSummary>> GetOffersAsync(
        CancellationToken cancellationToken = default)
    {
        return GetOffersInternalAsync(
            "api/offers",
            cancellationToken);
    }

    public Task<List<OfferSummary>> GetCurrentOffersAsync(
        CancellationToken cancellationToken = default)
    {
        return GetOffersInternalAsync(
            "api/offers/current",
            cancellationToken);
    }

    public Task<List<OfferSummary>> GetPreviousOffersAsync(
        CancellationToken cancellationToken = default)
    {
        return GetOffersInternalAsync(
            "api/offers/previous",
            cancellationToken);
    }

    private async Task<List<OfferSummary>> GetOffersInternalAsync(
        string endpoint,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await _httpClient.GetFromJsonAsync<List<OfferSummary>>(
                    endpoint,
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