using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using FlyerMonkey.Reviewer.Windows.Models;
using FlyerMonkey.Shared.Model;
using SQLServerConnection.Data;

namespace FlyerMonkey.Reviewer.Windows.Services;

public static class ReviewerOffer
{
    public static (string From, string To)? PrintedDates(string text)
    {
        const string weekday = @"(?:(?:Mon(?:day)?|Tue(?:sday)?|Wed(?:nesday)?|Thu(?:rsday)?|Fri(?:day)?|Sat(?:urday)?|Sun(?:day)?)\s+)?";
        const string month = @"(?:Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|Jun(?:e)?|Jul(?:y)?|Aug(?:ust)?|Sep(?:tember)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)";
        var pattern = $@"\b{weekday}(?<sd>\d{{1,2}})\s+(?<sm>{month})(?:\s+(?<sy>20\d{{2}}))?\s*(?:\bto\b|[-–—])\s*{weekday}(?<ed>\d{{1,2}})\s+(?<em>{month})\s+(?<ey>20\d{{2}})\b";
        var distinct = new HashSet<(DateOnly From, DateOnly To)>();
        foreach (Match match in Regex.Matches(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            if (!ParseDate(match.Groups["ed"].Value, match.Groups["em"].Value, match.Groups["ey"].Value, out var end))
                continue;
            var startYear = match.Groups["sy"].Success ? match.Groups["sy"].Value : match.Groups["ey"].Value;
            if (!ParseDate(match.Groups["sd"].Value, match.Groups["sm"].Value, startYear, out var start)) continue;
            if (start > end && !match.Groups["sy"].Success && start.Month == 12 && end.Month == 1)
                start = start.AddYears(-1);
            if (start <= end) distinct.Add((start, end));
        }
        if (distinct.Count != 1) return null;
        var range = distinct.Single();
        return (range.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            range.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    private static bool ParseDate(string day, string month, string year, out DateOnly date) =>
        DateOnly.TryParseExact($"{day} {month} {year}", ["d MMM yyyy", "d MMMM yyyy"],
            CultureInfo.GetCultureInfo("en-AU"), DateTimeStyles.None, out date);

    public static bool TryDates(string? start, string? end, out DateOnly from, out DateOnly to)
    {
        var startValid = DateOnly.TryParseExact(start?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out from);
        var endValid = DateOnly.TryParseExact(end?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out to);
        return startValid && endValid && from <= to;
    }

    public static Offer Create(ExtractedProduct card, int retailerId, int? locationId, string edition, string page,
        int pageNumber, DateOnly from, DateOnly to)
    {
        var price = ProductValidationService.ParseMoney(card.Price);
        return new Offer
        {
            RetailerID = retailerId, RetailerLocationID = locationId,
            AdvertisedPrice = price, RegularPrice = ProductValidationService.ParseMoney(card.RegularPrice),
            AdvertisedSaving = ProductValidationService.ParseMoney(card.Saving),
            ValidFrom = from.ToDateTime(TimeOnly.MinValue), ValidTo = to.ToDateTime(TimeOnly.MinValue),
            SourceType = "Flyer", SourceDescription = OfferIdentity.Source(edition, page), FlyerPageNumber = pageNumber,
            PromoText = card.Promotion, PromotionTypes = JsonSerializer.Serialize(card.PromotionTypes ?? []),
            UnitPriceText = card.UnitPrice,
            MemberOnlyFlag = card.MemberOnly || (card.PromotionTypes?.Contains("Member-only", StringComparer.OrdinalIgnoreCase) == true),
            MemberPrice = ProductValidationService.ParseMoney(card.MemberPrice),
            MultiBuyQuantity = card.OfferQuantity is > 1 ? card.OfferQuantity : null,
            MultiBuyPrice = card.OfferQuantity is > 1 ? ProductValidationService.ParseMoney(card.MultiBuyPrice) ?? price : null
        };
    }

    public static async Task<(int ProductId, ProductMatch Match, int? ReusedOfferId)> CommitAsync(
        ExtractedProduct card, Product product, Offer offer, IReadOnlyList<Product> catalogue,
        Func<int, Task<List<Offer>>> readExisting, Func<Product, Task<int>> createProduct, Func<Offer, Task<int>> addOffer)
    {
        if (card.OfferReview == OfferReviewChoice.ReuseExisting && card.ConfirmedOfferID.HasValue)
        {
            var review = card.CatalogueReview;
            var match = ProductIdentity.Match(product, catalogue, review?.Choice ?? ProductReviewChoice.Automatic,
                review?.AppliesTo(card) == true ? review.Target : null);
            if (match.ReuseId is int existingProductId)
            {
                offer.ProductID = existingProductId;
                var existing = await readExisting(existingProductId);
                var reusedOffer = OfferIdentity.ConfirmedReuse(offer, existing, card.ConfirmedOfferID);
                if (reusedOffer.HasValue) return (existingProductId, match, reusedOffer);
            }
        }
        var saved = await ExtractedProductIdentity.SaveOfferAsync(card, product, offer, catalogue, createProduct, addOffer);
        return (saved.ProductId, saved.Match, null);
    }
}
