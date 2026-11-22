namespace FlyerMonkey.Shared.Services;

public sealed class ClassicCraterImages(Uri apiRoot)
{
    private int version;
    public event Action? Changed;

    public string Url(int productId) =>
        new Uri(apiRoot, $"api/products/{productId}/classic-crater-image?v={version}").AbsoluteUri;

    public void Refresh()
    {
        Interlocked.Increment(ref version);
        Changed?.Invoke();
    }
}
