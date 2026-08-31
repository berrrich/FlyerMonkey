using System.Net.Http.Json;
using FlyerMonkey.Shared.Model;
using FlyerMonkey.Shared.Services;

namespace FlyerMonkey.Web.Services;

public sealed class ProductApiService : IProductService
{
    private readonly HttpClient _httpClient;

    public ProductApiService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<List<Product>> GetProductsAsync(
        CancellationToken cancellationToken = default)
    {
        var products = await _httpClient.GetFromJsonAsync<List<Product>>(
            "api/products",
            cancellationToken);

        return products ?? new List<Product>();
    }
}