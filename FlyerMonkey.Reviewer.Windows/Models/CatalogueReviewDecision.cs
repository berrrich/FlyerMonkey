using FlyerMonkey.Shared.Model;
using SQLServerConnection.Data;

namespace FlyerMonkey.Reviewer.Windows.Models;

public sealed record CatalogueReviewDecision(ProductReviewChoice Choice, CatalogueCandidate? Target,
    string Name, string? Brand, string? Variant, string? PackSizeText, string? Barcode)
{
    public static CatalogueReviewDecision For(ExtractedProduct product, ProductReviewChoice choice,
        CatalogueCandidate? target = null) =>
        new(choice, target, product.ProductName, product.Brand, product.Variant, product.PackSizeText, product.Barcode);

    public bool AppliesTo(ExtractedProduct product) => this == For(product, Choice, Target);
}

public static class ExtractedProductIdentity
{
    public static Product ToCatalogueProduct(this ExtractedProduct product) => new()
    {
        Name = product.ProductName,
        Brand = product.Brand,
        Variant = product.Variant,
        PackSizeText = product.PackSizeText,
        Category = product.Category,
        Barcode = product.Barcode
    };

    public static Task<(int ProductId, ProductMatch Match)> SaveOfferAsync(ExtractedProduct extracted, Product product,
        Offer offer, IReadOnlyList<Product> catalogue, Func<Product, Task<int>> createProduct,
        Func<Offer, Task<int>> addOffer)
    {
        var decision = extracted.CatalogueReview;
        var confirmed = decision is not null && decision.AppliesTo(extracted) ? decision.Target : null;
        return ProductIdentity.SaveOfferAsync(product, offer, catalogue, createProduct, addOffer,
            decision?.Choice ?? ProductReviewChoice.Automatic, confirmed);
    }
}
