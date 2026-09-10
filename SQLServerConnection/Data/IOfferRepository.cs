using FlyerMonkey.Shared.Model;

namespace SQLServerConnection.Data;

public interface IOfferRepository
{
    Task<int> AddOfferAsync(
        Offer offer,
        CancellationToken cancellationToken = default);

    Task<List<Offer>> GetOffersAsync(
        CancellationToken cancellationToken = default);

    Task<List<OfferSummary>> GetOfferSummariesAsync(
    CancellationToken cancellationToken = default);
}