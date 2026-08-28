using Microsoft.Data.Sqlite;

namespace FlyerMonkey.Reviewer.Windows.Services;

public sealed class SavedExtraction
{
    public long Id { get; set; }
    public string Retailer { get; set; } = "";
    public string FlyerFileName { get; set; } = "";
    public string PageFileName { get; set; } = "";
    public int PageNumber { get; set; }
    public int ProductCount { get; set; }
    public string SavedUtc { get; set; } = "";
}

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
}