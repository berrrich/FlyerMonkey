namespace FlyerMonkey.Shared.Model;

public class OfferSummary
{
    public int OfferID { get; set; }

    public int ProductID { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public string? Brand { get; set; }

    public string? Variant { get; set; }

    public string? PackSizeText { get; set; }

    public string? Category { get; set; }

    public int RetailerID { get; set; }

    public string RetailerName { get; set; } = string.Empty;

    public string? StoreName { get; set; }

    public string? Suburb { get; set; }

    public decimal? AdvertisedPrice { get; set; }

    public decimal? RegularPrice { get; set; }

    public decimal? AdvertisedSaving { get; set; }

    public string? PromoText { get; set; }

    public string? UnitPriceText { get; set; }

    public DateTime? ValidFrom { get; set; }

    public DateTime? ValidTo { get; set; }
}