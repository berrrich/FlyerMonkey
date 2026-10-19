extern alias CraterAzureIdentity;
using System.Security.Cryptography;
using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using FlyerMonkey.Api.Models;
using FlyerMonkey.Shared.Model;

namespace FlyerMonkey.Api.Services;

// Independent of the Development store: exact-path reads only, no listing, save or generation.
public sealed class ApprovedArtworkReader(BlobContainerClient container, ILogger<ApprovedArtworkReader>? logger = null)
{
    public static BlobContainerClient CreateContainer(string? serviceUri, bool managedIdentityOnly)
    {
        if (!Uri.TryCreate(serviceUri, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            !uri.Host.EndsWith(".blob.core.windows.net", StringComparison.OrdinalIgnoreCase) ||
            uri.AbsolutePath != "/" || !uri.IsDefaultPort || uri.Query != "" || uri.Fragment != "" || uri.UserInfo != "")
            throw new InvalidOperationException("Approved artwork requires an Azure HTTPS account root without credentials.");
        Azure.Core.TokenCredential credential = managedIdentityOnly
            ? new CraterAzureIdentity::Azure.Identity.ManagedIdentityCredential()
            : new CraterAzureIdentity::Azure.Identity.DefaultAzureCredential(new CraterAzureIdentity::Azure.Identity.DefaultAzureCredentialOptions
            {
                ExcludeEnvironmentCredential = true,
                ExcludeWorkloadIdentityCredential = true,
                ExcludeManagedIdentityCredential = true,
                ExcludeInteractiveBrowserCredential = true,
                ExcludeSharedTokenCacheCredential = true
            });
        var options = new BlobClientOptions();
        options.Retry.MaxRetries = 0;
        return new BlobServiceClient(uri, credential, options).GetBlobContainerClient("crater-artwork");
    }

    public async Task<byte[]?> ReadAsync(ApprovedArtwork approval, CancellationToken token)
    {
        new ApprovedArtworkManifest(1, [approval]).Validate();
        var stage = "container-check";
        try
        {
            if (container.Name != "crater-artwork" ||
                (await container.GetPropertiesAsync(cancellationToken: token)).Value.PublicAccess != PublicAccessType.None)
                throw new InvalidDataException("Approved artwork requires the private crater-artwork container.");
            var prefix = $"artwork/{approval.ArtworkId:N}/";
            stage = "metadata-read";
            var metadata = JsonSerializer.Deserialize<CraterArtwork>(await DownloadAsync(prefix + "metadata.json", 65536, token));
            stage = "metadata-validation";
            if (metadata is null || metadata.ProductID != approval.ProductID || metadata.ArtworkId != approval.ArtworkId ||
                metadata.SchemaVersion != 1 || metadata.RequestId == Guid.Empty || metadata.Preset != approval.Preset ||
                metadata.ContentType != "image/jpeg" || metadata.Filename != "artwork.jpg" ||
                string.IsNullOrWhiteSpace(metadata.ProductName) || string.IsNullOrWhiteSpace(metadata.PresetName) ||
                metadata.ProductName.Contains("fixture", StringComparison.OrdinalIgnoreCase) ||
                metadata.PresetName.Contains("fixture", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Artwork does not match its publication approval.");
            stage = "jpeg-read";
            var bytes = await DownloadAsync(prefix + "artwork.jpg", approval.Length, token);
            stage = "jpeg-hash-validation";
            if (bytes.Length != approval.Length || bytes[0] != 255 || bytes[1] != 216 || bytes[2] != 255 ||
                !Convert.ToHexString(SHA256.HashData(bytes)).Equals(approval.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Artwork content does not match its publication approval.");
            return bytes;
        }
        catch (RequestFailedException ex) when (ex.Status == 404 && ex.ErrorCode == "BlobNotFound" &&
            stage is "metadata-read" or "jpeg-read")
        {
            logger?.LogWarning("Approved artwork missing at stage {Stage} for product {ProductId}", stage, approval.ProductID);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogError("Approved artwork reader failed at stage {Stage} for product {ProductId}; failure type {FailureType}; storage status {StorageStatus}",
                stage, approval.ProductID, ex.GetType().Name, ex is RequestFailedException storage ? storage.Status : null as int?);
            throw;
        }
    }

    private async Task<byte[]> DownloadAsync(string name, int limit, CancellationToken token)
    {
        var response = await container.GetBlobClient(name).DownloadStreamingAsync(cancellationToken: token);
        using var stream = response.Value.Content;
        if (response.Value.Details.ContentLength > limit) throw new InvalidDataException("Artwork exceeds the approved size.");
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer, token)) != 0)
        {
            if (output.Length + count > limit) throw new InvalidDataException("Artwork exceeds the approved size.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }
}
