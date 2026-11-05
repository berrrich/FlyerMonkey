using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using FlyerMonkey.Reviewer.Windows;
using FlyerMonkey.Reviewer.Windows.Models;
using FlyerMonkey.Reviewer.Windows.Services;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Graphics;
using Syncfusion.Drawing;

internal static partial class Program
{
    private static int checks;
    private static void Check(bool success, string message) { if (!success) throw new Exception("FAIL " + message); checks++; Console.WriteLine("PASS " + message); }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static T? Visual<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent is T match) return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            if (Visual<T>(VisualTreeHelper.GetChild(parent, i)) is { } child) return child;
        return null;
    }
    private static void Eventually(Func<bool> condition, string message)
    {
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && deadline.Elapsed < TimeSpan.FromSeconds(5)) { Pump(); Thread.Sleep(10); }
        Check(condition(), message);
    }
    private static readonly string Root = Path.GetFullPath(Path.Combine(".artifacts", "reviewer-saved-preview", Guid.NewGuid().ToString("N")));
    [STAThread] private static int Main(string[] args)
    {
        try { Run(args.Contains("--ui")); return 0; } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static void Run(bool ui)
    {
        Environment.SetEnvironmentVariable("FLYERMONKEY_SQL_CONNECTION", null);
        Environment.SetEnvironmentVariable("FLYERMONKEY_BLOB_CONNECTION", null);
        Environment.SetEnvironmentVariable("FLYERMONKEY_OPENAI_KEY", null);
        Directory.CreateDirectory(Root);
        Syncfusion.Telemetry.Telemetry.Disable();
        Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense(Environment.GetEnvironmentVariable("FLYERMONKEY_SYNCFUSION_KEY"));
        var splitRoot = Path.Combine(Root, "Split"); var flyerFolder = Path.Combine(splitRoot, "fixture"); Directory.CreateDirectory(flyerFolder);
        var pdfPath = Path.Combine(Root, "fixture.pdf");
        using (var doc = new PdfDocument())
        {
            for (var i = 1; i <= 3; i++)
            {
                var page = doc.Pages.Add();
                var font = new PdfStandardFont(PdfFontFamily.Helvetica, 18);
                page.Graphics.DrawString("LOCAL REVIEW FIXTURE - PAGE " + i, font, PdfBrushes.Black, new PointF(25, 30));
                for (var row = 0; row < 20; row++) page.Graphics.DrawString("Produce and offers - line " + (row + 1), new PdfStandardFont(PdfFontFamily.Helvetica, 12), PdfBrushes.Black, new PointF(25, 80 + row * 25));
            }
            doc.Save(pdfPath);
        }
        List<byte[]> pages;
        using (var input = File.OpenRead(pdfPath)) pages = new PdfSplitService().SplitPdf(input);
        Check(pages.Count == 3, "existing splitter still creates three pages with viewer dependency present");
        for (var i = 0; i < pages.Count; i++) File.WriteAllBytes(Path.Combine(flyerFolder, $"page-{i + 1:000}.pdf"), pages[i]);
        var db = Path.Combine(Root, "drafts.db"); var saver = new ExtractionSaveService(db);
        for (var i = 1; i <= 3; i++) saver.SaveAsync(new FlyerFile { Retailer = "Fixture", FileName = "fixture.pdf" }, new FlyerPage { FileName = $"page-{i:000}.pdf", PageNumber = i }, new[] { new ExtractedProduct { ProductName = "Fixture product " + i, Price = "$2.50", Brand = "Fixture brand", Variant = "Original", ImageFilePath = null } }).GetAwaiter().GetResult();
        var commit = new SqlCommitService(db); commit.ClaimAsync(2).GetAwaiter().GetResult(); commit.MarkCommittedAsync(2).GetAwaiter().GetResult(); commit.ClaimAsync(3).GetAwaiter().GetResult();
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var providerCalls = 0;
        var window = new MainWindow(db, (_, _) => { providerCalls++; return Task.FromResult(new List<ExtractedProduct> { new() { ProductName = "New extraction fixture", Price = "$1" } }); }, loadFlyers: false, splitRoot: splitRoot) { Title = "Reviewer - MISSION 009C OFFLINE FIXTURE" };
        var list = (ListBox)window.FindName("SavedExtractionList"); var products = (ListBox)window.FindName("ProductList"); var preview = (PdfPageReview)window.FindName("SelectedPageReview");
        window.Show(); Pump();
        var entries = list.Items.Cast<SavedExtraction>().ToList();
        list.SelectedItem = entries.Single(x => x.Id == 1); Pump();
        Check(products.Items.Count == 1 && ((ExtractedProduct)products.Items[0]).ProductName == "Fixture product 1", "Saved selection loads exact SQLite batch");
        Check(((Button)window.FindName("SaveDataButton")).IsEnabled, "Saved extraction can Save");
        Check(preview.HasDocument && preview.LoadedPath == Path.Combine(flyerFolder, "page-001.pdf"), "Saved extraction resolves and embeds its own split PDF page");
        Eventually(() => preview.ZoomPercentage > 0 && preview.ZoomPercentage < 100, "initial fit width scales the PDF into the narrow review column");
        var originalPdf = File.ReadAllBytes(preview.LoadedPath!);
        preview.SetZoom(200); Pump(); Check(preview.ZoomPercentage == 200, "embedded viewer zooms to 200 percent");
        preview.FitWidth(); Pump(); Check(preview.HasDocument, "embedded Fit width retains loaded page");
        Eventually(() => preview.ZoomPercentage < 100, "Fit width replaces manual zoom after layout");
        var fitScroll = Visual<ScrollViewer>(preview)!;
        Eventually(() => fitScroll.ExtentWidth <= fitScroll.ViewportWidth - 8, "rendered PDF extent fits inside native viewport with margin");
        Eventually(() => fitScroll.ComputedHorizontalScrollBarVisibility != Visibility.Visible, "Fit width hides horizontal scrollbar");
        foreach (var manualZoom in new[] { 59, 34, 250 })
        {
            preview.SetZoom(manualZoom); Pump();
            ((Button)preview.FindName("FitWidthButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Eventually(() => fitScroll.ExtentWidth <= fitScroll.ViewportWidth - 8 && fitScroll.ComputedHorizontalScrollBarVisibility != Visibility.Visible,
                "Fit width button fits rendered page after manual " + manualZoom + "%");
        }
        window.Width = 1100; window.Height = 650; Pump();
        var smallZoom = preview.ZoomPercentage;
        window.Width = 1900; window.Height = 1000; Pump();
        Eventually(() => preview.ZoomPercentage > smallZoom, "Fit width grows with the current visible viewport");
        window.Width = 1100; window.Height = 650; Pump();
        Eventually(() => preview.ZoomPercentage <= smallZoom, "Fit width shrinks after restore to a small window");
        preview.SetZoom(175); Pump();
        var pdfScroll = Visual<ScrollViewer>(preview)!;
        pdfScroll.ScrollToVerticalOffset(150); pdfScroll.ScrollToHorizontalOffset(50); Pump();
        var offset = pdfScroll.VerticalOffset;
        Check(offset > 0, "native PDF scrolling is available at manual zoom");
        window.Width = 1600; Pump(); preview.RefreshForDisplayChange(); Pump();
        Check(preview.ZoomPercentage == 175, "manual zoom survives window and display refresh");
        Check(Math.Abs(pdfScroll.VerticalOffset - offset) < 1, "display refresh preserves manual vertical scrolling");
        var dpiHandler = typeof(PdfPageReview).GetMethod("OnDpiChanged", BindingFlags.Instance | BindingFlags.NonPublic)!;
        dpiHandler.Invoke(preview, new object[] { new DpiScale(1, 1), new DpiScale(2, 2) }); Pump();
        Check(preview.ZoomPercentage == 175 && Math.Abs(pdfScroll.VerticalOffset - offset) < 1, "simulated DPI notification preserves manual zoom and scroll");
        window.WindowState = WindowState.Maximized; Pump();
        Check(preview.ZoomPercentage == 175, "manual zoom survives maximize");
        window.WindowState = WindowState.Normal; Pump();
        preview.FitWidth(); Pump();
        Eventually(() => preview.ZoomPercentage < 175, "Fit width resumes after manual zoom and restore");
        var fitZoom = preview.ZoomPercentage;
        dpiHandler.Invoke(preview, new object[] { new DpiScale(2, 2), new DpiScale(1.25, 1.25) }); Pump();
        Check(preview.ZoomPercentage == fitZoom, "DPI notification does not double-scale unchanged logical viewport");
        Console.WriteLine($"VIEW {pdfScroll.ViewportWidth} EXT {pdfScroll.ExtentWidth} DPI {VisualTreeHelper.GetDpi(preview).DpiScaleX} HBAR {pdfScroll.ComputedHorizontalScrollBarVisibility}"); Eventually(() => pdfScroll.ScrollableWidth < 2, "Fit width leaves no cropped horizontal page content");
        window.Width = 1400; window.Height = 800; Pump();
        Check(products.ActualWidth > 0 && preview.ActualWidth > 0 && products.TranslatePoint(new System.Windows.Point(), window).X < preview.TranslatePoint(new System.Windows.Point(), window).X, "product cards remain visible beside PDF review");
        var p = (ExtractedProduct)products.Items[0]; p.ProductName = "Edited saved product"; p.Brand = "Updated brand"; p.Variant = "Updated variant"; p.PackSizeText = "2 kg"; p.Price = "$4"; p.RegularPrice = "$5"; p.Saving = "$1"; p.UnitPrice = "$2/kg"; p.OfferQuantity = 2; p.Promotion = "Any two"; p.Category = "Fruit & Vegetables"; p.PrimaryCategory = p.Category; p.Categories = new(){p.Category}; p.PromotionTypes = new(){"Multi-buy"}; p.Barcode = "fixture-only-barcode";
        saver.UpdateSavedExtractionAsync(1, products.Items.Cast<ExtractedProduct>()).GetAwaiter().GetResult();
        var loaded = commit.LoadProductsAsync(entries.Single(x => x.Id == 1)).GetAwaiter().GetResult().Single();
        Check(loaded.ProductName == p.ProductName && loaded.Brand == p.Brand && loaded.Variant == p.Variant && loaded.PackSizeText == p.PackSizeText, "Saved identity fields persist through exact-ID Save/reload");
        Check(loaded.Price == "$4" && loaded.RegularPrice == "$5" && loaded.Saving == "$1" && loaded.UnitPrice == "$2/kg" && loaded.OfferQuantity == 2 && loaded.Promotion == "Any two", "Saved offer fields persist through exact-ID Save/reload");
        Check(loaded.Categories.SequenceEqual(p.Categories) && loaded.PrimaryCategory == p.PrimaryCategory && loaded.PromotionTypes.SequenceEqual(p.PromotionTypes) && loaded.Barcode == p.Barcode, "categories, promotions and existing barcode metadata persist");
        list.SelectedItem = entries.Single(x => x.Id == 2); Pump();
        Check(preview.LoadedPath == Path.Combine(flyerFolder,"page-002.pdf") && ((ExtractedProduct)products.Items[0]).ProductName == "Fixture product 2", "Committed selection loads its own JSON and PDF");
        Eventually(() => preview.ZoomPercentage > 0 && preview.ZoomPercentage < 100, "changing saved batches refits the new PDF to the review column");
        Check(!((Button)window.FindName("SaveDataButton")).IsEnabled && !((Button)window.FindName("CommitButton")).IsEnabled && !((Button)window.FindName("AddOfferButton")).IsEnabled, "Committed view blocks Save, insertion and recommit");
        Check(((TextBlock)window.FindName("EditingContextText")).Text.Contains("inspection only"), "Committed view explains unsupported SQL corrections");
        try { saver.UpdateSavedExtractionAsync(2,new[]{new ExtractedProduct{ProductName="Must not persist",Price="$7"}}).GetAwaiter().GetResult(); throw new Exception("Committed update accepted"); } catch(InvalidOperationException) { Check(true,"SQLite service refuses Committed update"); }
        Check(((Button)window.FindName("GetDataButton")).Visibility == Visibility.Collapsed && providerCalls == 0, "Committed selection does not retry extraction or call provider");
        list.SelectedItem = entries.Single(x => x.Id == 3); Pump();
        Check(!((Button)window.FindName("SaveDataButton")).IsEnabled && !((Button)window.FindName("CommitButton")).IsEnabled && preview.HasDocument, "Recovery batch remains inspection-only with PDF review");
        var pageList = (ListBox)window.FindName("PageList");
        var fresh = new FlyerPage { FileName = "fresh.pdf", FullPath = Path.Combine(flyerFolder,"page-001.pdf"), PageNumber = 4 };
        pageList.Items.Add(fresh); pageList.SelectedItem = fresh; Pump();
        Check(list.SelectedItem == null && products.Items.Count == 0, "new page clears stale Saved batch and product-card context");
        Check(((Button)window.FindName("GetDataButton")).Visibility == Visibility.Visible && preview.HasDocument, "unsaved page still supports preview and explicit extraction");
        typeof(MainWindow).GetField("_selectedFlyer",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(window,new FlyerFile{Retailer="Fixture",FileName="fresh.pdf"});
        ((Button)window.FindName("GetDataButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
        Check(providerCalls == 1 && products.Items.Count == 1 && ((ExtractedProduct)products.Items[0]).ProductName == "New extraction fixture", "explicit extraction still runs against injected offline provider");
        var savedPage = new FlyerPage { FileName="page-001.pdf",FullPath=Path.Combine(flyerFolder,"page-001.pdf"),PageNumber=1,ExtractionRunId=1,ExtractionStatus="Saved" };
        pageList.Items.Add(savedPage); pageList.SelectedItem=savedPage; Pump();
        Check(list.SelectedItem is SavedExtraction selected && selected.Id==1 && ((ExtractedProduct)products.Items[0]).ProductName=="Edited saved product", "saved page reopens its exact extraction without provider call");
        Check(pageList.SelectedItem==savedPage && providerCalls==1,"saved-page selection remains selected and does not re-extract");
        using(var input=File.OpenRead(savedPage.FullPath)) using(var thumbnail=new MemoryStream()) { PDFtoImage.Conversion.SavePng(thumbnail,input,page:0);Check(thumbnail.Length>0,"existing PDFtoImage rendering works after embedded viewer loads"); }
        Check(File.ReadAllBytes(savedPage.FullPath).SequenceEqual(originalPdf),"zoom and PDF review never change the source PDF");
        Check(!preview.LoadPage(Path.Combine(Root,"missing.pdf")) && !preview.HasDocument && preview.LoadedPath==null,"missing PDF clears stale preview without losing product cards");
        Check(products.Items.Count==1,"missing PDF does not clear extracted products");
        var bad=Path.Combine(Root,"bad.pdf");File.WriteAllText(bad,"not a PDF");Check(!preview.LoadPage(bad) && !preview.HasDocument,"invalid PDF fails safely without retaining old document");
        preview.LoadPage(savedPage.FullPath); Pump(); Check(preview.HasDocument,"valid PDF loads again after a preview error");
        RenderedFitChecks();
        Console.WriteLine($"PASS: {checks} Saved editing/PDF checks; disposable fixtures only. {Root}");
        if(ui) { window.Closed += (_,_) => app.Shutdown(); Console.WriteLine("UI FIXTURE READY"); app.Run(); } else window.Close();
    }
}
