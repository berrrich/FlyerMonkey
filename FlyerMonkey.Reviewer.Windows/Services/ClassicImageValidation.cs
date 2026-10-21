using System.IO;
using System.Windows.Media.Imaging;

namespace FlyerMonkey.Reviewer.Windows.Services;

public static class ClassicImageValidation
{
    public static void ValidateJpeg(string? mediaType, byte[] bytes)
    {
        if (!string.Equals(mediaType, "image/jpeg", StringComparison.OrdinalIgnoreCase) ||
            bytes.Length < 4 || bytes[0] != 0xff || bytes[1] != 0xd8 || bytes[2] != 0xff)
            throw new InvalidDataException("Classic image generation response validation failed: expected JPEG content type and JPEG image bytes. No image was saved.");
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            if (decoder is not JpegBitmapDecoder || decoder.Frames.Count != 1)
                throw new InvalidDataException();
            // Force pixel decoding too: a JPEG header alone is not sufficient.
            var frame = decoder.Frames[0];
            if (frame.PixelWidth > 4096 || frame.PixelHeight > 4096)
                throw new InvalidDataException();
            var stride = checked((frame.PixelWidth * frame.Format.BitsPerPixel + 7) / 8);
            frame.CopyPixels(new byte[checked(stride * frame.PixelHeight)], stride, 0);
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException or
            InvalidOperationException or FormatException or OverflowException or System.Runtime.InteropServices.COMException)
        {
            throw new InvalidDataException("Classic image generation response validation failed: JPEG could not be decoded. No image was saved.");
        }
    }
}
