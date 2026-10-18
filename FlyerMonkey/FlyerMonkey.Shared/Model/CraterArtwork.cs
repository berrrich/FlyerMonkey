using FlyerMonkey.Shared.Services;

namespace FlyerMonkey.Shared.Model;

public sealed record CraterArtwork(Guid ArtworkId, Guid RequestId, int ProductID,
    string ProductName, CraterStyle Preset, string PresetName, DateTimeOffset CreatedUtc,
    string ContentType, string Filename, int SchemaVersion, string RequestFingerprint);
