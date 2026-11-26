using System.Text.RegularExpressions;
using System.Globalization;
using FlyerMonkey.Shared.Model;

namespace SQLServerConnection.Data;

public sealed record ProductMatch(int? ReuseId, IReadOnlyList<int> CandidateIds)
{
    public bool NeedsReview => ReuseId is null && CandidateIds.Count > 0;
}

public enum ProductRelationship
{
    LikelyDuplicate,
    PossibleDuplicate,
    RelatedProduct,
    NotDuplicate
}

public sealed record ProductCandidate(int ProductID, int CandidateProductID, ProductRelationship Relationship);

public sealed record CatalogueCandidate(int ProductID, string Name, string? Brand, string? Variant,
    string? PackSizeText, string? Barcode, ProductRelationship Relationship)
{
    public bool CanReuse => ProductIdentity.IsDuplicateCandidate(Relationship);
}

public enum ProductReviewChoice
{
    Automatic,
    ReuseExisting,
    KeepSeparate,
    ReviewLater
}

public static class ProductIdentity
{
    public static bool IsDuplicateCandidate(ProductRelationship relationship) =>
        relationship is ProductRelationship.LikelyDuplicate or ProductRelationship.PossibleDuplicate;

    public static IEnumerable<ProductCandidate> AuditCandidates(IEnumerable<Product> catalogue)
    {
        var byName = new Dictionary<string, List<Identity>>();
        var byBarcode = new Dictionary<string, List<Identity>>();
        var byBrandAndPack = new Dictionary<(string, string), List<Identity>>();
        foreach (var product in catalogue.OrderBy(product => product.ID))
        {
            var current = new Identity(product);
            var names = current.Name.Length == 0 ? [] : AddToIndex(byName, current.Name, current);
            var barcodes = current.Barcode.Length == 0 ? [] : AddToIndex(byBarcode, current.Barcode, current);
            var brandedPacks = current.Brand.Length == 0 || current.Pack.Length == 0 ? []
                : AddToIndex(byBrandAndPack, (current.Brand, current.Pack), current);
            foreach (var other in names.Union(barcodes).Union(brandedPacks).OrderBy(other => other.Product.ID))
                if (IsCandidate(current, other))
                    yield return new ProductCandidate(other.Product.ID, product.ID, Classify(current, other));
        }
    }

    private static IEnumerable<Identity> AddToIndex<TKey>(Dictionary<TKey, List<Identity>> index, TKey key,
        Identity current) where TKey : notnull
    {
        if (!index.TryGetValue(key, out var entries))
            index.Add(key, entries = []);
        entries.Add(current);
        return entries.Take(entries.Count - 1);
    }

    public static IReadOnlyList<CatalogueCandidate> FindCandidates(Product product, IEnumerable<Product> catalogue) =>
        CompareCatalogue(new Identity(product), catalogue)
            .OrderBy(pair => pair.Candidate.Relationship)
            .ThenBy(pair => pair.Candidate.ProductID)
            .Select(pair => pair.Candidate)
            .ToArray();

    private static IEnumerable<(CatalogueCandidate Candidate, bool SameBarcode)> CompareCatalogue(
        Identity current, IEnumerable<Product> catalogue)
    {
        foreach (var product in catalogue)
        {
            if (current.Product.ID != 0 && current.Product.ID == product.ID)
                continue;
            var other = new Identity(product);
            if (IsCandidate(current, other))
                yield return (new CatalogueCandidate(product.ID, product.Name, product.Brand, product.Variant,
                    product.PackSizeText, product.Barcode, Classify(current, other)), SameBarcode(current, other));
        }
    }

    private static bool SameName(Identity left, Identity right) => left.Name.Length > 0 && left.Name == right.Name;
    private static bool SameBrand(Identity left, Identity right) => left.Brand.Length > 0 && left.Brand == right.Brand;
    private static bool SameBarcode(Identity left, Identity right) => left.Barcode.Length > 0 && left.Barcode == right.Barcode;

