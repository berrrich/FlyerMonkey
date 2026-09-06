namespace FlyerMonkey.Reviewer.Windows.Models;

public class ExtractedProduct
{
    public string ProductName { get; set; } = string.Empty;

    public string? Brand { get; set; }

    public string? Variant { get; set; }

    public string? PackSizeText { get; set; }

    public string? Category { get; set; }

    public string? Barcode { get; set; }
}