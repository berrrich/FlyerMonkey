using FlyerMonkey.Shared.Model;
using FlyerMonkey.Shared.Services;

namespace FlyerMonkey.Shared.UI.Offers;

public class OffersUI
{
    private readonly IOfferService _offerService;

    public List<OfferSummary> Offers { get; private set; } = new();

    public bool IsLoading { get; private set; }

    public string? LoadError { get; private set; }

    public OffersUI(IOfferService offerService)
    {
        _offerService = offerService;
    }

    public async Task LoadOffersAsync()
    {
        IsLoading = true;
        LoadError = null;

        try
        {
            Offers = await _offerService.GetOffersAsync();
        }
        catch
        {
            LoadError =
                "Sorry for the inconvenience – we couldn't load the offers. Please try again.";
        }
        finally
        {
            IsLoading = false;
        }
    }
}