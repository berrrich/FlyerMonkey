using System.IO;

namespace FlyerMonkey.Reviewer.Windows.Services;

public static class ClassicImageFileService
{
    public static async Task<string> SaveAsync(string folder, byte[] jpeg)
    {
        ClassicImageValidation.ValidateJpeg("image/jpeg", jpeg);
        var path = Path.Combine(folder, $"{Guid.NewGuid():N}.jpg");
        try
        {
            Directory.CreateDirectory(folder);
            await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            await stream.WriteAsync(jpeg);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Retain partial collateral for inspection. Never overwrite or clean up image files automatically.
            throw new IOException("Classic image local saving failed in DATA/ProductImages. Check folder access and available disk space. Any partial collateral is retained.");
        }
    }
}
