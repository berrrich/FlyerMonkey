using Microsoft.Data.Sqlite;
using FlyerMonkey.Reviewer.Windows.Models;
using System.Text.Json;

namespace FlyerMonkey.Reviewer.Windows.Services;

public sealed class ExtractionReadService
{
    private readonly string _connectionString;

    public ExtractionReadService(string databasePath)
    {
        _connectionString = $"Data Source={databasePath}";
    }

    public async Task<List<SavedExtraction>> GetSavedAsync()
    {
        var results = new List<SavedExtraction>();

        using var connection =
            new SqliteConnection(_connectionString);

        await connection.OpenAsync();

        var command = connection.CreateCommand();

        command.CommandText = """
            SELECT
                ID,
                Retailer,
                FlyerFileName,
                PageFileName,
                PageNumber,
                ProductCount,
                SavedUtc
            FROM ExtractionRuns
            WHERE Status = 'Saved'
            ORDER BY ID DESC;
            """;

        using var reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            results.Add(new SavedExtraction
            {
                Id = reader.GetInt64(0),
                Retailer = reader.IsDBNull(1) ? "" : reader.GetString(1),
                FlyerFileName = reader.IsDBNull(2) ? "" : reader.GetString(2),
                PageFileName = reader.IsDBNull(3) ? "" : reader.GetString(3),
                PageNumber = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                ProductCount = reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
                SavedUtc = reader.IsDBNull(6) ? "" : reader.GetString(6)
            });

        }

        return results;
    }

    public async Task<List<SavedExtraction>> GetAllAsync()
    {
        var results = new List<SavedExtraction>();

        using var connection =
            new SqliteConnection(_connectionString);

        await connection.OpenAsync();

        var command = connection.CreateCommand();

        command.CommandText = """
    SELECT
        ID,
        Retailer,
        FlyerFileName,
        PageFileName,
        PageNumber,
        ProductCount,
        SavedUtc,
        Status
    FROM ExtractionRuns
    WHERE Status IN ('Saved', 'Committing', 'Committed')
    ORDER BY ID DESC;
    """;

        using var reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            results.Add(new SavedExtraction
            {
                Id = reader.GetInt64(0),
                Retailer = reader.IsDBNull(1) ? "" : reader.GetString(1),
                FlyerFileName = reader.IsDBNull(2) ? "" : reader.GetString(2),
                PageFileName = reader.IsDBNull(3) ? "" : reader.GetString(3),
                PageNumber = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                ProductCount = reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
                SavedUtc = reader.IsDBNull(6) ? "" : reader.GetString(6),
                Status = reader.IsDBNull(7)
    ? "Saved"
    : reader.GetString(7)
            });

        }

        return results;
    }
    public async Task<List<ExtractedProduct>?> GetProductsAsync(
    string flyerFileName,
    string pageFileName)
    {
        using var connection =
            new SqliteConnection(_connectionString);

        await connection.OpenAsync();

        var command = connection.CreateCommand();

        command.CommandText = """
        SELECT ExtractedJson
        FROM ExtractionRuns
        WHERE FlyerFileName = $flyerFileName
          AND PageFileName = $pageFileName
          AND Status = 'Saved'
        ORDER BY ID DESC
        LIMIT 1;
        """;

        command.Parameters.AddWithValue(
            "$flyerFileName",
            flyerFileName);

        command.Parameters.AddWithValue(
            "$pageFileName",
            pageFileName);

        var result =
            await command.ExecuteScalarAsync();

        if (result is not string json ||
            string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        return JsonSerializer.Deserialize<List<ExtractedProduct>>(json);
    }

    public async Task<(string From, string To)?> GetFlyerDatesAsync(string retailer, string flyerFileName)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT DISTINCT json_extract(card.value, '$.FlyerValidFrom'), json_extract(card.value, '$.FlyerValidTo')
            FROM ExtractionRuns run, json_each(run.ExtractedJson) card
            WHERE run.Retailer = $retailer AND run.FlyerFileName = $flyer
              AND run.Status IN ('Saved', 'Committed')
              AND json_valid(run.ExtractedJson)
            """;
        command.Parameters.AddWithValue("$retailer", retailer);
        command.Parameters.AddWithValue("$flyer", flyerFileName);
        await using var reader = await command.ExecuteReaderAsync();
        (string From, string To)? result = null;
        while (await reader.ReadAsync())
        {
            if (!ReviewerOffer.TryDates(reader[0] as string, reader[1] as string, out var from, out var to)) continue;
            if (result.HasValue) return null;
            result = (from.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                to.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        }
        return result;
    }
    public async Task<List<SavedExtraction>> GetExtractionStatesAsync()
    {
        var results = new List<SavedExtraction>();

        using var connection =
            new SqliteConnection(_connectionString);

        await connection.OpenAsync();

        var command = connection.CreateCommand();

        command.CommandText = """
        SELECT
            ID,
            Retailer,
            FlyerFileName,
            PageFileName,
            PageNumber,
            ProductCount,
            SavedUtc,
            Status
        FROM ExtractionRuns
        ORDER BY ID DESC;
        """;

        using var reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            results.Add(new SavedExtraction
            {
                Id = reader.GetInt64(0),
                Retailer = reader.IsDBNull(1) ? "" : reader.GetString(1),
                FlyerFileName = reader.IsDBNull(2) ? "" : reader.GetString(2),
                PageFileName = reader.IsDBNull(3) ? "" : reader.GetString(3),
                PageNumber = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                ProductCount = reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
                SavedUtc = reader.IsDBNull(6) ? "" : reader.GetString(6),
                Status = reader.IsDBNull(7) ? "" : reader.GetString(7)
            });
        }

        return results;
    }
}
