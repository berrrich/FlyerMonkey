using System.Text.Json;
using FlyerMonkey.Shared.Model;

namespace FlyerMonkey.Api.Services;

public sealed class LocalCraterArtworkStore(string root, ILogger<LocalCraterArtworkStore> logger) : ICraterArtworkStore
{
    public static string ResolveRoot(IWebHostEnvironment environment)
    {
        var api = new DirectoryInfo(environment.ContentRootPath);
        var repository = api.Parent;
        if (api.Name != "FlyerMonkey.Api" || repository is null ||
            !File.Exists(Path.Combine(api.FullName, "FlyerMonkey.Api.csproj")) ||
            !File.Exists(Path.Combine(repository.FullName, "FlyerMonkey", "FlyerMonkey.Shared", "FlyerMonkey.Shared.csproj")))
            throw new InvalidOperationException("Crater artwork requires the local repository layout.");
        return Path.Combine(repository.FullName, "DATA", "CraterArt");
    }

    private string Folder(Guid id) => Path.Combine(root, id.ToString("N"));

    private async Task<CraterArtwork?> MetadataAsync(Guid id, CancellationToken token, bool requireImage = true)
    {
        try
        {
            var path = Path.Combine(Folder(id), "metadata.json");
            if (!File.Exists(path)) return null;
            await using var stream = File.OpenRead(path);
            var item = await JsonSerializer.DeserializeAsync<CraterArtwork>(stream, cancellationToken: token);
            if (item is null || item.ArtworkId != id || item.RequestId == Guid.Empty || item.ProductID <= 0 ||
                item.SchemaVersion != 1 || item.ContentType != "image/jpeg" || item.Filename != "artwork.jpg" ||
                !Enum.IsDefined(item.Preset) || string.IsNullOrWhiteSpace(item.RequestFingerprint) ||
                requireImage && !File.Exists(Path.Combine(Folder(id), "artwork.jpg")))
            {
                logger.LogWarning("Incomplete Crater artwork {ArtworkId}", id);
                return null;
            }
            return item;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Unreadable Crater artwork {ArtworkId}", id);
            return null;
        }
    }

    private async Task<List<CraterArtwork>> AllAsync(CancellationToken token, bool requireImage = true)
    {
        var items = new List<CraterArtwork>();
        if (!Directory.Exists(root)) return items;
        foreach (var folder in Directory.EnumerateDirectories(root))
        {
            token.ThrowIfCancellationRequested();
            if (Guid.TryParseExact(Path.GetFileName(folder), "N", out var id) &&
                await MetadataAsync(id, token, requireImage) is { } item) items.Add(item);
        }
        return items;
    }

    public async Task<CraterArtwork?> FindRequestAsync(Guid requestId, CancellationToken token) =>
        (await AllAsync(token, false)).SingleOrDefault(a => a.RequestId == requestId);

    public async Task<IReadOnlyList<CraterArtwork>> ListAsync(int productId, CancellationToken token) =>
        (await AllAsync(token)).Where(a => a.ProductID == productId)
            .OrderByDescending(a => a.CreatedUtc).ThenBy(a => a.ArtworkId).ToArray();

    public async Task SaveAsync(CraterArtwork artwork, byte[] jpeg, CancellationToken token)
    {
        if (artwork.ArtworkId == Guid.Empty || artwork.Filename != "artwork.jpg" ||
            jpeg.Length < 3 || jpeg[0] != 255 || jpeg[1] != 216 || jpeg[2] != 255)
            throw new InvalidDataException("Invalid artwork.");
        Directory.CreateDirectory(root);
        var absoluteRoot = Path.GetFullPath(root);
        var staging = Path.GetFullPath(Path.Combine(absoluteRoot, ".staging-" + Guid.NewGuid().ToString("N")));
        if (!string.Equals(Path.GetDirectoryName(staging), absoluteRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Staging path escaped the artwork root.");
        Directory.CreateDirectory(staging);
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(staging, "artwork.jpg"), jpeg, token);
            await File.WriteAllTextAsync(Path.Combine(staging, "metadata.json"), JsonSerializer.Serialize(artwork), token);
            token.ThrowIfCancellationRequested();
            Directory.Move(staging, Folder(artwork.ArtworkId));
        }
        finally
        {
            // Only delete the uniquely created staging directory inside the validated storage root.
            if (Directory.Exists(staging))
            {
                try { Directory.Delete(staging, true); }
                catch (IOException ex) { logger.LogWarning(ex, "Crater staging cleanup failed"); }
                catch (UnauthorizedAccessException ex) { logger.LogWarning(ex, "Crater staging cleanup denied"); }
            }
        }
    }

    public async Task<byte[]?> ReadAsync(Guid artworkId, CancellationToken token)
    {
        if (artworkId == Guid.Empty || await MetadataAsync(artworkId, token) is null) return null;
        try { return await File.ReadAllBytesAsync(Path.Combine(Folder(artworkId), "artwork.jpg"), token); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }
}
