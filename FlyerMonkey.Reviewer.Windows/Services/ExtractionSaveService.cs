using FlyerMonkey.Reviewer.Windows.Models;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace FlyerMonkey.Reviewer.Windows.Services;

public class ExtractionSaveService
{
    private readonly string _connectionString;

    public ExtractionSaveService(string databasePath)
    {
        _connectionString =
            $"Data Source={databasePath}";
    }

    public async Task SaveAsync(
        FlyerFile flyer,
        FlyerPage page,
        IEnumerable<ExtractedProduct> products)
    {
        var productList = products.ToList();

        var json = JsonSerializer.Serialize(
            productList,
            new JsonSerializerOptions
            {
                WriteIndented = true
            });

        using var connection =
            new SqliteConnection(_connectionString);

        await connection.OpenAsync();

        var createTable = connection.CreateCommand();

        createTable.CommandText = """
        CREATE TABLE IF NOT EXISTS ExtractionRuns
        (
            ID              INTEGER PRIMARY KEY AUTOINCREMENT,
            Retailer        TEXT,
            FlyerFileName   TEXT,
            PageFileName    TEXT,
            PageNumber      INTEGER,
            ProductCount    INTEGER,
            ExtractedJson   TEXT NOT NULL,
            SavedUtc        TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
        );
        """;

        await createTable.ExecuteNonQueryAsync();

        var insert = connection.CreateCommand();

        insert.CommandText = """
        INSERT INTO ExtractionRuns
        (
            Retailer,
            FlyerFileName,
            PageFileName,
            PageNumber,
            ProductCount,
            ExtractedJson
        )
        VALUES
        (
            $retailer,
            $flyerFileName,
            $pageFileName,
            $pageNumber,
            $productCount,
            $json
        );
        """;

        insert.Parameters.AddWithValue(
            "$retailer",
            flyer.Retailer);

        insert.Parameters.AddWithValue(
            "$flyerFileName",
            flyer.FileName);

        insert.Parameters.AddWithValue(
            "$pageFileName",
            page.FileName);

        insert.Parameters.AddWithValue(
            "$pageNumber",
            page.PageNumber);

        insert.Parameters.AddWithValue(
            "$productCount",
            productList.Count);

        insert.Parameters.AddWithValue(
            "$json",
            json);

        await insert.ExecuteNonQueryAsync();
    }
}