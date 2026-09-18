using FlyerMonkey.Shared.Model;

namespace FlyerMonkey.Shared.Services;

public interface IOfferRepository
{
    Task<List<OfferSummary>> GetOfferSummariesAsync(
        CancellationToken cancellationToken = default);

    Task<List<OfferSummary>> GetCurrentOfferSummariesAsync(
        CancellationToken cancellationToken = default);

    Task<List<OfferSummary>> GetPreviousOfferSummariesAsync(
        CancellationToken cancellationToken = default);
}