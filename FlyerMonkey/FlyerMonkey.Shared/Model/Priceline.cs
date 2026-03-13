
using System.Text.Json.Serialization;

namespace FlyerMonkey.Shared.Model;
public class Priceline
{
    public string Position { get; set; } = string.Empty;
    public string ImageAlt { get; set; } = string.Empty;
    public string image { get; set; } = string.Empty;
    public string imageSrc { get; set; } = string.Empty;
    public string imageURL { get; set; } = string.Empty;
    public string description { get; set; } = string.Empty;
    public string url { get; set; } = string.Empty;
    public string regPrice { get; set; } = string.Empty;
    public string regPrice2 { get; set; } = string.Empty;
    public string salePrice { get; set; } = string.Empty;
}

[JsonSerializable(typeof(List<Priceline>))]

internal sealed partial class PricelineContext : JsonSerializerContext
{

}
