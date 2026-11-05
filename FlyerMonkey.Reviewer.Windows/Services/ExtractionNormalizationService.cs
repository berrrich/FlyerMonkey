using FlyerMonkey.Reviewer.Windows.Models;
using System.Text.RegularExpressions;

namespace FlyerMonkey.Reviewer.Windows.Services;

public static class ExtractionNormalizationService
{
    // Only called for fresh extractor output. Saving/reloading drafts never calls this method.
    public static void NormalizeFresh(ExtractedProduct product)
    {
        if (product.PackSizeManuallyCorrected) return;
        var name = product.ProductName ?? "";
        var categories = string.Join(" ", new[] { product.Category, product.PrimaryCategory }.Concat(product.Categories ?? new()));
        var meatName = Regex.IsMatch(name, @"\b(ham|beef|pork|lamb|veal|chicken|turkey|duck|bacon|salami|prosciutto|steak|boneless leg|sausages?)\b", RegexOptions.IgnoreCase);
        var meat = meatName || Regex.IsMatch(categories, @"\b(meat|poultry)\b", RegexOptions.IgnoreCase);
        if (!meat || Regex.IsMatch(name, @"\b(canned|tinned|soup|pie|pizza|stock|chips)\b", RegexOptions.IgnoreCase)) return;
        var size = product.PackSizeText ?? "";
        var pattern = @"(?<![\d$])\b\d+(?:\.\d+)?\s*(?:kg|g|ml|l|pack|pk|pieces?|pcs)\b";
        var candidate = Regex.Match(size, pattern, RegexOptions.IgnoreCase);
        var evidence = product.PackSizeEvidence ?? name;
        // A unit-price statement is not package-size evidence, even if extraction copied it into packSizeText.
        bool UnitPrice(string text) => Regex.IsMatch(text, @"\$|/\s*(?:kg|g|100\s*g)|\bper\s*(?:kg|kilogram|100\s*g)\b", RegexOptions.IgnoreCase);
        var printed = Regex.Matches(evidence, pattern, RegexOptions.IgnoreCase).Cast<Match>()
            .Any(m => !Regex.IsMatch(evidence[..m.Index], @"(?:\$\s*|/\s*|\bper\s*)$", RegexOptions.IgnoreCase)
                && !Regex.IsMatch(evidence[(m.Index + m.Length)..], @"^\s*/", RegexOptions.IgnoreCase)
                && Regex.Replace(m.Value, @"\s+", "").Equals(Regex.Replace(candidate.Value, @"\s+", ""), StringComparison.OrdinalIgnoreCase));
        if (!candidate.Success || UnitPrice(size) || !printed) product.PackSizeText = "";
        else product.PackSizeText = Regex.Replace(evidence, @"\s+", "").Contains(Regex.Replace(size, @"\s+", ""), StringComparison.OrdinalIgnoreCase) ? size.Trim() : candidate.Value.Trim();
    }
}
