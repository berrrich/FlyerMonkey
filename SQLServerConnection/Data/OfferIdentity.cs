using System.Text.Json;
using System.Text.RegularExpressions;
using FlyerMonkey.Shared.Model;

namespace SQLServerConnection.Data;

public enum OfferRelationship { LikelyExistingOffer, PossibleExistingOffer }
public enum OfferReviewChoice { ReuseExisting, KeepDistinct, ReviewLater }

public sealed record OfferCandidate(int OfferID, OfferRelationship Relationship, string Details)
{
    public bool CanReuse => Relationship == OfferRelationship.LikelyExistingOffer;
}

public static class OfferIdentity
{
    private const string SourcePrefix = "Flyer edition: ";

    public static string Source(string edition, string page) => $"{SourcePrefix}{edition} | {page}";

    private static string Edition(Offer offer) => offer.SourceDescription?.StartsWith(SourcePrefix, StringComparison.Ordinal) == true
        ? Normalize(offer.SourceDescription[SourcePrefix.Length..].Split(" | ", 2)[0]) : "";

    public static IReadOnlyList<OfferCandidate> Candidates(Offer proposed, IEnumerable<Offer> existing) =>
        existing.Select(offer => Compare(proposed, offer)).OfType<OfferCandidate>()
            .OrderBy(candidate => candidate.Relationship).ThenBy(candidate => candidate.OfferID).ToArray();

    public static int? ConfirmedReuse(Offer proposed, IEnumerable<Offer> existing, int? offerId) =>
        offerId is int id && existing.Any(offer => offer.ID == id && Compare(proposed, offer)?.CanReuse == true)
            ? id : null;

    private static OfferCandidate? Compare(Offer proposed, Offer existing)
    {
        if (proposed.ProductID != existing.ProductID || proposed.RetailerID != existing.RetailerID
            || proposed.AdvertisedPrice is null || proposed.AdvertisedPrice != existing.AdvertisedPrice
            || (proposed.RetailerLocationID.HasValue && existing.RetailerLocationID.HasValue
                && proposed.RetailerLocationID != existing.RetailerLocationID)
            || (proposed.ValidFrom?.Date > existing.ValidTo?.Date || existing.ValidFrom?.Date > proposed.ValidTo?.Date))
            return null;

        var edition = Edition(proposed);
        var otherEdition = Edition(existing);
        if (edition.Length > 0 && otherEdition.Length > 0 && edition != otherEdition) return null;

        var scopeMatches = proposed.RetailerLocationID.HasValue && proposed.RetailerLocationID == existing.RetailerLocationID;
        var datesMatch = proposed.ValidFrom.HasValue && proposed.ValidTo.HasValue
            && proposed.ValidFrom.Value.Date == existing.ValidFrom?.Date && proposed.ValidTo.Value.Date == existing.ValidTo?.Date;
        var termsMatch = Equals(Terms(proposed), Terms(existing));
        var editionMatches = edition.Length > 0 && edition == otherEdition;
        var relationship = scopeMatches && datesMatch && termsMatch && editionMatches
            ? OfferRelationship.LikelyExistingOffer : OfferRelationship.PossibleExistingOffer;
        return new OfferCandidate(existing.ID, relationship,
            $"Product {existing.ProductID} • Retailer {existing.RetailerID} • Store {existing.RetailerLocationID?.ToString() ?? "unknown"}\n"
            + $"{existing.AdvertisedPrice:C} • {existing.ValidFrom:yyyy-MM-dd} to {existing.ValidTo:yyyy-MM-dd}\n"
            + $"{existing.SourceDescription ?? "Source unavailable"} • Page {existing.FlyerPageNumber?.ToString() ?? "unknown"}\n"
            + $"Terms: {existing.PromoText} {existing.PromotionTypes} • Was {existing.RegularPrice:C} • Save {existing.AdvertisedSaving:C}\n"
            + $"Unit {existing.UnitPriceText} • Member price {existing.MemberPrice:C} • Member only {existing.MemberOnlyFlag}\n"
            + $"Multi-buy {existing.MultiBuyQuantity} for {existing.MultiBuyPrice:C}\n"
            + $"Edition {(editionMatches ? "matches" : "unverified")} • Dates {(datesMatch ? "match" : "differ")} • "
            + $"Store {(scopeMatches ? "matches" : "unverified")} • Terms {(termsMatch ? "match" : "differ")}");
    }

    private static object Terms(Offer offer) => new
    {
        offer.RegularPrice, offer.AdvertisedSaving, offer.MultiBuyQuantity, offer.MultiBuyPrice, offer.MemberPrice,
        MemberOnly = offer.MemberOnlyFlag == true,
        Text = Normalize(offer.PromoText),
        Types = NormalizeTypes(offer.PromotionTypes),
        Unit = Normalize(offer.UnitPriceText)
    };

    private static string NormalizeTypes(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return "";
        try
        {
            return string.Join("\n", (JsonSerializer.Deserialize<string[]>(json) ?? []).Select(Normalize)
                .Where(value => value.Length > 0).Distinct().Order(StringComparer.Ordinal));
        }
        catch (JsonException) { return Normalize(json); }
    }

    private static string Normalize(string? value) =>
        Regex.Replace((value ?? "").Trim(), @"\s+", " ").ToUpperInvariant();
}
