using FlyerMonkey.Shared.Model;
using Microsoft.Data.SqlClient;

namespace SQLServerConnection.Data;

public sealed class OfferRepository : IOfferRepository
{
    private readonly string _connectionString;

    public OfferRepository(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException(
                "A database connection string is required.",
                nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    private async Task<SqlConnection> OpenConnectionWithRetryAsync(
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        SqlException? lastException = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var connection = new SqlConnection(_connectionString);

            try
            {
                await connection.OpenAsync();
                return connection;
            }
            catch (SqlException ex)
            {
                lastException = ex;
                await connection.DisposeAsync();

                if (attempt == maxAttempts)
                {
                    break;
                }

                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));

                Console.WriteLine(
                    $"SQL connection attempt {attempt} failed. " +
                    $"Retrying in {delay.TotalSeconds:0} seconds.");

                await Task.Delay(delay, cancellationToken);
            }
        }

        throw new InvalidOperationException(
            $"Unable to connect to SQL Server after {maxAttempts} attempts.",
            lastException);
    }
    public async Task<int> AddOfferAsync(
    Offer offer,
    CancellationToken cancellationToken = default)
    {
        const string sql = """
        INSERT INTO Offers
        (
            ProductID,
            RetailerID,
            RetailerLocationID,
            AdvertisedPrice,
            RegularPrice,
            AdvertisedSaving,
            ValidFrom,
            ValidTo,
            SourceType,
            SourceDescription,
            FlyerPageNumber,
            FlyerImageBlobPath,
            PromoText,
            UnitPriceText,
            MultiBuyQuantity,
            MultiBuyPrice,
            MemberPrice,
            MemberOnlyFlag,
            SourceProductText
        )
        OUTPUT INSERTED.ID
        VALUES
        (
            @ProductID,
            @RetailerID,
            @RetailerLocationID,
            @AdvertisedPrice,
            @RegularPrice,
            @AdvertisedSaving,
            @ValidFrom,
            @ValidTo,
            @SourceType,
            @SourceDescription,
            @FlyerPageNumber,
            @FlyerImageBlobPath,
            @PromoText,
            @UnitPriceText,
            @MultiBuyQuantity,
            @MultiBuyPrice,
            @MemberPrice,
            @MemberOnlyFlag,
            @SourceProductText
        );
        """;

        await using var connection =
            await OpenConnectionWithRetryAsync(cancellationToken);

        await using var command =
            new SqlCommand(sql, connection);

        command.Parameters.AddWithValue("@ProductID", offer.ProductID);
        command.Parameters.AddWithValue("@RetailerID", offer.RetailerID);

        command.Parameters.AddWithValue(
            "@RetailerLocationID",
            (object?)offer.RetailerLocationID ?? DBNull.Value);

        command.Parameters.AddWithValue(
            "@AdvertisedPrice",
            (object?)offer.AdvertisedPrice ?? DBNull.Value);

        command.Parameters.AddWithValue(
            "@RegularPrice",
            (object?)offer.RegularPrice ?? DBNull.Value);

        command.Parameters.AddWithValue(
            "@AdvertisedSaving",
            (object?)offer.AdvertisedSaving ?? DBNull.Value);

        command.Parameters.AddWithValue(
            "@ValidFrom",
            (object?)offer.ValidFrom ?? DBNull.Value);

        command.Parameters.AddWithValue(
            "@ValidTo",
            (object?)offer.ValidTo ?? DBNull.Value);

        command.Parameters.AddWithValue(
            "@SourceType",
            (object?)offer.SourceType ?? DBNull.Value);

        command.Parameters.AddWithValue(
            "@SourceDescription",
            (object?)offer.SourceDescription ?? DBNull.Value);

        command.Parameters.AddWithValue(
            "@FlyerPageNumber",
            (object?)offer.FlyerPageNumber ?? DBNull.Value);

        command.Parameters.AddWithValue(
            "@FlyerImageBlobPath",
            (object?)offer.FlyerImageBlobPath ?? DBNull.Value);

        command.Parameters.AddWithValue(
            "@PromoText",
            (object?)offer.PromoText ?? DBNull.Value);

        command.Parameters.AddWithValue(
            "@UnitPriceText",
            (object?)offer.UnitPriceText ?? DBNull.Value);

        command.Parameters.AddWithValue(
            "@MultiBuyQuantity",
            (object?)offer.MultiBuyQuantity ?? DBNull.Value);

        command.Parameters.AddWithValue(
            "@MultiBuyPrice",
            (object?)offer.MultiBuyPrice ?? DBNull.Value);

        command.Parameters.AddWithValue(
            "@MemberPrice",
            (object?)offer.MemberPrice ?? DBNull.Value);

        command.Parameters.AddWithValue(
            "@MemberOnlyFlag",
            (object?)offer.MemberOnlyFlag ?? DBNull.Value);

        command.Parameters.AddWithValue(
            "@SourceProductText",
            (object?)offer.SourceProductText ?? DBNull.Value);

        var result =
            await command.ExecuteScalarAsync(cancellationToken);

        return Convert.ToInt32(result);
    }

    public async Task<List<OfferSummary>> GetOfferSummariesAsync(
    CancellationToken cancellationToken = default)
    {
        var offers = new List<OfferSummary>();

        const string sql = """
        SELECT
            o.ID AS OfferID,
            p.ID AS ProductID,
            p.Name AS ProductName,
            p.Brand,
            p.Variant,
            p.PackSizeText,
            p.Category,

            r.ID AS RetailerID,
            r.Name AS RetailerName,

            rl.StoreName,
            rl.Suburb,

            o.AdvertisedPrice,
            o.RegularPrice,
            o.AdvertisedSaving,
            o.PromoText,
            o.UnitPriceText,
            o.ValidFrom,
            o.ValidTo

        FROM dbo.Offers o

        INNER JOIN dbo.Products p
            ON p.ID = o.ProductID

        INNER JOIN dbo.Retailers r
            ON r.ID = o.RetailerID

        LEFT JOIN dbo.RetailerLocations rl
            ON rl.ID = o.RetailerLocationID

        ORDER BY
            o.AdvertisedPrice,
            p.Name;
        """;

        await using var connection =
            await OpenConnectionWithRetryAsync(cancellationToken);

        await using var command =
            new SqlCommand(sql, connection);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            offers.Add(new OfferSummary
            {
                OfferID = Convert.ToInt32(reader["OfferID"]),
                ProductID = Convert.ToInt32(reader["ProductID"]),

                ProductName =
                    reader["ProductName"]?.ToString() ?? string.Empty,

                Brand =
                    reader["Brand"] == DBNull.Value
                        ? null
                        : reader["Brand"]?.ToString(),

                Variant =
                    reader["Variant"] == DBNull.Value
                        ? null
                        : reader["Variant"]?.ToString(),

                PackSizeText =
                    reader["PackSizeText"] == DBNull.Value
                        ? null
                        : reader["PackSizeText"]?.ToString(),

                Category =
                    reader["Category"] == DBNull.Value
                        ? null
                        : reader["Category"]?.ToString(),

                RetailerID = Convert.ToInt32(reader["RetailerID"]),

                RetailerName =
                    reader["RetailerName"]?.ToString() ?? string.Empty,

                StoreName =
                    reader["StoreName"] == DBNull.Value
                        ? null
                        : reader["StoreName"]?.ToString(),

                Suburb =
                    reader["Suburb"] == DBNull.Value
                        ? null
                        : reader["Suburb"]?.ToString(),

                AdvertisedPrice =
                    reader["AdvertisedPrice"] == DBNull.Value
                        ? null
                        : Convert.ToDecimal(reader["AdvertisedPrice"]),

                RegularPrice =
                    reader["RegularPrice"] == DBNull.Value
                        ? null
                        : Convert.ToDecimal(reader["RegularPrice"]),

                AdvertisedSaving =
                    reader["AdvertisedSaving"] == DBNull.Value
                        ? null
                        : Convert.ToDecimal(reader["AdvertisedSaving"]),

                PromoText =
                    reader["PromoText"] == DBNull.Value
                        ? null
                        : reader["PromoText"]?.ToString(),

                UnitPriceText =
                    reader["UnitPriceText"] == DBNull.Value
                        ? null
                        : reader["UnitPriceText"]?.ToString(),

                ValidFrom =
                    reader["ValidFrom"] == DBNull.Value
                        ? null
                        : Convert.ToDateTime(reader["ValidFrom"]),

                ValidTo =
                    reader["ValidTo"] == DBNull.Value
                        ? null
                        : Convert.ToDateTime(reader["ValidTo"])
            });
        }

        return offers;
    }
    public Task<List<Offer>> GetOffersAsync(
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}