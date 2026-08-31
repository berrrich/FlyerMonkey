using FlyerMonkey.Shared.Model;
using SQLServerConnection.Data;

namespace FlyerMonkey.Api.Services;

public sealed class ProductService
{
    private readonly IProductRepository _repository;

    public ProductService(IProductRepository repository)
    {
        _repository = repository;
    }

    public async Task<List<Product>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        return await _repository.GetProductsAsync(cancellationToken);
    }

    public async Task<Product?> GetAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var products =
            await _repository.GetProductsAsync(cancellationToken);

        return products.FirstOrDefault(product => product.ID == id);
    }

    public async Task<int> AddAsync(
        Product product,
        CancellationToken cancellationToken = default)
    {
        return await _repository.AddProductAsync(
            product,
            cancellationToken);
    }
}