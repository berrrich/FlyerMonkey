namespace FlyerMonkey.Reviewer.Windows.Models;

public sealed class SavedExtraction
{
    public long Id { get; set; }
    public string Retailer { get; set; } = "";
    public string FlyerFileName { get; set; } = "";
    public string PageFileName { get; set; } = "";
    public int PageNumber { get; set; }
    public int ProductCount { get; set; }
    public string SavedUtc { get; set; } = "";
}