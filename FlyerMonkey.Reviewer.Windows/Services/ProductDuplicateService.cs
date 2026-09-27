using Microsoft.Data.Sqlite;
using FlyerMonkey.Reviewer.Windows.Models;
using System.Text.Json;
public sealed class ProductDuplicateService
{
    private readonly string _connectionString;

    private static string ProductKey(ExtractedProduct product)
    {
        return $"{product.Brand}|{product.ProductName}|{product.PackSizeText}"
            .Replace(",", "")
            .Trim()
            .ToUpperInvariant();
    }
    public ProductDuplicateService(string databasePath)
    {
        _connectionString = $"Data Source={databasePath}";
    }

    public async Task CheckAsync(
    List<ExtractedProduct> products)
    {
        using var connection =
            new SqliteConnection(_connectionString);

        await connection.OpenAsync();

        var command = connection.CreateCommand();

        command.CommandText = """
        SELECT ExtractedJson
        FROM ExtractionRuns
        WHERE ExtractedJson IS NOT NULL
          AND ExtractedJson <> '';
        """;

        using var reader =
            await command.ExecuteReaderAsync();

        var previousProductKeys = new HashSet<string>();

        while (await reader.ReadAsync())
        {
            var extractedJson = reader.GetString(0);

            var previousProducts =
                JsonSerializer.Deserialize<List<ExtractedProduct>>(
                    extractedJson,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

            if (previousProducts == null)
                continue;

            foreach (var previousProduct in previousProducts)
            {
                previousProductKeys.Add(
                    ProductKey(previousProduct));
            }
        }
        foreach (var product in products)
        {
            product.PossibleDuplicate =
                previousProductKeys.Contains(
                    ProductKey(product));
        }
    }
}