using Microsoft.Data.SqlClient;

namespace SQLServerConnection.Data;

public sealed class RetailerRepository : IRetailerRepository
{
    private readonly string _connectionString;

    public RetailerRepository(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException(
                "A database connection string is required.",
                nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    public async Task<int?> GetRetailerIdByNameAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT ID
            FROM Retailers
            WHERE Name = @Name;
            """;

        await using var connection =
            await OpenConnectionWithRetryAsync(cancellationToken);

        await using var command =
            new SqlCommand(sql, connection);

        command.Parameters.AddWithValue(
            "@Name",
            name);

        var result =
            await command.ExecuteScalarAsync(cancellationToken);

        if (result == null || result == DBNull.Value)
        {
            return null;
        }

        return Convert.ToInt32(result);
    }

    private async Task<SqlConnection> OpenConnectionWithRetryAsync(
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        SqlException? lastException = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var connection =
                new SqlConnection(_connectionString);

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

                var delay =
                    TimeSpan.FromSeconds(
                        Math.Pow(2, attempt));

                Console.WriteLine(
                    $"SQL connection attempt {attempt} failed. " +
                    $"Retrying in {delay.TotalSeconds:0} seconds.");

                await Task.Delay(
                    delay,
                    cancellationToken);
            }
        }

        throw new InvalidOperationException(
            $"Unable to connect to SQL Server after {maxAttempts} attempts.",
            lastException);
    }
}