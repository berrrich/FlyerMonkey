using FlyerMonkey.Api.Models;

namespace FlyerMonkey.Api.Services
{
    public static class ProductService
    {
        static List<Product> Products { get; }

        static ProductService()
        {
            Products = new List<Product>
            {
                new Product
                {
                    Id = 1,
                    Name = "Tim Tam Original",
                    Price = 4.50m
                },
                new Product
                {
                    Id = 2,
                    Name = "Vegemite",
                    Price = 6.00m
                },
                new Product
                {
                    Id = 3,
                    Name = "Bananas",
                    Price = 1.50m
                }
            };
        }

        public static List<Product> GetAll() => Products;

        public static Product? Get(int id)
        {
            return Products.FirstOrDefault(p => p.Id == id);
        }

    }
}
