using FlyerMonkey.Reviewer.Windows.Models;

namespace FlyerMonkey.Reviewer.Windows.Services;

public class ProductImageService
{
    public string BuildImagePrompt(ExtractedProduct product)
    {
        return
            $"Create a clean product image for " +
            $"{product.Brand} {product.ProductName}, " +
            $"{product.Variant}, {product.PackSizeText}.";
    }
}