    private static bool IsCandidate(Identity left, Identity right) =>
        SameName(left, right) || SameBarcode(left, right) ||
        (SameBrand(left, right) && left.Pack.Length > 0 && left.Pack == right.Pack
            && (left.Alternatives.Contains(right.Name) || right.Alternatives.Contains(left.Name)
                || left.Alternatives.Overlaps(right.Alternatives)));

    private static ProductRelationship Classify(Identity left, Identity right)
    {
        if (Conflicts(left.Brand, right.Brand))
            return ProductRelationship.NotDuplicate;

        var sameBarcode = SameBarcode(left, right);
        if (Conflicts(left.Pack, right.Pack) || Conflicts(left.Variant, right.Variant))
            return SameBrand(left, right) ? ProductRelationship.RelatedProduct : ProductRelationship.NotDuplicate;
        if (Conflicts(left.RawBarcode, right.RawBarcode) && !sameBarcode)
            return ProductRelationship.NotDuplicate;
        if (left.UnresolvedPack || right.UnresolvedPack || left.Variant != right.Variant
            || (left.Pack != right.Pack && (left.Pack.Contains('×') || right.Pack.Contains('×')))
            || !left.Alternatives.SetEquals(right.Alternatives))
            return ProductRelationship.PossibleDuplicate;
        if (sameBarcode || (SameName(left, right) && SameBrand(left, right)
            && left.Pack.Length > 0 && left.Pack == right.Pack))
            return ProductRelationship.LikelyDuplicate;
        return ProductRelationship.PossibleDuplicate;
    }

    private sealed class Identity
    {
        public Identity(Product product)
        {
            Product = product;
            var key = Key(product);
            (Brand, Name, Variant, Barcode) = key;
            Pack = NormalizePack(product.PackSizeText);
            UnresolvedPack = Pack.Length == 0 && HasUnrecognisedPack(product.PackSizeText);
            if (UnresolvedPack)
                Pack = Regex.Replace(product.PackSizeText!.ToUpperInvariant(), @"\s+", "");
            RawBarcode = BarcodeText(product.Barcode);
            foreach (var label in new[] { Name, Variant })
            {
                var alternatives = label.Split(" OR ", StringSplitOptions.RemoveEmptyEntries);
                if (alternatives.Length > 1) Alternatives.UnionWith(alternatives);
            }
        }

        public Product Product { get; }
        public string Brand { get; }
        public string Name { get; }
        public string Variant { get; }
        public string Barcode { get; }
        public string Pack { get; }
        public bool UnresolvedPack { get; }
        public string RawBarcode { get; }
        public HashSet<string> Alternatives { get; } = new(StringComparer.Ordinal);
    }

