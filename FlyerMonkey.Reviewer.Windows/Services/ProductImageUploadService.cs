using Azure.Storage.Blobs;
using System.IO;
using Azure;

namespace FlyerMonkey.Reviewer.Windows.Services;

public class ProductImageUploadService
{
    private const string ContainerName = "product-images";

    private readonly BlobServiceClient _blobServiceClient;

    public ProductImageUploadService(string connectionString)
        : this(new BlobServiceClient(connectionString)) { }

    public ProductImageUploadService(BlobServiceClient blobServiceClient)
    {
        _blobServiceClient = blobServiceClient;
    }

    public async Task<string> UploadAsync(
        string localFilePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(localFilePath))
        {
            throw new ArgumentException(
                "Image file path is required.",
                nameof(localFilePath));
        }

        if (!File.Exists(localFilePath))
        {
            throw new FileNotFoundException(
                "Product image file was not found.",
                localFilePath);
        }

        var containerClient =
            _blobServiceClient.GetBlobContainerClient(ContainerName);

        var blobName =
            Path.GetFileName(localFilePath);

        var blobClient =
            containerClient.GetBlobClient(blobName);

        try
        {
            await blobClient.UploadAsync(
                localFilePath,
                overwrite: true,
                cancellationToken);
        }
        catch (RequestFailedException ex)
        {
            // Never expose raw Azure messages, URLs or credentials.
            var code = ex.ErrorCode is { Length: > 0 and <= 80 } value &&
                value.All(char.IsAsciiLetterOrDigit) ? value : "unavailable";
            throw new InvalidOperationException(
                $"Azure Blob upload to product-images failed: HTTP {(ex.Status > 0 ? ex.Status.ToString() : "unavailable")}; Azure error code {code}.");
        }
        catch (OperationCanceledException)
        {
            throw new InvalidOperationException("Azure Blob upload to product-images timed out or was canceled; HTTP status unavailable.");
        }

        return $"{ContainerName}/{blobName}";
    }
}
