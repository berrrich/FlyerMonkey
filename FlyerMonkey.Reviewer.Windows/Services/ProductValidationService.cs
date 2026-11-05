using FlyerMonkey.Reviewer.Windows.Models;

namespace FlyerMonkey.Reviewer.Windows.Services;

public static class ProductValidationService
{
public static string? FieldError(ExtractedProduct p, string field) => field switch
    {
        nameof(p.ProductName) when string.IsNullOrWhiteSpace(p.ProductName) => "Product name is required.",
        nameof(p.Price) when string.IsNullOrWhiteSpace(p.Price) => "Advertised price is required.",
        nameof(p.Price) when ParseMoney(p.Price) is not decimal amount || amount < 0 => "Advertised price must be a valid non-negative amount.",
        nameof(p.OfferQuantity) when p.OfferQuantity is <= 0 => "Offer quantity must be positive when supplied.",
        _ => null
    };
    public static List<string> ValidateBatch(IEnumerable<ExtractedProduct> products)
    {
        var errors = new List<string>();
        int index = 0;
        foreach (var p in products) {
            index++;
            foreach (var field in new[] { nameof(p.ProductName), nameof(p.Price), nameof(p.OfferQuantity) })
                if (FieldError(p, field) is string error) errors.Add($"Product {index} ({p.ProductName}): {error}");
        }
        if (index == 0) errors.Add("At least one product is required.");
        return errors;
    }
    public static void SanitizeProduct(ExtractedProduct product)
    {
        if (!string.IsNullOrWhiteSpace(product.RegularPrice) &&
            (product.RegularPrice.Contains("per kg", StringComparison.OrdinalIgnoreCase) ||
             product.RegularPrice.Contains("per litre", StringComparison.OrdinalIgnoreCase) ||
             product.RegularPrice.Contains("per 100g", StringComparison.OrdinalIgnoreCase) ||
             product.RegularPrice.Contains("per 100ml", StringComparison.OrdinalIgnoreCase)))
        {
            product.RegularPrice = "";
        }
        var price = ParseMoney(product.Price);
        var saving = ParseMoney(product.Saving);

        if (price.HasValue && saving.HasValue)
        {
            product.CalculatedRegularPrice =
                $"${price.Value + saving.Value:0.00}";
        }
        else
        {
            product.CalculatedRegularPrice = null;
        }
    }
    private static decimal? ParseMoney(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var cleaned = value
            .Replace("$", "")
            .Replace(",", "")
            .Replace("SAVE", "", StringComparison.OrdinalIgnoreCase)
            .Trim();

        return decimal.TryParse(
            cleaned,
            System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture,
            out var amount)
                ? amount
                : null;
    }
}