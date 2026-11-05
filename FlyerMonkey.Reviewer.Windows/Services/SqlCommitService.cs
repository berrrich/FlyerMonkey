using FlyerMonkey.Reviewer.Windows.Models;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace FlyerMonkey.Reviewer.Windows.Services;

public class SqlCommitService
{
    private readonly string _sqliteConnectionString;

    public SqlCommitService(string sqlitePath)
    {
        _sqliteConnectionString =
            $"Data Source={sqlitePath}";
    }

    public async Task<List<ExtractedProduct>> LoadProductsAsync(
        SavedExtraction saved)
    {
        using var connection =
            new SqliteConnection(_sqliteConnectionString);

        await connection.OpenAsync();

        var command = connection.CreateCommand();

        command.CommandText = """
            SELECT ExtractedJson
            FROM ExtractionRuns
            WHERE ID = $id;
            """;

        command.Parameters.AddWithValue(
            "$id",
            saved.Id);

        var json =
            await command.ExecuteScalarAsync() as string;

        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException(
                "Saved extraction JSON was not found.");
        }

        var products =
            JsonSerializer.Deserialize<List<ExtractedProduct>>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

        return products ?? new List<ExtractedProduct>();
    }
    public async Task ClaimAsync(long id)
    {
        using var connection = new SqliteConnection(_sqliteConnectionString);
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE ExtractionRuns SET Status = 'Committing' WHERE ID = $id AND Status = 'Saved' AND NOT EXISTS (SELECT 1 FROM ExtractionRuns other WHERE other.ID <> $id AND other.Retailer = ExtractionRuns.Retailer AND other.FlyerFileName = ExtractionRuns.FlyerFileName AND other.PageFileName = ExtractionRuns.PageFileName AND other.Status <> 'Saved');";
        command.Parameters.AddWithValue("$id", id);
        if (await command.ExecuteNonQueryAsync() != 1) throw new InvalidOperationException("Batch already claimed or committed. Reconcile its outcome before retrying.");
    }
    public async Task MarkCommittedAsync(long extractionRunId)
    {
        using var connection =
            new SqliteConnection(_sqliteConnectionString);

        await connection.OpenAsync();

        var command = connection.CreateCommand();

        command.CommandText = """
        UPDATE ExtractionRuns
        SET
            Status = 'Committed',
            CommittedUtc = CURRENT_TIMESTAMP
        WHERE ID = $id
          AND Status = 'Committing';
        """;

        command.Parameters.AddWithValue(
            "$id",
            extractionRunId);

        if (await command.ExecuteNonQueryAsync() != 1) throw new InvalidOperationException("SQL succeeded but local acknowledgement failed. Reconciliation required; do not retry.");
    }
}