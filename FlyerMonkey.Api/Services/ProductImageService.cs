using Azure.Storage.Blobs;

namespace FlyerMonkey.Api.Services;

public sealed class ProductImageService
{
    private readonly BlobServiceClient _blobServiceClient;

    public ProductImageService(IConfiguration configuration)
    {
        var connectionString =
            configuration.GetConnectionString("BlobStorage")
            ?? throw new InvalidOperationException(
                "BlobStorage connection string is not configured.");

        _blobServiceClient = new BlobServiceClient(connectionString);
    }

    public async Task<Stream?> GetImageAsync(
        string imageBlobPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imageBlobPath))
            return null;

        var parts = imageBlobPath.Split(
            '/',
            2,
            StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length != 2)
            return null;

        var containerName = parts[0];
        var blobName = parts[1];

        var containerClient =
            _blobServiceClient.GetBlobContainerClient(containerName);

        var blobClient =
            containerClient.GetBlobClient(blobName);

        if (!await blobClient.ExistsAsync(cancellationToken))
            return null;

        var response =
            await blobClient.DownloadStreamingAsync(
                cancellationToken: cancellationToken);

        return response.Value.Content;
    }
}