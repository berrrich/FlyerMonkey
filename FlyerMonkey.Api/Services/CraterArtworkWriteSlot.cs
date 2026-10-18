namespace FlyerMonkey.Api.Services;

// Coordinate fixture imports and paid generation within the existing single-API-process development model.
internal static class CraterArtworkWriteSlot
{
    internal static readonly SemaphoreSlim Instance = new(1, 1);
}
