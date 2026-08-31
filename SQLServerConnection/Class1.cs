using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace SQLServerConnection
{
    public interface IProductService
    {
        Task<List<ProductDto>> GetProductsAsync(int top = 5, CancellationToken cancellationToken = default);
    }

    public class ProductDto
    {
        public int? ID { get; set; }
        public string? Name { get; set; }
        public Dictionary<string, object?> Fields { get; } = new Dictionary<string, object?>();
    }

    public class ProductService : IProductService
    {
        private readonly string _connectionString;

        public ProductService(string connectionString)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        public async Task<List<ProductDto>> GetProductsAsync(int top = 5, CancellationToken cancellationToken = default)
        {
            var results = new List<ProductDto>();

            const int maxAttempts = 3;
            var baseDelay = TimeSpan.FromSeconds(2);
            const int commandTimeoutSeconds = 60; // per-command timeout

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    await using var conn = new SqlConnection(_connectionString);
                    await conn.OpenAsync(cancellationToken);

                    // Use inline TOP with a trusted integer to avoid parameter issues
                    var cmdText = $"SELECT TOP ({top}) * FROM [dbo].[Table_1];";
                    await using var cmd = new SqlCommand(cmdText, conn)
                    {
                        CommandTimeout = commandTimeoutSeconds
                    };

                    await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        var dto = new ProductDto();
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            var name = reader.GetName(i);
                            var val = reader.GetValue(i);
                            var safeVal = val is DBNull ? null : val;
                            dto.Fields[name] = safeVal;

                            if (string.Equals(name, "ID", StringComparison.OrdinalIgnoreCase) && safeVal != null)
                            {
                                if (int.TryParse(safeVal.ToString(), out var id)) dto.ID = id;
                            }

                            if (string.Equals(name, "Name", StringComparison.OrdinalIgnoreCase) && safeVal != null)
                            {
                                dto.Name = safeVal.ToString();
                            }
                        }

                        results.Add(dto);
                    }

                    return results;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // Propagate cancellation
                    throw;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"GetProductsAsync attempt {attempt} failed: {ex.Message}");
                    if (attempt == maxAttempts)
                    {
                        Console.WriteLine("Max retry attempts reached. Rethrowing.");
                        throw;
                    }

                    // Exponential backoff before retrying
                    var delay = TimeSpan.FromSeconds(baseDelay.TotalSeconds * Math.Pow(2, attempt - 1));
                    try
                    {
                        await Task.Delay(delay, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                }
            }

            return results;
        }
    }
}
