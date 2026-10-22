using FlyerMonkey.Shared.Model;

namespace FlyerMonkey.Api.Services;

public interface ICraterArtworkStore
{
    Task<CraterArtwork?> GetMetadataAsync(Guid artworkId, CancellationToken token) => Task.FromResult<CraterArtwork?>(null);
    Task<CraterArtwork?> FindRequestAsync(Guid requestId, CancellationToken token);
    Task<IReadOnlyList<CraterArtwork>> ListAsync(int productId, CancellationToken token);
    Task SaveAsync(CraterArtwork artwork, byte[] jpeg, CancellationToken token);
    Task<byte[]?> ReadAsync(Guid artworkId, CancellationToken token);
}
