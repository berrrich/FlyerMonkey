using FlyerMonkey.Reviewer.Windows.Models;

namespace FlyerMonkey.Reviewer.Windows.Services;

public static class ProductValidationService
{
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