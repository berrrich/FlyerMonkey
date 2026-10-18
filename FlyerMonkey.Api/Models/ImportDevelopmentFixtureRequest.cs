namespace FlyerMonkey.Api.Models;

// There is no client-supplied image, path, product name, preset or prompt.
public sealed class ImportDevelopmentFixtureRequest
{
    public Guid RequestId { get; set; }
    public Guid ArtworkId { get; set; }
    public int ProductID { get; set; }
}
