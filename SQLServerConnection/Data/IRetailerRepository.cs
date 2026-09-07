namespace SQLServerConnection.Data;

public interface IRetailerRepository
{
    Task<int?> GetRetailerIdByNameAsync(
        string name,
        CancellationToken cancellationToken = default);
}