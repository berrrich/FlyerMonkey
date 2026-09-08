using Microsoft.Data.SqlClient;

namespace SQLServerConnection.Data;

public sealed class RetailerLocationRepository
    : IRetailerLocationRepository
{
    private readonly string _connectionString;

    public RetailerLocationRepository(string connectionString)
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
                await connection.OpenAsync(cancellationToken);
                return connection;
            }
            catch (SqlException ex)
            {
                lastException = ex;
                await connection.DisposeAsync();

                if (attempt == maxAttempts)
                    break;

                var delay =
                    TimeSpan.FromSeconds(Math.Pow(2, attempt));

                await Task.Delay(delay, cancellationToken);
            }
        }

        throw new InvalidOperationException(
            $"Unable to connect to SQL Server after {maxAttempts} attempts.",
            lastException);
    }

    public async Task<int?> GetLocationIdAsync(
        int retailerId,
        string suburb,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT TOP (1) ID
            FROM dbo.RetailerLocations
            WHERE RetailerID = @RetailerID
              AND Suburb = @Suburb
              AND IsActive = 1;
            """;

        await using var connection =
            await OpenConnectionWithRetryAsync(cancellationToken);

        await using var command =
            new SqlCommand(sql, connection);

        command.Parameters.AddWithValue(
            "@RetailerID",
            retailerId);

        command.Parameters.AddWithValue(
            "@Suburb",
            suburb);

        var result =
            await command.ExecuteScalarAsync(cancellationToken);

        if (result == null || result == DBNull.Value)
            return null;

        return Convert.ToInt32(result);
    }
}