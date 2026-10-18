namespace FlyerMonkey.Api.Models;

public class GenerateImageRequest
{
    public Guid RequestId { get; set; }
    public int ProductID { get; set; }
    public string ProductName { get; set; } = "";
    public FlyerMonkey.Shared.Services.CraterStyle Preset { get; set; }
    public string Prompt { get; set; } = "";
}
