using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using FlyerMonkey.Reviewer.Windows.Models;

namespace FlyerMonkey.Reviewer.Windows.Services;

public sealed record ArtworkRecord(string ArtworkId, string FilePath, DateTimeOffset CreatedUtc, string ProductName,
    string? Variant, string? PackSizeText, string Prompt, string Origin, string Sha256);

/// <summary>Immutable artwork and provenance; lifecycle events are append-only. No cleanup or overwriting.</summary>
public sealed class ArtworkArchive(string folder, string generator = "Configured Classic provider")
{
    public string Folder => Path.GetFullPath(folder);
    private string ProvenanceFolder => Path.Combine(Folder, "provenance");
    public static string FriendlyPart(string? value)
    {
        var part = Regex.Replace(value ?? "", @"[^\p{L}\p{N}.-]+", "-").Trim('-', '.', ' ');
        if (part.Length > 55) part = part[..55].TrimEnd('-', '.');
        return string.IsNullOrEmpty(part) ? "product" : part;
    }
    public async Task<ArtworkRecord> CreateAsync(ExtractedProduct product, byte[] jpeg, string prompt, string origin = "Classic generation")
    {
        ClassicImageValidation.ValidateJpeg("image/jpeg", jpeg);
        var id = Guid.NewGuid().ToString("N"); var now = DateTimeOffset.UtcNow;
        Directory.CreateDirectory(Folder); Directory.CreateDirectory(ProvenanceFolder);
        var details = string.Join("-", new[] { product.Variant, product.PackSizeText }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var filename = FriendlyPart(product.ProductName) + (string.IsNullOrWhiteSpace(details) ? "" : "-" + FriendlyPart(details)) + $"-{now:yyyyMMdd-HHmmss}-{id}.jpg";
        var record = new ArtworkRecord(id, Path.Combine(Folder, filename), now, product.ProductName, product.Variant,
            product.PackSizeText, prompt, origin + "; generator: " + generator, Convert.ToHexString(SHA256.HashData(jpeg)));
        // Finish saving collateral even if local cancellation arrives. Never publish a reference before both writes succeed.
        await using (var image = new FileStream(record.FilePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            await image.WriteAsync(jpeg);
        await WriteNewJsonAsync(Path.Combine(ProvenanceFolder, id + ".json"), record);
        await RecordEventAsync(record, "Generated");
        return record;
    }
    private static async Task WriteNewJsonAsync(string path, object data)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        await JsonSerializer.SerializeAsync(stream, data, new JsonSerializerOptions { WriteIndented = true });
    }
    public async Task RecordEventAsync(ArtworkRecord record, string action)
    {
        var events = Path.Combine(ProvenanceFolder, "events"); Directory.CreateDirectory(events);
        await WriteNewJsonAsync(Path.Combine(events, record.ArtworkId + "-" + Guid.NewGuid().ToString("N") + ".json"),
            new { record.ArtworkId, Action = action, Utc = DateTimeOffset.UtcNow, record.FilePath });
    }
    public async Task<ArtworkRecord?> ExistingAsync(ExtractedProduct product)
    {
        if (string.IsNullOrWhiteSpace(product.ImageFilePath)) return null;
        Directory.CreateDirectory(ProvenanceFolder);
        if (!string.IsNullOrWhiteSpace(product.ArtworkId))
        {
            var metadata = Path.Combine(ProvenanceFolder, FriendlyPart(product.ArtworkId) + ".json");
            if (File.Exists(metadata)) { var existing = JsonSerializer.Deserialize<ArtworkRecord>(await File.ReadAllTextAsync(metadata)); if (existing?.FilePath == product.ImageFilePath) return existing; }
        }
        var id = Guid.NewGuid().ToString("N");
        var hash = File.Exists(product.ImageFilePath) ? Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(product.ImageFilePath))) : "Unavailable";
        var record = new ArtworkRecord(id, product.ImageFilePath, DateTimeOffset.UtcNow, product.ProductName,
            product.Variant, product.PackSizeText, "Unknown: existing image reference", "Legacy reference (file retained in place)", hash);
        await WriteNewJsonAsync(Path.Combine(ProvenanceFolder, id + ".json"), record);
        product.ArtworkId = id;
        return record;
    }
    public async Task DetachAsync(ExtractedProduct product)
    {
        var record = await ExistingAsync(product);
        if (record == null) return;
        await RecordEventAsync(record, "Detached / archived");
        product.ImageFilePath = null; product.ArtworkId = null;
    }
    public static void Assign(ExtractedProduct product, ArtworkRecord record) { product.ImageFilePath = record.FilePath; product.ArtworkId = record.ArtworkId; }
}

public sealed class ClassicArtworkWorkflow(ExtractedProduct product, ArtworkArchive archive, Func<string, CancellationToken, Task<byte[]>> generate)
{
    private readonly string? original = product.ImageFilePath;
    private ArtworkRecord? proposal;
    private bool busy;
    private volatile bool closed;
    public bool HasProposal => !closed && proposal != null;
    public string? ProposedPath => proposal?.FilePath;
    public async Task GenerateAsync(string? instructions, bool assignWorking, CancellationToken token, Action<string>? report = null, bool deferAssignment = false)
    {
        if (busy || closed) throw new InvalidOperationException("An image operation is already running or closed.");
        busy = true; proposal = null;
        try
        {
            token.ThrowIfCancellationRequested();
            var prompt = new ProductImageService().BuildImagePrompt(product, instructions);
            report?.Invoke("Waiting for the image provider. Local Cancel is available.");
            var bytes = await generate(prompt, token);
            report?.Invoke("Archiving returned artwork and provenance...");
            // Archive returned output before observing late cancellation/close: preserve completed collateral.
            var artwork = await archive.CreateAsync(product, bytes, prompt, assignWorking ? "One-click Crater It" : "Edit Image proposal");
            if (token.IsCancellationRequested || closed)
            {
                await archive.RecordEventAsync(artwork, "Completed after cancellation / not assigned");
                throw new OperationCanceledException("Local image processing cancelled; completed returned artwork was archived. Provider cancellation is uncertain.", token);
            }
            proposal = artwork;
            if (assignWorking && !deferAssignment) await AcceptAsync();
        }
        finally { busy = false; }
    }
    public async Task AcceptAsync(Func<Action, Task>? dispatchAssignment = null)
    {
        if (!HasProposal) throw new InvalidOperationException("Generate a proposed image first.");
        if (product.ImageFilePath != original) throw new InvalidOperationException("The working image changed; this proposal cannot overwrite it.");
        var accepted = proposal!;
        var prior = await archive.ExistingAsync(product);
        if (closed || product.ImageFilePath != original) throw new OperationCanceledException("The image editor closed or the working image changed.");
        if (prior != null) await archive.RecordEventAsync(prior, "Superseded / retained");
        await archive.RecordEventAsync(accepted, "Assigned working image");
        void Assign()
        {
            if (closed || product.ImageFilePath != original) throw new OperationCanceledException("The image editor closed or the working image changed.");
            ArtworkArchive.Assign(product, accepted); closed = true;
        }
        if (dispatchAssignment == null) Assign(); else await dispatchAssignment(Assign);
    }
    public void Close() { closed = true; proposal = null; }
}
