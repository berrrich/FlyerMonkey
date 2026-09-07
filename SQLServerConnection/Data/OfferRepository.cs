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

    public Task<List<Offer>> GetOffersAsync(
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}