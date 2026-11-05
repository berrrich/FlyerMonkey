using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FlyerMonkey.Reviewer.Windows;

/// <summary>Uses the card width, capped at one physical display pixel per source pixel.</summary>
public sealed class ProductThumbnail : Image
{
    protected override Size MeasureOverride(Size availableSize)
    {
        if (Source is BitmapSource bitmap)
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            availableSize.Width = Math.Min(availableSize.Width, bitmap.PixelWidth / dpi.DpiScaleX);
            availableSize.Height = Math.Min(availableSize.Height, bitmap.PixelHeight / dpi.DpiScaleY);
        }
        return base.MeasureOverride(availableSize);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        InvalidateMeasure();
    }
}
