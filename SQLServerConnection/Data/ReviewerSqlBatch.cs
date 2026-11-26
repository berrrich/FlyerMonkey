using FlyerMonkey.Shared.Model;
using Microsoft.Data.SqlClient;
using System.Transactions;

namespace SQLServerConnection.Data;

/// <summary>One connection and local transaction for all Reviewer batch writes. Never retries.</summary>
public sealed class ReviewerSqlBatch : IAsyncDisposable
{
    private readonly SqlConnection _connection;
    private readonly SqlTransaction _transaction;
    private readonly ProductRepository _products;
    private readonly OfferRepository _offers;
    private bool _completionAttempted;
    private bool _writeFailed;
    private bool _disposed;

    private ReviewerSqlBatch(SqlConnection connection, SqlTransaction transaction)
    {
        _connection = connection;
        _transaction = transaction;
        _products = new ProductRepository(connection.ConnectionString);
        _offers = new OfferRepository(connection.ConnectionString);
    }

    public static async Task<ReviewerSqlBatch> OpenAsync(string connectionString)
    {
        if (Transaction.Current != null)
            throw new InvalidOperationException("Reviewer batch writes cannot run inside an ambient transaction.");

        var settings = new SqlConnectionStringBuilder(connectionString) { Enlist = false };
        var connection = new SqlConnection(settings.ConnectionString);
        try
        {
            await connection.OpenAsync();
            var transaction = (SqlTransaction)await connection.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
            return new ReviewerSqlBatch(connection, transaction);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private void EnsureWritable()
    {
        if (_disposed || _completionAttempted || _writeFailed)
            throw new InvalidOperationException("This batch is closed or completion has already been attempted. Do not retry it.");
    }

    public async Task<int> AddProductAsync(Product product)
    {
        EnsureWritable();
        try { return await _products.AddProductAsync(product, _connection, _transaction); }
        catch { _writeFailed = true; throw; }
    }

    public async Task<List<Product>> LoadCatalogueForMatchingAsync()
    {
        EnsureWritable();
        // Hold the catalogue key range until commit so concurrent Reviewer batches
        // see each other's new products before deciding whether to insert.
        try { return await ReadCatalogueAsync(_connection, _transaction, CancellationToken.None); }
        catch { _writeFailed = true; throw; }
    }

    public static async Task<List<Product>> ReadCatalogueAsync(string connectionString, CancellationToken token)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(token);
        return await ReadCatalogueAsync(connection, null, token);
    }

    private static async Task<List<Product>> ReadCatalogueAsync(SqlConnection connection, SqlTransaction? transaction,
        CancellationToken token)
    {
        const string columns = "SELECT ID, Name, Brand, Variant, PackSizeText, Barcode FROM dbo.Products";
        var sql = transaction is null ? columns + " ORDER BY ID;" : columns + " WITH (UPDLOCK, HOLDLOCK) ORDER BY ID;";
        await using var command = new SqlCommand(sql, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(token);
        var products = new List<Product>();
        while (await reader.ReadAsync(token))
            products.Add(new Product
            {
                ID = reader.GetInt32(0),
                Name = reader[1] as string ?? "",
                Brand = reader[2] as string,
                Variant = reader[3] as string,
                PackSizeText = reader[4] as string,
                Barcode = reader[5] as string
            });
        return products;
    }

    public async Task SaveProductCategoriesAsync(int productId, string? primaryCategory, IEnumerable<string>? categories)
    {
        EnsureWritable();
        try { await _products.SaveProductCategoriesAsync(productId, primaryCategory, categories, _connection, _transaction); }
        catch { _writeFailed = true; throw; }
    }

    public async Task<int> AddOfferAsync(Offer offer)
    {
        EnsureWritable();
        try { return await _offers.AddOfferAsync(offer, _connection, _transaction); }
        catch { _writeFailed = true; throw; }
    }

    public async Task<List<Offer>> LoadOffersForMatchingAsync(int productId, int retailerId)
    {
        EnsureWritable();
        try { return await ReadOffersAsync(_connection, _transaction, productId, retailerId, CancellationToken.None); }
        catch { _writeFailed = true; throw; }
    }

    public static async Task<List<Offer>> ReadOffersAsync(string connectionString, int productId, int retailerId,
        CancellationToken token)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(token);
        return await ReadOffersAsync(connection, null, productId, retailerId, token);
    }

    private static async Task<List<Offer>> ReadOffersAsync(SqlConnection connection, SqlTransaction? transaction,
        int productId, int retailerId, CancellationToken token)
    {
        const string columns = """
            SELECT ID, RetailerLocationID, AdvertisedPrice, RegularPrice, AdvertisedSaving, ValidFrom,
                   ValidTo, SourceDescription, FlyerPageNumber, PromoText, PromotionTypes, UnitPriceText,
                   MultiBuyQuantity, MultiBuyPrice, MemberPrice, MemberOnlyFlag
            FROM dbo.Offers
            """;
        var locking = transaction is null ? "" : " WITH (UPDLOCK, HOLDLOCK)";
        await using var command = new SqlCommand(columns + locking + " WHERE ProductID = @product AND RetailerID = @retailer;",
            connection, transaction);
        command.Parameters.Add("@product", System.Data.SqlDbType.Int).Value = productId;
        command.Parameters.Add("@retailer", System.Data.SqlDbType.Int).Value = retailerId;
        await using var reader = await command.ExecuteReaderAsync(token);
        var offers = new List<Offer>();
        while (await reader.ReadAsync(token))
        {
            offers.Add(new Offer
            {
                ID = Convert.ToInt32(reader[0]), ProductID = productId, RetailerID = retailerId,
                RetailerLocationID = reader[1] as int?, AdvertisedPrice = reader[2] as decimal?,
                RegularPrice = reader[3] as decimal?, AdvertisedSaving = reader[4] as decimal?,
                ValidFrom = reader[5] as DateTime?, ValidTo = reader[6] as DateTime?,
                SourceDescription = reader[7] as string, FlyerPageNumber = reader[8] as int?,
                PromoText = reader[9] as string, PromotionTypes = reader[10] as string,
                UnitPriceText = reader[11] as string, MultiBuyQuantity = reader[12] as int?,
                MultiBuyPrice = reader[13] as decimal?, MemberPrice = reader[14] as decimal?,
                MemberOnlyFlag = reader[15] as bool?
            });
        }
        return offers;
    }

    public async Task CommitAsync()
    {
        EnsureWritable();
        // A failure or lost acknowledgement must never trigger a second Commit.
        _completionAttempted = true;
        await _transaction.CommitAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            // Disposing an uncommitted transaction rolls back all batch inserts.
            await _transaction.DisposeAsync();
        }
        finally
        {
            await _connection.DisposeAsync();
        }
    }
}
