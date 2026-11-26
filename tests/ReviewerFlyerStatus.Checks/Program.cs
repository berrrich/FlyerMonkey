using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using FlyerMonkey.Reviewer.Windows;
using FlyerMonkey.Reviewer.Windows.Services;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Graphics;

class Program
{
    static int checks;
    static void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); checks++; }
    static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => {}, DispatcherPriority.ApplicationIdle);
    static T? Find<T>(DependencyObject root, string name) where T: FrameworkElement
    { if(root is T item && item.Name == name) return item; for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++) if(Find<T>(VisualTreeHelper.GetChild(root,i),name) is {} result) return result; return null; }
    [STAThread] static int Main()
    {
        try { Run(); Console.WriteLine($"{checks} checks passed"); return 0; } catch(Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    static void Run()
    {
        Syncfusion.Telemetry.Telemetry.Disable();
        Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense(Environment.GetEnvironmentVariable("FLYERMONKEY_SYNCFUSION_KEY"));
        var root=Path.GetFullPath(Path.Combine(".artifacts","reviewer-flyer-status",Guid.NewGuid().ToString("N"))); Directory.CreateDirectory(root);
        Console.WriteLine(root);
        var splits=Path.Combine(root,"Split"); Directory.CreateDirectory(splits);
        var flyer=new FlyerFile { Retailer="Coles",FileName="fixture.pdf",FullPath=Path.Combine(root,"Coles","fixture.pdf") };
        FlyerSplitState Discover(params FlyerFile[] others) => FlyerStatusService.Discover(splits,flyer,new[]{flyer}.Concat(others));
        Check(Discover().Pages.Length==0 && Discover().Warning=="","absent folder neutral");
        Directory.CreateDirectory(Path.Combine(splits,"fixture.pending-fixture"));
        Check(Discover().Pages.Length==0 && Discover().Warning.Contains("Incomplete"),"abandoned staging folder reported without publishing");
        var folder=Path.Combine(splits,"fixture"); Directory.CreateDirectory(folder);
        Check(Discover().Pages.Length==0 && Discover().Warning.Contains("Incomplete"),"empty folder not split and reported");
        File.WriteAllText(Path.Combine(folder,"page-001.pdf"),"invalid");
        Check(Discover().Pages.Length==0 && Discover().Warning.Contains("Incomplete"),"corrupt PDF not split");
        using(var pdf=new PdfDocument()) {pdf.Pages.Add();pdf.Save(Path.Combine(folder,"page-001.pdf"));}
        var bytes=File.ReadAllBytes(Path.Combine(folder,"page-001.pdf"));
        Check(Discover().Pages.Length==1,"valid completed single page found");
        File.WriteAllText(Path.Combine(folder,"page-003.pdf"),"invalid");
        Check(Discover().Pages.Length==1 && Discover().Warning.Contains("Incomplete"),"valid page retained while incomplete folder reported");
        var other=new FlyerFile{Retailer="Woolworths",FileName="fixture.pdf",FullPath=Path.Combine(root,"Woolworths","fixture.pdf")};
        Check(Discover(other).Pages.Length==0 && Discover(other).Warning.Contains("Ambiguous"),"cross retailer collision blocks association");
        var duplicate=new FlyerFile{Retailer="Coles",FileName="fixture.pdf",FullPath=Path.Combine(root,"other","fixture.pdf")};
        Check(Discover(duplicate).Pages.Length==0,"same retailer duplicate source blocks association");
        Directory.CreateDirectory(Path.GetDirectoryName(flyer.FullPath)!);
        using(var pdf=new PdfDocument()) {pdf.Pages.Add();pdf.Pages.Add();pdf.Save(flyer.FullPath);}
        Check(Discover().Warning.Contains("Incomplete"),"missing trailing page reported against source page count");
        var db=Path.Combine(root,"state.db");var service=new FlyerStatusService(db);
        Check(!service.IsComplete(flyer),"new flyer incomplete"); service.SetComplete(flyer,true);
        var restarted=new FlyerStatusService(db); Check(restarted.IsComplete(flyer),"completion survives new service connection");
        Check(!restarted.IsComplete(other),"completion isolated by retailer");
        var moved=new FlyerFile{Retailer="coles",FileName="FIXTURE.PDF",FullPath=Path.Combine(root,"moved","fixture.pdf")};
        Check(restarted.IsComplete(moved),"stable identity survives path move and Windows case");
        flyer.SetStatus(true,Discover());Check(flyer.StatusColour=="#8BD49B","green overrides blue");
        restarted.SetComplete(flyer,false);Check(!new FlyerStatusService(db).IsComplete(flyer),"reopen persists");
        flyer.SetStatus(false,Discover());Check(flyer.StatusColour=="#70B8F0","reopen restores blue");
        flyer.SetStatus(false,Discover(other));Check(flyer.StatusColour=="White","collision neutral");
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var window=new MainWindow(db,loadFlyers:false,splitRoot:splits);window.Show();Pump();
        var list=(ListBox)window.FindName("FlyerList");list.Items.Add(flyer);
        // Detach date confirmation for selection-template assertions only.
        var handler=typeof(MainWindow).GetMethod("FlyerList_SelectionChanged",BindingFlags.NonPublic|BindingFlags.Instance)!;
        list.SelectionChanged-=(SelectionChangedEventHandler)Delegate.CreateDelegate(typeof(SelectionChangedEventHandler),window,handler);
        list.SelectedItem=flyer;Pump();list.UpdateLayout();
        var products=(ListBox)window.FindName("ProductList");
        ((System.Collections.ObjectModel.ObservableCollection<FlyerMonkey.Reviewer.Windows.Models.ExtractedProduct>)products.ItemsSource).Add(new(){ProductName="Unsaved fixture",Price="$1"});
        Check((bool)typeof(MainWindow).GetProperty("HasPotentialUnsavedOffers",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(window)!,"editable offers trigger unsaved warning guard");
        ((System.Collections.ObjectModel.ObservableCollection<FlyerMonkey.Reviewer.Windows.Models.ExtractedProduct>)products.ItemsSource).Clear();
        var container=(ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(flyer);
        foreach(var complete in new[]{false,true}) {
            flyer.SetStatus(complete,Discover());Pump();
            var frame=Find<Border>(container,"SelectionFrame")!;
            Check(((SolidColorBrush)frame.BorderBrush).Color == ((SolidColorBrush)window.FindResource("ReviewerAccent")).Color,
                "visible selection on "+(complete?"green":"blue"));
        }
        list.SelectedItem=null;Pump();Check(Find<Border>(container,"SelectionFrame")!.BorderBrush==Brushes.Transparent || ((SolidColorBrush)Find<Border>(container,"SelectionFrame")!.BorderBrush).Color==Colors.Transparent,"deselection clears outline");
        typeof(MainWindow).GetField("_selectedPage",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(window,new FlyerPage());
        Check((bool)typeof(MainWindow).GetProperty("HasPotentialUnsavedOffers",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(window)!,"deleting every offer still triggers unsaved warning guard");
        typeof(MainWindow).GetField("_selectedPage",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(window,null);
        // Actual completion handler, with no offers: no confirmation or external service needed.
        typeof(MainWindow).GetField("_selectedFlyer",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(window,flyer);
        flyer.SetStatus(false,Discover());
        var button=(Button)window.FindName("FlyerCompletionButton");button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
        Check(new FlyerStatusService(db).IsComplete(flyer) && flyer.IsComplete,"actual Mark Complete handler persists");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();Check(!new FlyerStatusService(db).IsComplete(flyer) && !flyer.IsComplete,"actual Reopen handler persists");
        service.SetComplete(flyer,true);window.Close();
        var next=new MainWindow(db,loadFlyers:false,splitRoot:splits);next.Show();
        typeof(MainWindow).GetMethod("RefreshFlyerStatus",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(next,new object[]{flyer});
        Check(flyer.IsComplete && flyer.StatusColour=="#8BD49B","new window reloads persisted green");
        Check(bytes.SequenceEqual(File.ReadAllBytes(Path.Combine(folder,"page-001.pdf"))),"existing split bytes unchanged");
        CheckFlyerSort(next, root);
        next.Close();app.Shutdown();
    }

    static void CheckFlyerSort(MainWindow window, string root)
    {
        var incoming = Path.Combine(root, "Incoming");
        void Add(string retailer, string name, DateTime modified, string? cover = null)
        {
            var folder = Path.Combine(incoming, retailer);
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, name);
            using (var pdf = new PdfDocument())
            {
                var page = pdf.Pages.Add();
                if (cover is not null)
                    page.Graphics.DrawString(cover, new PdfStandardFont(PdfFontFamily.Helvetica, 14),
                        PdfBrushes.Black, new Syncfusion.Drawing.PointF(20, 20));
                pdf.Save(path);
            }
            File.SetLastWriteTime(path, modified);
        }

        Add("Coles", "COLWAMETRO_280526_TEST.pdf", new DateTime(2026, 8, 1));
        Add("Woolworths", "WW_WA_030626_TEST.pdf", new DateTime(2026, 5, 1));
        Add("Independent", "cover.pdf", new DateTime(2026, 5, 1), "Thu 4 Jun to Wed 10 Jun 2026");
        Add("Independent", "beta.pdf", new DateTime(2026, 6, 2, 13, 0, 0));
        Add("Independent", "Alpha.pdf", new DateTime(2026, 6, 2, 13, 0, 0));
        Add("Independent", "new-file.pdf", new DateTime(2026, 6, 3, 12, 0, 0));

        var load = typeof(MainWindow).GetMethod("LoadFlyersFrom", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var list = (ListBox)window.FindName("FlyerList");
        var pages = (ListBox)window.FindName("PageList");
        void Reload() => load.Invoke(window, [incoming]);
        Reload();
        Check(list.Items.Cast<FlyerFile>().Select(flyer => flyer.FileName).SequenceEqual(new[]
        {
            "cover.pdf", "new-file.pdf", "WW_WA_030626_TEST.pdf", "Alpha.pdf", "beta.pdf", "COLWAMETRO_280526_TEST.pdf"
        }), "flyers sort by cover or retailer date, then modification time and stable filename");

        var handler = typeof(MainWindow).GetMethod("FlyerList_SelectionChanged", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var selection = (SelectionChangedEventHandler)Delegate.CreateDelegate(typeof(SelectionChangedEventHandler), window, handler);
        list.SelectionChanged -= selection;
        var selected = (FlyerFile)list.Items[2];
        list.SelectedItem = selected;
        typeof(MainWindow).GetField("_selectedFlyer", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, selected);
        list.SelectionChanged += selection;
        var page = new FlyerPage { PageNumber = 2, FileName = "page-002.pdf" };
        pages.Items.Add(page);
        pages.SelectedItem = page;
        Add("Independent", "first.pdf", new DateTime(2026, 6, 5));
        Reload();
        Check(((FlyerFile)list.Items[0]).FileName == "first.pdf" && ReferenceEquals(list.SelectedItem, selected)
            && ReferenceEquals(pages.SelectedItem, page), "refresh inserts newer flyer without changing the selected flyer or page");
    }
}
