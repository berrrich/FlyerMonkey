using FlyerMonkey.Reviewer.Windows.Models;
using System.Text.RegularExpressions;

namespace FlyerMonkey.Reviewer.Windows.Services;

public class ProductImageService
{
    public string BuildImagePrompt(ExtractedProduct product, string? instructions = null)
    {
        var produce = IsFreshProduce(product);
        var packaging = Regex.IsMatch(product.PackSizeText ?? "", @"\b(bag|punnet|tray|tub|box|net|carton|packet)s?\b", RegexOptions.IgnoreCase);
        var prompt = produce
            ? $"Create a clean product image of the actual fresh produce: {product.ProductName}. " +
              (string.IsNullOrWhiteSpace(product.Variant) ? "" : $"Produce variety: {product.Variant}. ") +
              "Show the named fruit or vegetable recognisably in its natural form, with its characteristic shape, colour and skin. " +
              (packaging ? $"Use only the packaging explicitly described in the product data: {product.PackSizeText}. " : "Show loose produce, without packaging. ")
            : $"Create a clean product image for {product.Brand} {product.ProductName}, {product.Variant}, {product.PackSizeText}.";
        if (!string.IsNullOrWhiteSpace(instructions))
            prompt += "\n\nAdditional operator instructions:\n" + instructions.Trim();
        if (produce)
            prompt += "\n\nFresh produce constraints: preserve the actual named produce and its natural form. " +
                "Do not invent packaging, branding, weights, stickers, labels or printed text. " +
                (packaging ? "Do not add packaging beyond what the product data explicitly describes." : "Keep the produce loose even if other instructions suggest packaging.");
        else
            prompt += "\n\nProduct accuracy: use only supplied identity and packaging facts; do not invent branding, weights or labels. If the named product is fresh fruit or vegetables, show the exact named produce in its natural form. For loose produce, omit all packaging, branding, weights and labels.";
        return prompt;
    }

    private static bool IsFreshProduce(ExtractedProduct product)
    {
        var name = product.ProductName ?? "";
        if (Regex.IsMatch(name, @"\b(juices?|drinks?|jams?|sauces?|oils?|chips|crisps|frozen|canned|tinned|dried|powder|yoghurt|yogurt|cakes?|pies?|soups?|chocolate|ice cream|vinegar|cider|cordial|concentrate|puree|paste|spread|pickled|roasted|cooked|battered|gems)\b", RegexOptions.IgnoreCase))
            return false;
        var categories = string.Join(" ", new[] { product.Category, product.PrimaryCategory }.Concat(product.Categories ?? new()));
        if (Regex.IsMatch(categories, @"\b(meat|poultry|seafood|bakery|beverages?|dairy|frozen|confectionery)\b", RegexOptions.IgnoreCase)) return false;
        return Regex.IsMatch(categories, @"\b(produce|fruit|vegetables?)\b", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(name, @"\b(apples?|pears?|bananas?|oranges?|mandarins?|lemons?|limes?|grapefruits?|mango(?:es)?|avocados?|tomato(?:es)?|potato(?:es)?|onions?|garlic|ginger|carrots?|broccoli|cauliflower|lettuce|spinach|capsicums?|cucumbers?|zucchini|eggplants?|pumpkins?|asparagus|mushrooms?|celery|cabbage|grapes?|strawberr(?:y|ies)|blueberr(?:y|ies)|raspberr(?:y|ies)|watermelons?|rockmelons?|pineapples?|kiwifruit|peaches|peach|nectarines?|plums?|cherries|cherry|beetroot|sweetcorn|corn cobs?)\b", RegexOptions.IgnoreCase);
    }
}
