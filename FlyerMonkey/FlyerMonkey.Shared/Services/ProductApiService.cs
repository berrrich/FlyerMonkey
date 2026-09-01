using FlyerMonkey.Shared.Model;
using System.Net.Http.Json;

namespace FlyerMonkey.Shared.Services
{
    public class ProductApiService : IProductService
    {
        private readonly HttpClient _http;

        public ProductApiService(HttpClient http)
        {
            _http = http;
        }

        public async Task<List<Product>> GetProductsAsync(CancellationToken cancellationToken = default)
        {
            var result = await _http.GetFromJsonAsync<List<Product>>("api/products", cancellationToken);
            return result ?? new List<Product>();
        }
    }
}
