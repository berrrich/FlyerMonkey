using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace FlyerMonkey.Shared.Model
{
    public class Product
    {
        public int ID { get; set; }

        public string Name { get; set; } = string.Empty;

        public string? Brand { get; set; }

        public string? Variant { get; set; }

        public string? PackSizeText { get; set; }

        public string? Barcode { get; set; }

        public string? Category { get; set; }

        public string? ImageBlobPath { get; set; }
    }
}
