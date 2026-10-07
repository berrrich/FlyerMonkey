using Azure.Storage.Blobs;
using System.IO;

namespace FlyerMonkey.Reviewer.Windows.Services;

public class ProductImageUploadService
{
    private const string ContainerName = "product-images";

    private readonly BlobServiceClient _blobServiceClient;

    public ProductImageUploadService(string connectionString)
    {
        _blobServiceClient =
            new BlobServiceClient(connectionString);
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

        await blobClient.UploadAsync(
            localFilePath,
            overwrite: true,
            cancellationToken);

        return $"{ContainerName}/{blobName}";
    }
}