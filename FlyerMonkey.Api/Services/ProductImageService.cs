using Azure.Storage.Blobs;

namespace FlyerMonkey.Api.Services;

public sealed class ProductImageService
{
    private const string ContainerName = "product-images";
    private readonly BlobServiceClient _blobServiceClient;

    public ProductImageService(IConfiguration configuration)
    {
        var connectionString =
            configuration.GetConnectionString("BlobStorage")
            ?? throw new InvalidOperationException(
                "BlobStorage connection string is not configured.");

        _blobServiceClient = new BlobServiceClient(connectionString);
    }

    internal ProductImageService(BlobServiceClient blobServiceClient)
    {
        _blobServiceClient = blobServiceClient;
    }

    public async Task<Stream?> GetImageAsync(
        string imageBlobPath,
        CancellationToken cancellationToken = default)
    {
        // Treat stored paths as untrusted too. Never select a container from data.
        // Reject URI/escape/traversal forms rather than normalizing them.
        if (string.IsNullOrWhiteSpace(imageBlobPath) ||
            !imageBlobPath.StartsWith(ContainerName + "/", StringComparison.Ordinal) ||
            imageBlobPath.Any(c => char.IsControl(c) || c is '\\' or '%' or '?' or '#' or ':'))
            return null;

        var blobName = imageBlobPath[(ContainerName.Length + 1)..];
        if (blobName.Split('/').Any(segment => string.IsNullOrWhiteSpace(segment) ||
            segment is "." or ".." || segment != segment.Trim()))
            return null;

        var containerClient =
            _blobServiceClient.GetBlobContainerClient(ContainerName);

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
