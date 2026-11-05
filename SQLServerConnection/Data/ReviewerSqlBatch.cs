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
