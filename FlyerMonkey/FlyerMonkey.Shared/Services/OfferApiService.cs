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
        return await _httpClient.GetFromJsonAsync<List<OfferSummary>>(
                   "api/offers",
                   cancellationToken)
               ?? new List<OfferSummary>();
    }
}