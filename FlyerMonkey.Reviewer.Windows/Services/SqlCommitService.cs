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
}