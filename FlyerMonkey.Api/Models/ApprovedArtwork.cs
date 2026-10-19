using FlyerMonkey.Shared.Services;

namespace FlyerMonkey.Api.Models;

public sealed record ApprovedArtwork(int ProductID, Guid ArtworkId, string Sha256, int Length,
    CraterStyle Preset, string Provenance, string ApprovedBy, DateTimeOffset ApprovedUtc);

public sealed record ApprovedArtworkManifest(int SchemaVersion, ApprovedArtwork[] Entries)
{
    public void Validate()
    {
        if (SchemaVersion != 1 || Entries is null || Entries.Any(x => x is null) ||
            Entries.GroupBy(x => x.ProductID).Any(g => g.Count() != 1) ||
            Entries.Any(x => x.ProductID <= 0 || x.ArtworkId == Guid.Empty ||
                x.ArtworkId == Guid.Parse("df6a9c89-85b1-40e4-bbc9-d80103913b6c") ||
                x.ArtworkId == Guid.Parse("e21ce070-019e-4429-b32d-9872e38b5475") ||
                x.Provenance != "generated" || !Enum.IsDefined(x.Preset) ||
                string.IsNullOrWhiteSpace(x.ApprovedBy) || x.ApprovedUtc == default || x.ApprovedUtc.Offset != TimeSpan.Zero ||
                x.Length < 3 || x.Length > 2 * 1024 * 1024 || x.Sha256 is not { Length: 64 } || !x.Sha256.All(Uri.IsHexDigit)))
            throw new InvalidDataException("Invalid approved-artwork manifest.");
    }
}
