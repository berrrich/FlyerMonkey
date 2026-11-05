using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using FlyerMonkey.Reviewer.Windows.Models;
using FlyerMonkey.Reviewer.Windows.Services;

internal static partial class Program
{
    [StructLayout(LayoutKind.Sequential)] struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MonitorData { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet=CharSet.Auto)] static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorData info);
    static bool FullyVisible(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var info = new MonitorData { Size = Marshal.SizeOf<MonitorData>() };
        return GetWindowRect(handle, out var rect) && GetMonitorInfo(MonitorFromWindow(handle, 2), ref info)
            && rect.Left >= info.Work.Left && rect.Top >= info.Work.Top
            && rect.Right <= info.Work.Right && rect.Bottom <= info.Work.Bottom;
    }
    static void LayoutChecks()
    {
        foreach (var work in new[] { new Rect(0,0,1920,1040), new Rect(1920,0,3840,2080), new Rect(-3840,-400,3840,2080) })
        {
            foreach (var dpi in new[] { 1d, 1.5, 2d })
            {
                var result = ReviewerWindowLayout.VisibleBounds(new Rect(work.Left-500,work.Top-200,1400*dpi,800*dpi),work);
                Check(work.Contains(result), $"bounds fully visible at {work.Width}px / {dpi*100}% including monitor origin");
                var oversized = ReviewerWindowLayout.VisibleBounds(new Rect(work.Right-10,work.Bottom-10,6000,4000),work);
                Check(oversized == work, "oversized bounds clamp size and position to working area");
            }
        }
        var calls = 0;
        var f = Fixture((_,_) => { calls++; throw new Exception("No generation expected"); }, window => {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -20000; window.Top = -20000; window.Width = 2400; window.Height = 1600;
        });
        Check(FullyVisible(f.Window), "actual MainWindow startup recovers off-screen title bar and oversized bounds");
        var normal = new Rect(f.Window.Left,f.Window.Top,f.Window.Width,f.Window.Height);
        f.Window.WindowState = WindowState.Maximized; Pump();
        Check(f.Window.WindowState == WindowState.Maximized, "maximize remains maximized");
        f.Window.WindowState = WindowState.Normal; Pump();
        Check(FullyVisible(f.Window), "restore keeps actual WPF window fully within monitor work area");
        Check(Math.Abs(f.Window.Width-normal.Width)<2 && Math.Abs(f.Window.Height-normal.Height)<2, "maximize/restore preserves normal window size");
        f.Window.Left = -20000; f.Window.Top = -20000; Pump();
        ReviewerWindowLayout.ConstrainToCurrentMonitor(f.Window, VisualTreeHelper.GetDpi(f.Window)); Pump();
        Check(FullyVisible(f.Window), "display-change bounds guard recovers position as well as size");
        var imagePath = System.IO.Path.Combine(Root,"layout-image.jpg"); System.IO.File.WriteAllBytes(imagePath,Jpeg());
        ((ExtractedProduct)f.Products.Items[0]).ImageFilePath = imagePath; Pump(); f.Products.UpdateLayout();
        var image = Children<Image>(f.Products).Single(i => i.Source != null);
        Check(double.IsNaN(image.Width) && double.IsNaN(image.Height) && image.Stretch==Stretch.Uniform, "thumbnail uses responsive dimensions and preserves aspect ratio");
        Check(image.ActualWidth * VisualTreeHelper.GetDpi(image).DpiScaleX <= 17, "small source image is not enlarged beyond natural pixels");
        var parent = (FrameworkElement)VisualTreeHelper.GetParent(image);
        var point = image.TranslatePoint(new Point(),parent);
        Check(image.HorizontalAlignment==HorizontalAlignment.Center && Math.Abs(point.X+image.ActualWidth/2-parent.ActualWidth/2)<2, "actual thumbnail is centered within product card");
        Check(Children<Button>(f.Products).Any(b=>b.Content?.ToString()=="Edit Image" && b.IsEnabled)
            && Children<Button>(f.Products).Any(b=>b.Content?.ToString()=="Remove / Archive" && b.IsEnabled)
            && image.Cursor==System.Windows.Input.Cursors.Hand, "existing image actions and clickable preview remain available");
        var largePath = System.IO.Path.Combine(Root,"large-thumbnail.jpg");
        var encoder = new System.Windows.Media.Imaging.JpegBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(System.Windows.Media.Imaging.BitmapSource.Create(1200,600,96,96,PixelFormats.Bgr24,null,new byte[1200*600*3],1200*3)));
        using(var output = System.IO.File.Create(largePath)) encoder.Save(output);
        var cardWidth = parent.ActualWidth;
        ((ExtractedProduct)f.Products.Items[0]).ImageFilePath = largePath; Pump(); f.Products.UpdateLayout();
        Check(Math.Abs(image.ActualWidth - (parent.ActualWidth - 10)) < 2, "large thumbnail fills available inner card width with small margins");
        Check(Math.Abs(image.ActualWidth / image.ActualHeight - 2) < .03, "landscape thumbnail keeps source aspect ratio");
        Check(Math.Abs(parent.ActualWidth-cardWidth)<2, "responsive thumbnail does not widen card");
        var largeWidth = image.ActualWidth;
        f.Window.Width=900; Pump(); f.Products.UpdateLayout();
        Check(image.ActualWidth < largeWidth && Math.Abs(image.ActualWidth-(parent.ActualWidth-10))<2, "thumbnail follows narrower offer card on resize");
        Check(calls==0,"layout checks make no generation calls");
        f.Window.Close();
    }
}
