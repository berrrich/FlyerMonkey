using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FlyerMonkey.Reviewer.Windows;
using Syncfusion.Pdf;

internal static partial class Program
{
    static ScrollViewer? PdfScroll(DependencyObject parent)
    {
        if (parent is ScrollViewer { Name: "PART_ScrollViewer" } scroll) return scroll;
        for (var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
            if (PdfScroll(VisualTreeHelper.GetChild(parent,i)) is { } child) return child;
        return null;
    }
    static void RenderedFitChecks()
    {
        foreach(var landscape in new[]{false,true})
        {
            var path=Path.Combine(Root, landscape ? "wide.pdf" : "tall.pdf");
            using(var pdf=new PdfDocument())
            {
                pdf.PageSettings.Size=new Syncfusion.Drawing.SizeF(landscape?1200:600,landscape?600:1200);
                var page=pdf.Pages.Add();
                page.Graphics.DrawRectangle(Syncfusion.Pdf.Graphics.PdfPens.Black,0,0,page.GetClientSize().Width-1,page.GetClientSize().Height-1);
                pdf.Save(path);
            }
            var review=new PdfPageReview { Height=500 };
            var outer=new ScrollViewer { Content=review, HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled };
            var host=new Window { Width=720,Height=700,Content=outer };
            host.Show(); Pump(); Check(review.LoadPage(path),"loads " +(landscape?"landscape":"portrait")+" fit fixture");
            foreach(var scale in new[]{1d,1.25,2d})
            {
                review.LayoutTransform=new ScaleTransform(scale,scale); Pump();
                review.SetZoom(59); Pump();
                var scroll=PdfScroll(review)!;
                Check(scroll!=outer,"fit targets native PDF scroll viewport inside nested ScrollViewer");
                ((Button)review.FindName("FitWidthButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Eventually(()=>scroll.ExtentWidth<=scroll.ViewportWidth-8 && scroll.ComputedHorizontalScrollBarVisibility!=Visibility.Visible,
                    $"rendered page fits without horizontal scrollbar at layout scale {scale}, landscape={landscape}");
                Console.WriteLine($"FIT evidence: zoom={review.ZoomPercentage}% extent={scroll.ExtentWidth:F2} viewport={scroll.ViewportWidth:F2} dpi={VisualTreeHelper.GetDpi(review).DpiScaleX} layoutScale={scale}");
                host.Width=620; Pump();
                Eventually(()=>scroll.ExtentWidth<=scroll.ViewportWidth-8,"active fit follows smaller nested viewport");
                review.SetZoom(59); Pump(); host.Width=720; Pump();
                Check(review.ZoomPercentage==59,"manual zoom retained across nested viewport resize");
            }
            review.Dispose(); host.Close();
        }
    }
}
