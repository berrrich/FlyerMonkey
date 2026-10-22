using FlyerMonkey.Shared.Services;

namespace FlyerMonkey.Shared.Model;

public sealed record CraterArtwork(Guid ArtworkId, Guid RequestId, int ProductID,
    string ProductName, CraterStyle Preset, string PresetName, DateTimeOffset CreatedUtc,
    string ContentType, string Filename, int SchemaVersion, string RequestFingerprint)
{
    // Nullable for existing immutable manifests. Ownerless records are never private operator artwork.
    public Guid? OwnerTenantId { get; init; }
    public Guid? OwnerObjectId { get; init; }
}
