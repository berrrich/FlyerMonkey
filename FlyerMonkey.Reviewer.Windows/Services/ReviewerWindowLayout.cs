using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FlyerMonkey.Reviewer.Windows.Services;

public static class ReviewerWindowLayout
{
    public static (double Flyers,double Pages,double Products,double Saved) Columns(double width)
    {
        var products = Math.Clamp(width * 0.215, 220, 300);
        var sideScale = Math.Clamp((width - products - 220) / 770, 0, 1);
        return (300 * sideScale,240 * sideScale,products,230 * sideScale);
    }
    public static void ConstrainToCurrentMonitor(Window window, DpiScale dpi)
    {
        if (window.WindowState != WindowState.Normal) return;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(handle,2), ref info)) return;
        if (!GetWindowRect(handle, out var bounds)) return;
        // Both native rectangles use physical pixels, including negative monitor
        // origins. Avoid converting global coordinates with a single monitor DPI.
        var visible = VisibleBounds(
            new System.Windows.Rect(bounds.Left, bounds.Top, bounds.Right-bounds.Left, bounds.Bottom-bounds.Top),
            new System.Windows.Rect(info.Work.Left, info.Work.Top, info.Work.Right-info.Work.Left, info.Work.Bottom-info.Work.Top));
        SetWindowPos(handle, IntPtr.Zero, (int)visible.Left, (int)visible.Top,
            (int)visible.Width, (int)visible.Height, 0x0004 | 0x0010); // no z-order change or activation
    }
    public static System.Windows.Rect VisibleBounds(System.Windows.Rect bounds, System.Windows.Rect work)
    {
        var width = Math.Min(bounds.Width, work.Width);
        var height = Math.Min(bounds.Height, work.Height);
        return new System.Windows.Rect(Math.Clamp(bounds.Left, work.Left, work.Right-width),
            Math.Clamp(bounds.Top, work.Top, work.Bottom-height), width, height);
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size;public Rect Monitor,Work;public uint Flags; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd,out Rect rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
}
