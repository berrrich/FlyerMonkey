namespace SQLServerConnection.Data;

public interface IRetailerLocationRepository
{
    Task<int?> GetLocationIdAsync(
        int retailerId,
        string suburb,
        CancellationToken cancellationToken = default);
}