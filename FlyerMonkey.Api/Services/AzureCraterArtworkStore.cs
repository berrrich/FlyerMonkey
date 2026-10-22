using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using FlyerMonkey.Shared.Model;

namespace FlyerMonkey.Api.Services;

/// <summary>Private, development-only Blob storage. One API process per container is required.</summary>
public sealed class AzureCraterArtworkStore(
    BlobContainerClient container, ILogger<AzureCraterArtworkStore> logger) : ICraterArtworkStore
{
    private static string ImageName(Guid id) => $"artwork/{id:N}/artwork.jpg";
    private static string ManifestName(Guid id) => $"artwork/{id:N}/metadata.json";

    private static bool Valid(CraterArtwork item, Guid id) =>
        id != Guid.Empty && item.ArtworkId == id && item.RequestId != Guid.Empty && item.ProductID > 0 &&
        !string.IsNullOrWhiteSpace(item.ProductName) && item.ProductName.Length <= 1000 &&
        item.SchemaVersion == 1 && item.ContentType == "image/jpeg" && item.Filename == "artwork.jpg" &&
        Enum.IsDefined(item.Preset) && !string.IsNullOrWhiteSpace(item.PresetName) &&
        item.CreatedUtc.Offset == TimeSpan.Zero && item.RequestFingerprint is { Length: 64 } fingerprint &&
        fingerprint.All(Uri.IsHexDigit);

    private async Task CheckContainerAsync(CancellationToken token)
    {
        if (container.Name != "crater-artwork") throw new IOException("Only the crater-artwork container is permitted.");
        // Read only: never create a container, change access policies, or inspect another container.
        var properties = await container.GetPropertiesAsync(cancellationToken: token);
        if (properties.Value.PublicAccess != PublicAccessType.None)
            throw new IOException("Crater artwork requires a private container.");
    }

    private static bool MissingBlob(RequestFailedException ex) => ex.Status == 404 && ex.ErrorCode == "BlobNotFound";
    private static bool AlreadyExists(RequestFailedException ex) =>
        ex.Status == 409 && ex.ErrorCode == "BlobAlreadyExists" ||
        ex.Status == 412 && ex.ErrorCode == "ConditionNotMet";

    private async Task<byte[]?> DownloadAsync(string name, CancellationToken token)
    {
        try { return (await container.GetBlobClient(name).DownloadContentAsync(token)).Value.Content.ToArray(); }
        catch (RequestFailedException ex) when (MissingBlob(ex)) { return null; }
    }

    private async Task<bool> ImageExistsAsync(Guid id, CancellationToken token)
    {
        try
        {
            await container.GetBlobClient(ImageName(id)).GetPropertiesAsync(cancellationToken: token);
            return true;
        }
        catch (RequestFailedException ex) when (MissingBlob(ex)) { return false; }
    }

    private async Task<CraterArtwork?> ManifestAsync(Guid id, CancellationToken token)
    {
        var bytes = await DownloadAsync(ManifestName(id), token);
        if (bytes is null) return null;
        try
        {
            var item = JsonSerializer.Deserialize<CraterArtwork>(bytes);
            if (item is not null && Valid(item, id)) return item;
        }
        catch (JsonException) { }
        logger.LogWarning("Invalid Crater artwork manifest {ArtworkId}", id);
        return null;
    }

    public Task<CraterArtwork?> GetMetadataAsync(Guid artworkId, CancellationToken token) =>
        artworkId == Guid.Empty ? Task.FromResult<CraterArtwork?>(null) :
            WithContainerAsync(() => ManifestAsync(artworkId, token), token);

    private async Task<List<CraterArtwork>> ManifestsAsync(CancellationToken token)
    {
        var items = new List<CraterArtwork>();
        await foreach (var blob in container.GetBlobsAsync(BlobTraits.None, BlobStates.None, "artwork/", token))
        {
            var parts = blob.Name.Split('/');
            if (parts.Length == 3 && parts[0] == "artwork" && parts[2] == "metadata.json" &&
                Guid.TryParseExact(parts[1], "N", out var id) && id != Guid.Empty &&
                await ManifestAsync(id, token) is { } item) items.Add(item);
        }
        return items;
    }

    // Translate SDK failures into the existing API's storage-failure contract. Never return empty history on outages.
    private async Task<T> WithContainerAsync<T>(Func<Task<T>> work, CancellationToken token)
    {
        try
        {
            await CheckContainerAsync(token);
            return await work();
        }
        catch (RequestFailedException ex)
        {
            throw new IOException($"Azure Crater artwork operation failed (HTTP {ex.Status}).", ex);
        }
    }

    public Task<CraterArtwork?> FindRequestAsync(Guid requestId, CancellationToken token)
    {
        if (requestId == Guid.Empty) throw new ArgumentException("Request ID must be nonempty.", nameof(requestId));
        return WithContainerAsync(async () =>
        {
            // Include committed manifests whose JPEG has disappeared: a replay must not regenerate them.
            var matches = (await ManifestsAsync(token)).Where(a => a.RequestId == requestId).ToArray();
            if (matches.Length > 1) throw new IOException("Duplicate Crater request records require investigation.");
            return matches.SingleOrDefault();
        }, token);
    }

    public Task<IReadOnlyList<CraterArtwork>> ListAsync(int productId, CancellationToken token)
    {
        if (productId <= 0) throw new ArgumentOutOfRangeException(nameof(productId));
        return WithContainerAsync<IReadOnlyList<CraterArtwork>>(async () =>
        {
            var items = new List<CraterArtwork>();
            foreach (var item in (await ManifestsAsync(token)).Where(a => a.ProductID == productId))
                if (await ImageExistsAsync(item.ArtworkId, token)) items.Add(item);
            return items.OrderByDescending(a => a.CreatedUtc).ThenBy(a => a.ArtworkId).ToArray();
        }, token);
    }

    public Task<byte[]?> ReadAsync(Guid artworkId, CancellationToken token)
    {
        if (artworkId == Guid.Empty) return Task.FromResult<byte[]?>(null);
        return WithContainerAsync(async () => await ManifestAsync(artworkId, token) is null
            ? null : await DownloadAsync(ImageName(artworkId), token), token);
    }

    private async Task CreateAsync(string name, byte[] bytes, string contentType, CancellationToken token)
    {
        try
        {
            await container.GetBlobClient(name).UploadAsync(BinaryData.FromBytes(bytes), new BlobUploadOptions
            {
                Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All },
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType, CacheControl = "no-store" }
            }, token);
        }
        catch (RequestFailedException ex) when (AlreadyExists(ex))
        {
            var existing = await DownloadAsync(name, token);
            if (existing is null || !existing.AsSpan().SequenceEqual(bytes))
                throw new IOException("Existing Crater artwork differs; overwrite refused.", ex);
        }
    }

    public async Task SaveAsync(CraterArtwork artwork, byte[] jpeg, CancellationToken token)
    {
        if (!Valid(artwork, artwork.ArtworkId) || jpeg.Length < 3 || jpeg[0] != 255 || jpeg[1] != 216 || jpeg[2] != 255)
            throw new InvalidDataException("Invalid artwork.");
        await WithContainerAsync(async () =>
        {
            var existing = (await ManifestsAsync(token)).Where(a => a.RequestId == artwork.RequestId).ToArray();
            if (existing.Length > 1 || existing.Length == 1 && existing[0] != artwork)
                throw new IOException("Request ID already belongs to another artwork record.");
            if (existing.Length == 1)
            {
                var bytes = await DownloadAsync(ImageName(artwork.ArtworkId), token);
                if (bytes is null || !bytes.AsSpan().SequenceEqual(jpeg))
                    throw new IOException("Previously committed artwork is missing or differs.");
                return true;
            }
            await CreateAsync(ImageName(artwork.ArtworkId), jpeg, "image/jpeg", token);
            // This manifest is the completion marker. Failed/ambiguous uploads leave bytes for later inspection.
            await CreateAsync(ManifestName(artwork.ArtworkId), JsonSerializer.SerializeToUtf8Bytes(artwork), "application/json", token);
            return true;
        }, token);
    }
}
