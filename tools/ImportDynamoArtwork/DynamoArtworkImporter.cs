using System.Security.Cryptography;
using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using FlyerMonkey.Shared.Model;
using FlyerMonkey.Shared.Services;

namespace FlyerMonkey.Tools;

public static class DynamoArtworkImporter
{
    public static async Task ImportAsync(string directory, Func<BlobContainerClient> createContainer)
    {
        var id = Guid.Parse("28071cca-5456-4f78-b621-28c504a3440a");
        const string hash = "D83EC0500BD2152E02E571BF2B7E825E31AD4DC1565DF49A78F6AD1DE99E4817";
        var jpeg = await File.ReadAllBytesAsync(Path.Combine(directory, "artwork.jpg"));
        var json = await File.ReadAllBytesAsync(Path.Combine(directory, "metadata.json"));
        if (Convert.ToHexString(SHA256.HashData(json)) != "FB70CD7A12720B8C8461CA9D6B65E0D06C4D555AFA96DB9581CF62130D7CF37A") throw new InvalidDataException("Source metadata differs from the reviewed original.");
        var metadata = JsonSerializer.Deserialize<CraterArtwork>(json);
        if (metadata is null || metadata.ArtworkId != id || metadata.ProductID != 102 ||
            metadata.RequestId != Guid.Parse("07bd10b8-6513-42a8-a410-b4a9c6531237") ||
            metadata.ProductName != "Dynamo Professional Laundry Liquid" || metadata.Preset != CraterStyle.CometCrush ||
            metadata.PresetName != "Comet Crush" || metadata.SchemaVersion != 1 || metadata.ContentType != "image/jpeg" ||
            metadata.Filename != "artwork.jpg" || jpeg.Length != 164538 || Convert.ToHexString(SHA256.HashData(jpeg)) != hash)
            throw new InvalidDataException("Source differs from the reviewed Dynamo artwork.");
        await CopyCreateOnlyAsync(jpeg, json, createContainer());
    }

    // Only the pinned-source entry point above is exposed to the operator.
    internal static async Task CopyCreateOnlyAsync(byte[] jpeg, byte[] json, BlobContainerClient container)
    {
        var id = Guid.Parse("28071cca-5456-4f78-b621-28c504a3440a");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var token = timeout.Token;
        if (container.Name != "crater-artwork") throw new InvalidDataException("Wrong container.");
        if ((await container.GetPropertiesAsync(cancellationToken: token)).Value.PublicAccess != PublicAccessType.None)
            throw new InvalidDataException("Container must remain private.");
        var image = container.GetBlobClient($"artwork/{id:N}/artwork.jpg");
        var manifest = container.GetBlobClient($"artwork/{id:N}/metadata.json");
        if ((await image.ExistsAsync(token)).Value || (await manifest.ExistsAsync(token)).Value)
            throw new IOException("Destination already exists; no overwrite or repair permitted.");
        var options = new BlobUploadOptions { Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All },
            HttpHeaders = new BlobHttpHeaders { ContentType = "image/jpeg", CacheControl = "no-store" } };
        await image.UploadAsync(BinaryData.FromBytes(jpeg), options, token);
        options.HttpHeaders.ContentType = "application/json";
        await manifest.UploadAsync(BinaryData.FromBytes(json), options, token);
        if (!(await image.DownloadContentAsync(token)).Value.Content.ToArray().AsSpan().SequenceEqual(jpeg) ||
            !(await manifest.DownloadContentAsync(token)).Value.Content.ToArray().AsSpan().SequenceEqual(json))
            throw new IOException("Post-copy verification failed; inspect storage without retrying.");
    }
}
