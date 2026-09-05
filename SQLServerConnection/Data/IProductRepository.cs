using FlyerMonkey.Shared.Model;

namespace SQLServerConnection.Data;

public interface IProductRepository
{
    Task<List<Product>> GetProductsAsync(
        CancellationToken cancellationToken = default);

    Task<int> AddProductAsync(
    Product product,
    CancellationToken cancellationToken = default);
}