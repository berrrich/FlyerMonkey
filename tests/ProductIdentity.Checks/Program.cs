using FlyerMonkey.Shared.Model;
using SQLServerConnection.Data;

var canonical = Drink(184, "425ml", "Chocolate");
var formatting = Drink(0, "0.425 L", "chocolate", "Roke-by", "Protein Drink");
Require(ProductIdentity.Match(formatting, [canonical]).ReuseId == 184, "Normalised brand, name and size should match.");
Require(ProductIdentity.Match(Drink(0, "ea", "Chocolate"), [canonical]).NeedsReview, "Each is ambiguous.");
Require(ProductIdentity.Match(Drink(0, "2 x 425ml", "Chocolate"), [canonical]).CandidateIds.Count == 0, "A multipack must stay separate from a single.");
Require(ProductIdentity.Match(Drink(0, "425 ml", "Vanilla"), [canonical]).CandidateIds.Count == 0, "Different flavours are separate SKUs.");
Require(ProductIdentity.Match(Drink(0, "425 ml", ""), [canonical]).NeedsReview, "A missing flavour is ambiguous.");
Require(ProductIdentity.Match(Drink(0, "425 ml", ""), [canonical, Drink(187, "425ml", "")]).NeedsReview,
    "A generic row should not win automatically when another row names a possible flavour.");
Require(ProductIdentity.Match(Drink(0, "500ml", "Chocolate"), [canonical]).CandidateIds.Count == 0, "Different sizes are separate SKUs.");
Require(ProductIdentity.Match(formatting, [canonical, Drink(186, "425ml", "Chocolate")]).NeedsReview, "Two strong candidates require review.");

canonical.Barcode = "4006381333931";
var barcodeMatch = Drink(0, "ea", "", name: "Rokeby flavoured milk");
barcodeMatch.Barcode = "04006381333931";
Require(ProductIdentity.Match(barcodeMatch, [canonical]).NeedsReview, "A missing variant requires review even with a verified GTIN.");
var renamed = Drink(0, "425ml", "Chocolate", name: "Rokeby flavoured milk");
renamed.Barcode = barcodeMatch.Barcode;
Require(ProductIdentity.Match(renamed, [canonical]).ReuseId == 184, "A verified GTIN can bridge a compatible description change.");
var questionablePack = Drink(0, "2 x 425ml", "Chocolate");
questionablePack.Barcode = canonical.Barcode;
Require(ProductIdentity.Match(questionablePack, [canonical]).ReuseId is null, "A GTIN must not override different packaging.");
var competing = Drink(186, "500ml", "Chocolate");
competing.Barcode = canonical.Barcode;
Require(ProductIdentity.Match(renamed, [canonical, competing]).NeedsReview, "Conflicting claims to the same GTIN must be reviewed.");
var otherBarcode = Drink(0, "425ml", "Chocolate");
otherBarcode.Barcode = "4006381333948";
Require(ProductIdentity.Match(otherBarcode, [canonical]).ReuseId is null, "Different barcodes must stay separate.");

var redBull = Drink(94, "4 x 250ml", "", "Red Bull", "Energy Drink");
var v = Drink(117, "4 x 250ml", "N/A", "V", "V Energy Drink");
var monster = Drink(153, "4x500mL", "", "Monster", "Energy Drink");
var energyDrinks = ProductIdentity.AuditCandidates([monster, v, redBull]).ToArray();
Require(energyDrinks.Length == 3 && energyDrinks.All(pair =>
    pair.Relationship == ProductRelationship.NotDuplicate && pair.ProductID < pair.CandidateProductID),
    "Each cross-brand energy drink pair should appear once as NotDuplicate.");
CheckAuditPair(redBull, Drink(130, "4x250ml", "", "Red Bull", "Red Bull Energy Drink"),
    ProductRelationship.LikelyDuplicate);
CheckAuditPair(v, Drink(208, "4x250mL", "", "V", "V Energy Drink"),
    ProductRelationship.PossibleDuplicate);
CheckAuditPair(Drink(155, "2 Litre or Capsules 28 Pack", "", "Omo", "Laundry Liquid"),
    Drink(213, "2 Litre", "Active Clean", "Radiant", "Radiant Laundry Liquid"),
    ProductRelationship.NotDuplicate);

var coke = Drink(201, "1.25 Litre", "Zero Sugar", "Coca-Cola", "Coca-Cola");
CheckAuditPair(coke, Drink(220, "10 x 375ml", "Zero Sugar", "Coca-Cola", "Coca-Cola"),
    ProductRelationship.RelatedProduct);
CheckAuditPair(coke, Drink(221, "1250ml", "Zero Sugar", "Coca-Cola", "Coca-Cola"),
    ProductRelationship.LikelyDuplicate);
