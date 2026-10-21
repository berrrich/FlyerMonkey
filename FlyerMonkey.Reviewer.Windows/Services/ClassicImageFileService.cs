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
            await File.WriteAllBytesAsync(path, jpeg);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            try { File.Delete(path); }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
            throw new IOException("Classic image local saving failed in DATA/ProductImages. Check folder access and available disk space.");
        }
    }
}
