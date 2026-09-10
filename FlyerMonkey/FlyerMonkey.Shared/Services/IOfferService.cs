using FlyerMonkey.Shared.Model;

namespace FlyerMonkey.Shared.Services;

public interface IOfferService
{
    Task<List<OfferSummary>> GetOffersAsync(
        CancellationToken cancellationToken = default);
}