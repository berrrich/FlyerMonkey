using FlyerMonkey.Shared.Model;
using Microsoft.Data.SqlClient;
using System.Data;

namespace SQLServerConnection.Data;

public sealed class ProductRepository : IProductRepository
{
    private readonly string _connectionString;
    public async Task<int> AddProductAsync(
    Product product,
    CancellationToken cancellationToken = default)
    {
        const string sql = """
        INSERT INTO Products (Name)
        OUTPUT INSERTED.ID
        VALUES (@Name);
        """;

        await using var connection =
            await OpenConnectionWithRetryAsync(cancellationToken);

        await using var command =
            new SqlCommand(sql, connection)
            {
                CommandType = CommandType.Text
            };

        command.Parameters.AddWithValue(
            "@Name",
            product.Name);

        var result =
            await command.ExecuteScalarAsync(cancellationToken);

        return Convert.ToInt32(result);
    }
    public ProductRepository(string connectionString)
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

    public async Task<List<Product>> GetProductsAsync(
        CancellationToken cancellationToken = default)
    {
        var products = new List<Product>();

        const string sql = """
    SELECT
        ID,
        Name,
        Brand,
        Variant,
        PackSizeText,
        Barcode,
        Category,
        ImageBlobPath
    FROM Products
    ORDER BY Name
    """;

        await using var connection =
            await OpenConnectionWithRetryAsync(cancellationToken);

        await using var command =
            new SqlCommand(sql, connection)
            {
                CommandType = CommandType.Text
            };

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            products.Add(new Product
            {
                ID = reader["ID"] == DBNull.Value
        ? 0
        : Convert.ToInt32(reader["ID"]),

                Name = reader["Name"] == DBNull.Value
        ? string.Empty
        : reader["Name"]?.ToString() ?? string.Empty,

                Brand = reader["Brand"] == DBNull.Value
        ? null
        : reader["Brand"]?.ToString(),

                Variant = reader["Variant"] == DBNull.Value
        ? null
        : reader["Variant"]?.ToString(),

                PackSizeText = reader["PackSizeText"] == DBNull.Value
        ? null
        : reader["PackSizeText"]?.ToString(),

                Barcode = reader["Barcode"] == DBNull.Value
        ? null
        : reader["Barcode"]?.ToString(),

                Category = reader["Category"] == DBNull.Value
        ? null
        : reader["Category"]?.ToString(),

                ImageBlobPath = reader["ImageBlobPath"] == DBNull.Value
        ? null
        : reader["ImageBlobPath"]?.ToString()
            });
        }

        return products;
    }
}