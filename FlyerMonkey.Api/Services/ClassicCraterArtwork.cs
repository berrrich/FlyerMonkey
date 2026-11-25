using System.Globalization;
using FlyerMonkey.Shared.Model;
using FlyerMonkey.Shared.Services;

namespace FlyerMonkey.Api.Services;

public sealed class ClassicCraterArtwork(string root, ICustomerCraterStore quota)
{
    private string Latest(int productId) =>
        Path.Combine(root, productId.ToString(CultureInfo.InvariantCulture) + ".jpg");

    public bool Exists(int productId) => File.Exists(Latest(productId));

    public byte[]? Read(int productId)
    {
        var file = Latest(productId);
        return File.Exists(file) ? File.ReadAllBytes(file) : null;
    }

    public async Task<bool> GenerateAsync(Product product, Func<ICustomerCraterProvider> providerFactory)
    {
        if (!quota.TryReserveDailySlot(DateOnly.FromDateTime(DateTime.UtcNow))) return false;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var image = await providerFactory().GenerateAsync(
            CraterItPromptBuilder.Build(product, CraterStyle.Classic), timeout.Token);
        if (image.Length < 3 || image[0] != 255 || image[1] != 216 || image[2] != 255)
            throw new InvalidDataException("Provider did not return a JPEG.");
        Directory.CreateDirectory(root);
        var file = Latest(product.ID);
        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, image);
            File.Move(temporary, file, overwrite: true);
        }
        finally { File.Delete(temporary); }
        return true;
    }
}
