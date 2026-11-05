using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Syncfusion.Licensing;
using Syncfusion.Windows.PdfViewer;

namespace FlyerMonkey.Reviewer.Windows;

/// <summary>Embedded local-only PDF review. No save/annotation/form editing controls are exposed.</summary>
public partial class PdfPageReview : UserControl, IDisposable
{
    private PdfDocumentView? _viewer;
    private MemoryStream? _document;
    private bool _fitToWidth;
    private int _layoutVersion;
    public string? LoadedPath { get; private set; }
    public bool HasDocument => LoadedPath != null && _viewer?.PageCount > 0;
    public int ZoomPercentage => _viewer?.ZoomPercentage ?? 100;
    public string StatusMessage => PreviewStatus.Text;

    public PdfPageReview() { InitializeComponent(); }

    public bool LoadPage(string? path)
    {
        Clear();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            PreviewStatus.Text = "The saved split PDF page is not available locally.";
            return false;
        }
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!string.Equals(Path.GetExtension(fullPath), ".pdf", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Choose a local PDF page.");
            var bytes = File.ReadAllBytes(fullPath);
            using (var validationStream = new MemoryStream(bytes, writable: false))
            using (var document = new Syncfusion.Pdf.Parsing.PdfLoadedDocument(validationStream))
            {
                if (document.Pages.Count == 0) throw new InvalidDataException("The PDF has no pages.");
            }
            EnsureViewer();
            // Read-only in-memory copy: the viewer cannot lock or change the source file.
            _document = new MemoryStream(bytes, writable: false);
            LoadedPath = fullPath;
            _viewer!.Load(_document);
            if (_viewer.PageCount == 0) throw new InvalidDataException("The PDF page could not be loaded.");
            PageName.Text = Path.GetFileName(fullPath);
            PreviewStatus.Text = "Scroll to inspect; use + / - or Fit width to zoom.";
            SetButtons(true);
            FitWidth();
            return true;
        }
        catch (Exception ex)
        {
            Clear("PDF preview unavailable. " + ex.Message);
            return false;
        }
    }

    private void EnsureViewer()
    {
        if (_viewer != null) return;
        Syncfusion.Telemetry.Telemetry.Disable();
        var key = Environment.GetEnvironmentVariable("FLYERMONKEY_SYNCFUSION_KEY");
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("The workstation Syncfusion WPF licence is not configured.");
        SyncfusionLicenseProvider.RegisterLicense(key);
        if (!SyncfusionLicenseProvider.ValidateLicense(new[] { Platform.WPF }))
            throw new InvalidOperationException("The workstation licence does not cover this Syncfusion WPF viewer version.");
        _viewer = new PdfDocumentView
        {
            ShowScrollbar = true,
            ShowVerticalScrollBar = true,
            ShowHorizontalScrollBar = true,
            MinimumZoomPercentage = 10,
            MaximumZoomPercentage = 400
        };
        _viewer.PreviewTextInput += (_, e) => e.Handled = true;
        _viewer.PreviewMouseRightButtonDown += (_, e) => e.Handled = true;
        _viewer.ZoomChanged += (_, _) => { if (_viewer is { } current) ZoomText.Text = $"{current.ZoomPercentage}%"; };
        _viewer.Loaded += (_, _) => RefreshForDisplayChange();
        _viewer.DocumentLoaded += (_, _) => RefreshForDisplayChange();
        _viewer.SizeChanged += (_, _) => RefreshForDisplayChange();
        _viewer.ScrollChanged += (_, e) => {
            if (e.ViewportWidthChange != 0 || e.ViewportHeightChange != 0 || e.ExtentWidthChange != 0)
                RefreshForDisplayChange();
        };
        ViewerHost.Content = _viewer;
    }

    public void SetZoom(int percentage)
    {
        if (!HasDocument) return;
        _fitToWidth = false;
        _layoutVersion++;
        _viewer!.ZoomMode = ZoomMode.Default;
        _viewer.ShowHorizontalScrollBar = true;
        _viewer.ZoomTo(Math.Clamp(percentage, 10, 400));
        ZoomText.Text = $"{_viewer.ZoomPercentage}%";
    }

    public void FitWidth()
    {
        _fitToWidth = true;
        if (_viewer != null) _viewer.ShowHorizontalScrollBar = false;
        RefreshForDisplayChange();
    }

    private void ApplyFitWidth()
    {
        if (!_fitToWidth || !HasDocument || _viewer!.ActualWidth <= 0 || _viewer.ActualHeight <= 0) return;
        var scroll = FindScrollViewer(_viewer!);
        var width = scroll?.ViewportWidth ?? 0;
        var renderedWidth = scroll?.ExtentWidth ?? 0;
        if (width <= 16 || renderedWidth <= 0) return;
        // Measure Syncfusion's rendered page extent and its own viewport in the
        // same coordinate system. PDF points and monitor DPI cannot reliably
        // predict this SDK's zoom interpretation across different documents.
        var zoom = Math.Clamp((int)Math.Floor(_viewer!.ZoomPercentage * (width - 16) / renderedWidth), 10, 400);
        // Integer zoom can leave less than one percent of unused width. Avoid
        // alternating between adjacent percentages due to layout rounding.
        if (renderedWidth <= width - 12 && zoom <= _viewer.ZoomPercentage + 1) return;
        if (_viewer!.ZoomPercentage == zoom) return;
        _viewer.ZoomMode = ZoomMode.Default;
        _viewer.ZoomTo(zoom);
        ZoomText.Text = $"{_viewer.ZoomPercentage}%";
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        if (parent is ScrollViewer { Name: "PART_ScrollViewer" } scroll) return scroll;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            if (FindScrollViewer(VisualTreeHelper.GetChild(parent, i)) is { } child) return child;
        return null;
    }

    public void RefreshForDisplayChange()
    {
        var version = ++_layoutVersion;
        Dispatcher.BeginInvoke(new Action(() => {
            if (version != _layoutVersion || !HasDocument || !_fitToWidth || !IsVisible) return;
            // Run after WPF has arranged the current viewport (including native scrollbars).
            // The viewport is in device-independent units.
            // Reapplying manual ZoomTo here resets the user's scroll position.
            ViewerHost.UpdateLayout();
            if (version == _layoutVersion) ApplyFitWidth();
        }), System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        RefreshForDisplayChange();
    }
    public void Clear(string message = "Select a split PDF page or a saved extraction.")
    {
        _layoutVersion++;
        _fitToWidth = false;
        LoadedPath = null;
        _viewer?.Unload();
        _document?.Dispose(); _document = null;
        PageName.Text = "";
        PreviewStatus.Text = message;
        SetButtons(false);
    }

    private void SetButtons(bool enabled) { ZoomOutButton.IsEnabled = ZoomInButton.IsEnabled = FitWidthButton.IsEnabled = enabled; }
    private void ZoomIn_Click(object sender, RoutedEventArgs e) => SetZoom(ZoomPercentage + 25);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => SetZoom(ZoomPercentage - 25);
    private void FitWidth_Click(object sender, RoutedEventArgs e) => FitWidth();
    public void Dispose() { Clear(); ViewerHost.Content = null; _viewer = null; }
}
