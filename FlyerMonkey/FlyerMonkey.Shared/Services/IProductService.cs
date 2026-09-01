using System;
using System.Collections.Generic;
using System.Text;
using FlyerMonkey.Shared.Model;
using System.Threading;
using System.Threading.Tasks;

namespace FlyerMonkey.Shared.Services
{
    public interface IProductService
    {
        Task<List<Product>> GetProductsAsync(CancellationToken cancellationToken = default);
    }
}
