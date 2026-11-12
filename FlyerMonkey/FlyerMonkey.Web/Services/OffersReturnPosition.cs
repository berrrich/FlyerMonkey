namespace FlyerMonkey.Web.Services;

public sealed class OffersReturnPosition
{
    public int OfferId { get; set; }
    public string Retailer { get; set; } = "Woolworths";
    public double ScrollY { get; set; }
    public bool Pending { get; set; }
}