CheckAuditPair(coke, Drink(222, "1.25L", "Original Taste", "Coca-Cola", "Coca-Cola"),
    ProductRelationship.RelatedProduct);
CheckAuditPair(Drink(223, "3.75L", "Zero Sugar", "Coca-Cola", "Coca-Cola"),
    Drink(220, "10 x 375ml", "Zero Sugar", "Coca-Cola", "Coca-Cola"),
    ProductRelationship.RelatedProduct);

var favourites = new[]
{
    Drink(133, "686g-700g", "Special Edition Snack", "Cadbury", "Cadbury Favourites"),
    Drink(149, "264-265g", "", "Cadbury", "Cadbury Favourites"),
    Drink(177, "470g", "", "Cadbury", "Cadbury Favourites"),
    Drink(210, "686g-700g", "Ultimate", "Cadbury", "Cadbury Favourites")
};
var cadburyPairs = ProductIdentity.AuditCandidates(favourites).ToArray();
Require(cadburyPairs.Length == 6 && cadburyPairs.All(pair => pair.Relationship == ProductRelationship.RelatedProduct),
    "Cadbury Favourites pack sizes and named variants should remain distinct.");
CheckAuditPair(canonical, otherBarcode, ProductRelationship.NotDuplicate);
CheckAuditPair(canonical, competing, ProductRelationship.RelatedProduct);
CheckAuditPair(canonical, barcodeMatch, ProductRelationship.PossibleDuplicate);
CheckAuditPair(canonical, Drink(199, "425ml", "Chocolate"), ProductRelationship.LikelyDuplicate);
var camembert = Drink(91, "200g", "Double Brie or Camembert", "Tasmanian Heritage", "Brie");
var brieChoice = Drink(221, "200g", "", "Tasmanian Heritage", "Tasmanian Heritage Brie or Camembert");
CheckAuditPair(camembert, brieChoice, ProductRelationship.PossibleDuplicate);
CheckAuditPair(Drink(230, "200g", "", "Tasmanian Heritage", "Camembert"), brieChoice,
    ProductRelationship.PossibleDuplicate);
Require(ProductIdentity.Match(camembert, [brieChoice]).NeedsReview,
    "A matching cheese alternative should surface without triggering automatic reuse.");
redBull.Barcode = monster.Barcode = canonical.Barcode;
Require(ProductIdentity.Match(redBull, [monster]).ReuseId is null, "A shared barcode must not override conflicting brands.");

var offers = new List<Offer>();
var creates = 0;
Task<int> Create(Product product) => Task.FromResult(200 + ++creates);
Task<int> AddOffer(Offer offer)
{
    offers.Add(offer);
    return Task.FromResult(offers.Count);
}

var first = new Offer { AdvertisedPrice = 3m, SourceDescription = "page-002.pdf" };
var second = new Offer { AdvertisedPrice = 4m, SourceDescription = "page-003.pdf" };
var third = new Offer { AdvertisedPrice = 5m, SourceDescription = "page-004.pdf" };
await ProductIdentity.SaveOfferAsync(formatting, first, [canonical], Create, AddOffer);
await ProductIdentity.SaveOfferAsync(formatting, second, [canonical], Create, AddOffer);
var ambiguous = await ProductIdentity.SaveOfferAsync(Drink(0, "ea", "Chocolate"), third, [canonical], Create, AddOffer);
Require(creates == 1 && offers.SequenceEqual([first, second, third]), "Every extracted offer must reach the writer once.");
Require(first.ProductID == 184 && second.ProductID == 184 && third.ProductID == 201
    && ambiguous.Match.NeedsReview && third.AdvertisedPrice == 5m && third.SourceDescription == "page-004.pdf",
    "Reuse must preserve both offers and ambiguity must preserve the new offer and its data.");
Console.WriteLine("Product identity and offer preservation checks passed.");

static Product Drink(int id, string pack, string variant, string brand = "Rokeby", string name = "Rokeby Protein Drink") =>
    new() { ID = id, Brand = brand, Name = name, Variant = variant, PackSizeText = pack };

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void CheckAuditPair(Product left, Product right, ProductRelationship expected)
{
    var pair = ProductIdentity.AuditCandidates([right, left]).Single();
    Require(pair.Relationship == expected && pair.ProductID == Math.Min(left.ID, right.ID)
        && pair.CandidateProductID == Math.Max(left.ID, right.ID)
        && ProductIdentity.FindCandidates(left, [right]).Single().Relationship == expected
        && ProductIdentity.FindCandidates(right, [left]).Single().Relationship == expected,
        $"Expected {expected} for {left.ID} and {right.ID}; got {pair.Relationship}.");
}
