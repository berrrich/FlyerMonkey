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
        foreach (var product in productList)
        {
            ProductValidationService.SanitizeProduct(product);
        }
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
            ValidFrom     TEXT,
            ValidTo       TEXT,
            Status        TEXT NOT NULL DEFAULT 'Saved',
            CommittedUtc  TEXT,
            SavedUtc        TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
        );
        """;

        await createTable.ExecuteNonQueryAsync();

        // Update an existing uncommitted extraction,
        // or insert a new extraction if none exists.

        var save = connection.CreateCommand();

        save.CommandText = """
    UPDATE ExtractionRuns
    SET
        ProductCount = $productCount,
        ExtractedJson = $json,
        SavedUtc = CURRENT_TIMESTAMP
    WHERE ID = (
        SELECT ID
        FROM ExtractionRuns
        WHERE Retailer = $retailer
          AND FlyerFileName = $flyerFileName
          AND PageFileName = $pageFileName
          AND Status = 'Saved'
        ORDER BY ID DESC
        LIMIT 1
    );
    """;

        save.Parameters.AddWithValue("$retailer", flyer.Retailer);
        save.Parameters.AddWithValue("$flyerFileName", flyer.FileName);
        save.Parameters.AddWithValue("$pageFileName", page.FileName);
        save.Parameters.AddWithValue("$productCount", productList.Count);
        save.Parameters.AddWithValue("$json", json);

        var rowsUpdated = await save.ExecuteNonQueryAsync();

        if (rowsUpdated == 0)
        {
            // Do not silently create a second batch
            // for a page already committed to SQL.
            var check = connection.CreateCommand();

            check.CommandText = """
        SELECT COUNT(*)
        FROM ExtractionRuns
        WHERE Retailer = $retailer
          AND FlyerFileName = $flyerFileName
          AND PageFileName = $pageFileName
          AND Status = 'Committed';
        """;

            check.Parameters.AddWithValue("$retailer", flyer.Retailer);
            check.Parameters.AddWithValue("$flyerFileName", flyer.FileName);
            check.Parameters.AddWithValue("$pageFileName", page.FileName);

            var committedCount = Convert.ToInt32(
                await check.ExecuteScalarAsync());

            if (committedCount > 0)
            {
                throw new InvalidOperationException(
                    "This page has already been committed. " +
                    "Use the correction workflow to change committed data.");
            }

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

            insert.Parameters.AddWithValue("$retailer", flyer.Retailer);
            insert.Parameters.AddWithValue("$flyerFileName", flyer.FileName);
            insert.Parameters.AddWithValue("$pageFileName", page.FileName);
            insert.Parameters.AddWithValue("$pageNumber", page.PageNumber);
            insert.Parameters.AddWithValue("$productCount", productList.Count);
            insert.Parameters.AddWithValue("$json", json);

            await insert.ExecuteNonQueryAsync();

        } // Closes if (rowsUpdated == 0)

    } // Closes SaveAsync()

    public async Task UpdateSavedExtractionAsync(
    long extractionId,
    IEnumerable<ExtractedProduct> products)
    {
        var productList = products.ToList();

        foreach (var product in productList)
        {
            ProductValidationService.SanitizeProduct(product);
        }

        var json = JsonSerializer.Serialize(
            productList,
            new JsonSerializerOptions
            {
                WriteIndented = true
            });

        using var connection =
            new SqliteConnection(_connectionString);

        await connection.OpenAsync();

        using var command = connection.CreateCommand();

        command.CommandText = """
        UPDATE ExtractionRuns
        SET
            ProductCount = $productCount,
            ExtractedJson = $json,
            SavedUtc = CURRENT_TIMESTAMP
        WHERE ID = $extractionId
          AND Status = 'Saved';
        """;

        command.Parameters.AddWithValue(
            "$extractionId", extractionId);

        command.Parameters.AddWithValue(
            "$productCount", productList.Count);

        command.Parameters.AddWithValue(
            "$json", json);

        var rowsUpdated =
            await command.ExecuteNonQueryAsync();

        if (rowsUpdated != 1)
        {
            throw new InvalidOperationException(
                "The saved extraction could not be updated. " +
                "It may already have been committed or deleted.");
        }
    }

    public async Task UpdateProductsAsync(
    int extractionRunId,
    IEnumerable<ExtractedProduct> products)
    {
        var productList = products.ToList();

        foreach (var product in productList)
        {
            ProductValidationService.SanitizeProduct(product);
        }

        var json = JsonSerializer.Serialize(
            productList,
            new JsonSerializerOptions
            {
                WriteIndented = true
            });

        using var connection =
            new SqliteConnection(_connectionString);

        await connection.OpenAsync();

        var command = connection.CreateCommand();

        command.CommandText = """
        UPDATE ExtractionRuns
        SET
            ProductCount = $productCount,
            ExtractedJson = $json
        WHERE ID = $id
          AND Status = 'Saved';
        """;

        command.Parameters.AddWithValue(
            "$productCount",
            productList.Count);

        command.Parameters.AddWithValue(
            "$json",
            json);

        command.Parameters.AddWithValue(
            "$id",
            extractionRunId);

        await command.ExecuteNonQueryAsync();
    }
    public async Task MarkCommittedAsync(int extractionRunId)
    {
        using var connection =
            new SqliteConnection(_connectionString);

        await connection.OpenAsync();

        var command = connection.CreateCommand();

        command.CommandText = """
        UPDATE ExtractionRuns
        SET
            Status = 'Committed',
            CommittedUtc = CURRENT_TIMESTAMP
        WHERE ID = $id
          AND Status = 'Saved';
        """;

        command.Parameters.AddWithValue(
            "$id",
            extractionRunId);

        await command.ExecuteNonQueryAsync();
    }
}