    private static string NormalizePack(string? value)
    {
        var pack = Regex.Replace(value ?? "", @"\blitres?\b", "L",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var match = Regex.Match(pack, @"^\s*(?<count>[1-9][0-9]*)\s*[x×]\s*(?<size>.+?)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success) return Pack(pack);
        var unitPack = Pack(match.Groups["size"].Value);
        return unitPack.Length == 0 ? "" : match.Groups["count"].Value + "×" + unitPack;
    }

    public static ProductMatch Match(Product product, IReadOnlyList<Product> catalogue,
        ProductReviewChoice choice = ProductReviewChoice.Automatic, CatalogueCandidate? confirmed = null)
    {
        var comparisons = CompareCatalogue(new Identity(product), catalogue).ToArray();
        var candidates = comparisons.Where(pair => pair.Candidate.CanReuse).ToArray();
        int? reuseId = null;
        if (choice == ProductReviewChoice.ReuseExisting && confirmed is not null)
        {
            var current = candidates.Select(pair => pair.Candidate)
                .SingleOrDefault(candidate => candidate.ProductID == confirmed.ProductID);
            if (current is not null && (current with { Relationship = confirmed.Relationship }) == confirmed)
                reuseId = current.ProductID;
        }
        else if (choice == ProductReviewChoice.Automatic)
        {
            // A verified barcode can disambiguate candidates, but only if one record claims it.
            var barcodeClaims = comparisons.Where(pair => pair.SameBarcode).ToArray();
            if (barcodeClaims.Length == 1 && barcodeClaims[0].Candidate.Relationship == ProductRelationship.LikelyDuplicate)
                reuseId = barcodeClaims[0].Candidate.ProductID;
            else if (barcodeClaims.Length == 0 && candidates.Length == 1
                && candidates[0].Candidate.Relationship == ProductRelationship.LikelyDuplicate)
                reuseId = candidates[0].Candidate.ProductID;
        }
        return new ProductMatch(reuseId, candidates.Select(pair => pair.Candidate.ProductID).Order().ToArray());
    }

    public static async Task<(int ProductId, ProductMatch Match)> SaveOfferAsync(
        Product product, Offer offer, IReadOnlyList<Product> catalogue,
        Func<Product, Task<int>> createProduct, Func<Offer, Task<int>> addOffer,
        ProductReviewChoice choice = ProductReviewChoice.Automatic, CatalogueCandidate? confirmed = null)
    {
        var match = Match(product, catalogue, choice, confirmed);
        var productId = match.ReuseId ?? await createProduct(product);
        offer.ProductID = productId;
        await addOffer(offer);
        return (productId, match);
    }

    private static bool Conflicts(string left, string right) =>
        left.Length > 0 && right.Length > 0 && left != right;

    private static bool HasUnrecognisedPack(string? value) =>
        Normalize(value) is not ("" or "EA" or "EACH");

    private static (string Brand, string Name, string Variant, string Barcode) Key(Product product)
    {
        var brand = Normalize(product.Brand).Replace(" ", "");
        var name = Normalize(product.Name);
        // Retailers may repeat the brand in the product description or omit it there.
        if (brand.Length > 0)
        {
            var words = name.Split(' ');
            var prefix = "";
            for (var index = 0; index < words.Length - 1 && prefix.Length < brand.Length; index++)
            {
                prefix += words[index];
                if (prefix == brand) name = string.Join(' ', words.Skip(index + 1));
            }
        }
        return (brand, name, Normalize(product.Variant), Barcode(product.Barcode));
    }

    private static string Pack(string? value)
    {
        var match = Regex.Match((value ?? "").Trim().ToUpperInvariant(),
            @"^(?<amount>[0-9]+(?:\.[0-9]+)?)\s*(?<unit>G|KG|ML|L|PK|PACK|PCS)$");
        if (!match.Success || !decimal.TryParse(match.Groups["amount"].Value,
            NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
            || amount <= 0 || amount > 1_000_000) return "";
        var unit = match.Groups["unit"].Value;
        return unit switch
        {
            "KG" => MassOrVolume(amount * 1000, "G"),
            "L" => MassOrVolume(amount * 1000, "ML"),
            "PK" or "PACK" => MassOrVolume(amount, "PK"),
            _ => MassOrVolume(amount, unit)
        };
    }

    private static string MassOrVolume(decimal amount, string unit) =>
        amount.ToString("G29", CultureInfo.InvariantCulture) + unit;

    private static string Normalize(string? value) =>
        Regex.Replace((value ?? "").ToUpperInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();

    private static string Barcode(string? value)
    {
        var digits = BarcodeText(value);
        if (!Regex.IsMatch(digits, @"^(?:[0-9]{8}|[0-9]{12,14})$") || digits.All(c => c == '0'))
            return "";
        var sum = digits.Reverse().Select((c, index) => (c - '0') * (index % 2 == 0 ? 1 : 3)).Sum();
        return sum % 10 == 0 ? digits.PadLeft(14, '0') : "";
    }

    private static string BarcodeText(string? value) => Regex.Replace(value ?? "", @"[\s-]", "");
}
