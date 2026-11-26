using System.Data;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure;
using Azure.Storage.Blobs;
using FlyerMonkey.Shared.Model;
using Microsoft.Data.SqlClient;
using SQLServerConnection.Data;

string[] filters = ["all", "no-offers", "expired-only", "current", "duplicates", "missing-images"];
var filter = args.Length > 0 ? args[0] : "all";
var perth = TimeZoneInfo.FindSystemTimeZoneById("Australia/Perth");
var asOf = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, perth).Date;
if (args.Length > 2 || !filters.Contains(filter)
    || (args.Length == 2 && !DateTime.TryParseExact(args[1], "yyyy-MM-dd",
        CultureInfo.InvariantCulture, DateTimeStyles.None, out asOf))
    || asOf >= DateTime.MaxValue.Date)
{
    Console.Error.WriteLine($"Usage: CatalogueAudit [{string.Join('|', filters)}] [yyyy-MM-dd]");
    return 2;
}

var sql = Environment.GetEnvironmentVariable("FLYERMONKEY_SQL_CONNECTION");
if (string.IsNullOrWhiteSpace(sql))
{
    Console.Error.WriteLine("Set FLYERMONKEY_SQL_CONNECTION to a catalogue connection.");
    return 2;
}

var blob = Environment.GetEnvironmentVariable("FLYERMONKEY_BLOB_CONNECTION");
var images = string.IsNullOrWhiteSpace(blob) ? null : new BlobContainerClient(blob, "product-images");
var rows = new List<AuditRecord>();
const string reportSql = """
    SELECT p.ID, p.Name, p.Brand, p.Variant, p.PackSizeText, p.Barcode, p.ImageBlobPath,
           counts.OfferCount, latest.ID AS LatestOfferID, latest.ValidTo AS LatestValidTo,
           counts.CurrentOfferCount, counts.ExpiredOfferCount
    FROM dbo.Products p
    CROSS APPLY (
        SELECT COUNT(*) AS OfferCount,
               COUNT(CASE WHEN o.ValidFrom < @NextDay AND o.ValidTo >= @AsOf THEN 1 END) AS CurrentOfferCount,
               COUNT(CASE WHEN o.ValidTo < @AsOf THEN 1 END) AS ExpiredOfferCount
        FROM dbo.Offers o WHERE o.ProductID = p.ID
    ) counts
    OUTER APPLY (
        SELECT TOP (1) o.ID, o.ValidTo FROM dbo.Offers o WHERE o.ProductID = p.ID
        ORDER BY o.ValidTo DESC, o.ID DESC
    ) latest
    ORDER BY p.ID;
    """;

try
{
    await using (var connection = new SqlConnection(sql))
    {
        await connection.OpenAsync();
        await using var command = new SqlCommand(reportSql, connection);
        command.Parameters.Add("@AsOf", SqlDbType.Date).Value = asOf;
        command.Parameters.Add("@NextDay", SqlDbType.Date).Value = asOf.AddDays(1);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var product = new Product
            {
                ID = reader.GetInt32(0), Name = reader[1] as string ?? "",
                Brand = reader[2] as string, Variant = reader[3] as string,
                PackSizeText = reader[4] as string, Barcode = reader[5] as string,
                ImageBlobPath = reader[6] as string
            };
            rows.Add(new AuditRecord(product, reader.GetInt32(7),
                reader.IsDBNull(8) ? null : Convert.ToInt64(reader[8], CultureInfo.InvariantCulture),
                reader.IsDBNull(9) ? null : reader.GetDateTime(9), reader.GetInt32(10), reader.GetInt32(11)));
        }
    }
}
catch (SqlException error)
{
    Console.Error.WriteLine($"Catalogue SELECT failed (SQL error {error.Number}).");
    return 1;
}

var catalogue = rows.Select(row => row.Product).ToArray();
var relationships = ProductIdentity.AuditCandidates(catalogue).ToArray();
var duplicateCandidates = relationships
    .Where(pair => ProductIdentity.IsDuplicateCandidate(pair.Relationship))
    .SelectMany(pair => new[] { (ID: pair.ProductID, OtherID: pair.CandidateProductID),
        (ID: pair.CandidateProductID, OtherID: pair.ProductID) })
    .ToLookup(candidate => candidate.ID, candidate => candidate.OtherID);
var candidateGroups = Enum.GetValues<ProductRelationship>().Select(relationship =>
{
    var pairs = relationships.Where(pair => pair.Relationship == relationship).ToArray();
    return new
    {
        Relationship = relationship,
        PairCount = pairs.Length,
        ProductCount = pairs.SelectMany(pair => new[] { pair.ProductID, pair.CandidateProductID }).Distinct().Count(),
        Pairs = pairs.Select(pair => new { pair.ProductID, pair.CandidateProductID })
    };
}).ToArray();
var imageStatuses = new Dictionary<string, string>(StringComparer.Ordinal);
var results = new List<object>();
var unverified = 0;
foreach (var row in rows)
{
    if (filter == "no-offers" && row.OfferCount != 0
        || filter == "expired-only" && (row.OfferCount == 0 || row.ExpiredCount != row.OfferCount)
        || filter == "current" && row.CurrentCount == 0) continue;

    var product = row.Product;
    var candidates = duplicateCandidates[product.ID].Order().ToArray();
    if (filter == "duplicates" && candidates.Length == 0) continue;
    var imageStatus = await ImageStatusAsync(product.ImageBlobPath);
    if (imageStatus == "Unverified") unverified++;
    if (filter == "missing-images" && imageStatus is not ("MissingPath" or "InvalidPath" or "MissingBlob"))
        continue;

    results.Add(new
    {
        ProductID = product.ID, product.Name, product.Brand, product.Variant,
        PackSize = product.PackSizeText, row.OfferCount, row.LatestOfferID, row.LatestValidTo,
        CurrentOfferCount = row.CurrentCount, ImageStatus = imageStatus,
        PotentialDuplicate = candidates.Length > 0, CandidateProductIDs = candidates
    });
}

Console.WriteLine(JsonSerializer.Serialize(new
{
    AsOf = asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
    Filter = filter, UnverifiedImages = unverified, Products = results,
    DuplicateCandidateProductCount = duplicateCandidates.Count,
    CandidateGroups = filter is "all" or "duplicates" ? candidateGroups : null
}, new JsonSerializerOptions
{
    WriteIndented = true,
    Converters = { new JsonStringEnumConverter() }
}));
return 0;

async Task<string> ImageStatusAsync(string? path)
{
    const string prefix = "product-images/";
    if (string.IsNullOrWhiteSpace(path)) return "MissingPath";
    if (!path.StartsWith(prefix, StringComparison.Ordinal) || path.Length == prefix.Length
        || path.Any(c => char.IsControl(c) || c is '\\' or '%' or '?' or '#' or ':')) return "InvalidPath";
    if (images is null) return "Unverified";
    if (imageStatuses.TryGetValue(path, out var cached)) return cached;
    string status;
    try
    {
        var exists = await images.GetBlobClient(path[prefix.Length..]).ExistsAsync();
        status = exists.Value ? "Present" : "MissingBlob";
    }
    catch (RequestFailedException)
    {
        status = "Unverified";
    }
    imageStatuses[path] = status;
    return status;
}

record AuditRecord(Product Product, int OfferCount, long? LatestOfferID, DateTime? LatestValidTo,
    int CurrentCount, int ExpiredCount);
