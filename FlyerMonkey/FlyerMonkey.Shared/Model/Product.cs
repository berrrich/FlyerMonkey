using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace FlyerMonkey.Shared.Model
{
    public class Product
    {
        public string Name { get; set; } = string.Empty;
        public int ID { get; set; }
    }

    //[JsonSerializable(typeof(List<Product>))]

    //internal sealed partial class ProductContext : JsonSerializerContext
    //{
    //}
}
