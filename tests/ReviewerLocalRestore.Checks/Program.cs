using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using FlyerMonkey.Reviewer.Windows;
using FlyerMonkey.Reviewer.Windows.Models;
using FlyerMonkey.Reviewer.Windows.Services;
using FlyerMonkey.Shared.Model;
using SQLServerConnection.Data;

internal static class Program
{
    static string Sql = "", Database = "", Root = "", Prefix = "";
    static readonly List<string> Passed = new();
    static object? finalReport;
    static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL " + name);
        Passed.Add(name); Console.WriteLine("PASS " + name);
    }
    static object Scalar(string sql)
    {
        using var c = new SqlConnection(Sql); c.Open();
        using var cmd = new SqlCommand(sql, c) { CommandTimeout = 30 };
        return cmd.ExecuteScalar()!;
    }
    static void Execute(string sql)
    {
        using var c = new SqlConnection(Sql); c.Open();
        using var cmd = new SqlCommand(sql, c) { CommandTimeout = 30 }; cmd.ExecuteNonQuery();
    }
    static int[] Counts() => new[] { "Offers", "Products", "ProductCategories" }.Select(t => Convert.ToInt32(Scalar("SELECT COUNT(*) FROM dbo.[" + t + "]"))).ToArray();
    static string Status(string path) {
        using var c = new SqliteConnection("Data Source=" + path + ";Mode=ReadOnly"); c.Open();
        using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT Status FROM ExtractionRuns WHERE ID=1";
        return (string)cmd.ExecuteScalar()!;
    }
    static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    static void Until(Func<bool> condition, string label)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && watch.Elapsed.TotalSeconds < 40) { Pump(); Thread.Sleep(15); }
        Check(condition(), label);
    }
    static string Snapshot(int maxOffer, int maxProduct)
    {
        // Ordinary scalar subquery avoids FOR JSON's multi-row output chunking.
        var json = (string)Scalar($"""
SELECT (SELECT
 (SELECT * FROM dbo.Offers WHERE ID<={maxOffer} ORDER BY ID FOR JSON PATH,INCLUDE_NULL_VALUES) Offers,
 (SELECT * FROM dbo.Products WHERE ID<={maxProduct} ORDER BY ID FOR JSON PATH,INCLUDE_NULL_VALUES) Products,
 (SELECT * FROM dbo.ProductCategories WHERE ProductID<={maxProduct} ORDER BY ProductID,CategoryID FOR JSON PATH,INCLUDE_NULL_VALUES) Links,
 (SELECT * FROM dbo.Categories ORDER BY ID FOR JSON PATH,INCLUDE_NULL_VALUES) Categories,
 (SELECT * FROM dbo.Retailers ORDER BY ID FOR JSON PATH,INCLUDE_NULL_VALUES) Retailers,
 (SELECT * FROM dbo.RetailerLocations ORDER BY ID FOR JSON PATH,INCLUDE_NULL_VALUES) Locations,
 (SELECT * FROM dbo.ProviderListings ORDER BY ID FOR JSON PATH,INCLUDE_NULL_VALUES) Listings,
 (SELECT * FROM dbo.OfferPromotionTypes ORDER BY OfferID,PromotionTypeID FOR JSON PATH,INCLUDE_NULL_VALUES) OfferPromotions,
 (SELECT * FROM dbo.PromotionTypes ORDER BY ID FOR JSON PATH,INCLUDE_NULL_VALUES) Promotions,
 (SELECT * FROM dbo.DeliveryProviders ORDER BY ID FOR JSON PATH,INCLUDE_NULL_VALUES) Providers,
 (SELECT * FROM dbo.Customers ORDER BY CustomerId FOR JSON PATH,INCLUDE_NULL_VALUES) Customers
 FOR JSON PATH,WITHOUT_ARRAY_WRAPPER,INCLUDE_NULL_VALUES)
""");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }
    static (string Path, string Source) Draft(string label)
    {
        var path = Path.Combine(Root, label + ".db"); var source = Prefix + "-" + label + ".pdf";
        new ExtractionSaveService(path).SaveAsync(new FlyerFile { Retailer = "Woolworths", FileName = source },
            new FlyerPage { FileName = source, PageNumber = 1 }, new[] {
                new ExtractedProduct { ProductName=Prefix+"-"+label+"-milk",Price="$2.50",Category="Deli",PrimaryCategory="Deli",Categories=new(){"Deli"} },
                new ExtractedProduct { ProductName=Prefix+"-"+label+"-bread",Price="$3.00",Category="Deli",PrimaryCategory="Deli",Categories=new(){"Deli"} }
            }).GetAwaiter().GetResult();
        return (path, source);
    }
    static MainWindow Fixture(string path)
    {
        var window = new MainWindow(path, (_, _) => throw new Exception("Extraction forbidden"), false,
            Path.Combine(Root, "split"), (_, _) => throw new Exception("Paid generation forbidden"), Path.Combine(Root, "images"));
        var list = (ListBox)window.FindName("SavedExtractionList");
        Until(() => list.Items.Count == 1, "fixture loads one isolated saved batch");
        list.SelectedIndex = 0; Pump();
        Check(((ListBox)window.FindName("ProductList")).Items.Count == 2, "fixture loads two image-free products");
        return window;
    }
    static void SelectOnly(MainWindow window) {
        var list = (ListBox)window.FindName("SavedExtractionList"); list.SelectedIndex = 0; Pump();
    }
    static bool Busy(MainWindow window) => (bool)typeof(MainWindow).GetField("_processing", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    static void UiChecks()
    {
        var baseline = Counts();
        var success = Draft("rapid");
        var window = Fixture(success.Path);
        try {
            var button = (Button)window.FindName("CommitButton");
            using var dialogs = new DialogDriver();
            var burst = 0;
            dialogs.OnConfirm = () => {
                Check(!button.IsEnabled && Busy(window), "Commit disabled and processing gate held during confirmation");
                for (var i=0;i<25;i++) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); burst++; }
            };
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Until(() => !Busy(window), "rapid-click commit finishes");
            dialogs.ThrowIfFailed();
            Check(burst == 25 && dialogs.Confirmations == 1, "25 rapid reentrant clicks produce exactly one confirmation");
            Check(Status(success.Path) == "Committed", "rapid-click batch acknowledged Committed");
            Check(Counts().SequenceEqual(baseline.Select(x=>x+2)), "rapid clicks persist exactly two offers/products/category links");
            Check(Convert.ToInt32(Scalar($"SELECT COUNT(*) FROM dbo.Offers WHERE SourceDescription=N'{success.Source}'")) == 2, "rapid clicks create one two-offer source batch");
            SelectOnly(window);
            Check(!button.IsEnabled, "committed batch disables Commit button");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            dialogs.ThrowIfFailed();
            Check(dialogs.Confirmations == 1 && Counts().SequenceEqual(baseline.Select(x=>x+2)), "programmatic committed-batch resubmission writes nothing");
        } finally { window.Close(); Pump(); }

        var failed = Draft("sql-failure"); var beforeFailure = Counts();
        // A real constraint rejects the second offer after the first complete product/offer/link.
        // New disposable LOCAL database only; the constraint is retained with the test fixture.
        Execute($"ALTER TABLE dbo.Offers ADD CONSTRAINT [CK_009J_{Guid.NewGuid():N}] CHECK (NOT(SourceDescription=N'{failed.Source}' AND AdvertisedPrice=3.00))");
        var failingWindow = Fixture(failed.Path);
        try {
            var button = (Button)failingWindow.FindName("CommitButton");
            using var dialogs = new DialogDriver();
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Until(() => !Busy(failingWindow), "simulated SQL failure finishes");
            dialogs.ThrowIfFailed();
            Check(Counts().SequenceEqual(beforeFailure), "second-offer failure rolls back all products/offers/category links");
            Check(Status(failed.Path) == "Committing", "failed batch remains durably claimed for recovery");
            SelectOnly(failingWindow);
            var entry = ((ListBox)failingWindow.FindName("SavedExtractionList")).Items.Cast<SavedExtraction>().Single();
            Check(entry.RecoveryRequired && !button.IsEnabled, "failed batch displays Recovery Required and disables Commit");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            dialogs.ThrowIfFailed();
            Check(dialogs.Confirmations == 1 && Counts().SequenceEqual(beforeFailure), "retry click after failure is blocked without another SQL attempt");
            var refused=false;try{new SqlCommitService(failed.Path).ClaimAsync(1).GetAwaiter().GetResult();}catch(InvalidOperationException){refused=true;}
            Check(refused && Status(failed.Path) == "Committing", "fresh service also refuses uncertain retry; no status reset");
        } finally { failingWindow.Close(); Pump(); }
    }
    static async Task OverlapChecks()
    {
        var draft=Draft("overlap");var baseline=Counts();
        var start=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts=Enumerable.Range(0,12).Select(_=>Task.Run(async()=>{
            await start.Task;var service=new SqlCommitService(draft.Path);
            try{await service.ClaimAsync(1);}catch(InvalidOperationException){return false;}
            await using var batch=await ReviewerSqlBatch.OpenAsync(Sql);
            for(var i=0;i<2;i++){
                var product=await batch.AddProductAsync(new Product{Name=Prefix+"-overlap-"+i,Category="Deli"});
                await batch.SaveProductCategoriesAsync(product,"Deli",new[]{"Deli"});
                await batch.AddOfferAsync(new Offer{ProductID=product,RetailerID=1,RetailerLocationID=3,AdvertisedPrice=2.50m,
                    SourceType="Flyer",SourceDescription=draft.Source,FlyerPageNumber=1,ValidFrom=new DateTime(2026,11,10),ValidTo=new DateTime(2026,11,17)});
            }
            await batch.CommitAsync();await service.MarkCommittedAsync(1);return true;
        })).ToArray();
        start.SetResult();var outcomes=await Task.WhenAll(attempts);
        Check(outcomes.Count(x=>x)==1, "12 overlapping commit requests have exactly one durable-claim winner");
        Check(Counts().SequenceEqual(baseline.Select(x=>x+2)) && Status(draft.Path)=="Committed", "overlapping winner persists one complete two-offer batch");
        var refused=false;try{await new SqlCommitService(draft.Path).ClaimAsync(1);}catch(InvalidOperationException){refused=true;}
        Check(refused,"completed overlapping batch cannot be submitted again");
    }
    [STAThread] static int Main(string[] args)
    {
        try {
            if(args.Length!=1 || !System.Text.RegularExpressions.Regex.IsMatch(args[0],"^Mission009J_Disposable_[a-f0-9]{12}$"))throw new Exception("Explicit disposable LOCAL database required");
            Database=args[0];Sql=$"Server=(localdb)\\MSSQLLocalDB;Database={Database};Integrated Security=True;Encrypt=False;Enlist=False;Connect Timeout=30";
            Check(Convert.ToInt32(Scalar("SELECT SERVERPROPERTY('IsLocalDB')"))==1 && (string)Scalar("SELECT DB_NAME()")==Database,"hard-coded LocalDB and exact disposable database identity confirmed");
            Check(Counts().SequenceEqual(new[]{109,112,57}),"Reviewer test restore starts at archive baseline 109/112/57");
            Root=Path.GetFullPath(Path.Combine(".artifacts","mission009j","reviewer-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(Root);
            Prefix="Mission009J-"+Guid.NewGuid().ToString("N");
            // Override production settings only in this isolated child process.
            Environment.SetEnvironmentVariable("FLYERMONKEY_SQL_CONNECTION",Sql);
            Environment.SetEnvironmentVariable("FLYERMONKEY_BLOB_CONNECTION","UseDevelopmentStorage=true");
            Environment.SetEnvironmentVariable("FLYERMONKEY_OPENAI_KEY",null);
            Syncfusion.Telemetry.Telemetry.Disable();
            _=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var maxOffer=Convert.ToInt32(Scalar("SELECT MAX(ID) FROM dbo.Offers"));
            var maxProduct=Convert.ToInt32(Scalar("SELECT MAX(ID) FROM dbo.Products"));
            var original=Snapshot(maxOffer,maxProduct);
            UiChecks();
            Task.Run(OverlapChecks).GetAwaiter().GetResult();
            Check(Convert.ToInt32(Scalar($"SELECT COUNT(*) FROM dbo.Products p WHERE p.Name LIKE N'{Prefix}%'"))==4,"only four synthetic successful products persisted");
            Check(Convert.ToInt32(Scalar($"SELECT COUNT(*) FROM dbo.Products p WHERE p.Name LIKE N'{Prefix}%' AND (SELECT COUNT(*) FROM dbo.Offers o WHERE o.ProductID=p.ID)<>1"))==0,"every synthetic product has exactly one offer; no partial/orphan/duplicate fixture rows");
            Check(Snapshot(maxOffer,maxProduct)==original,"all original rows across all eleven archive tables remain unchanged");
            Check(Counts().SequenceEqual(new[]{113,116,61}),"Reviewer test final counts are baseline plus exactly four successful records");
            finalReport=new {Status="PASS",Database,Server=@"(localdb)\MSSQLLocalDB",Root,Checks=Passed.Count,Passed,FinalCounts=Counts(),
                RapidReentrantClicks=25,OverlappingRequests=12,SuccessfulTwoOfferBatches=2,FailureRetry="Blocked; Recovery Required; no automatic retry",
                OriginalArchiveRowsUnchanged=true,DatabaseRetained=true,ProductionConnections=0,AzureCalls=0,PaidCalls=0,AutomatedActualWpfHandlers=true,MouseDrivenVerification=false};
            File.WriteAllText(Path.Combine(Root,"results.json"),JsonSerializer.Serialize(finalReport,new JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine("RESULT "+Path.Combine(Root,"results.json"));return 0;
        }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }

    sealed class DialogDriver:IDisposable
    {
        readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(25)};
        readonly HashSet<string> seen=new();
        Exception? failure; public int Confirmations; public Action? OnConfirm;
        public DialogDriver(){timer.Tick+=Tick;timer.Start();}
        void Tick(object? sender,EventArgs args){
            EnumWindows((hwnd,_)=>{
                GetWindowThreadProcessId(hwnd,out var pid);
                if(pid!=Environment.ProcessId)return true;
                var cls=new StringBuilder(100);GetClassName(hwnd,cls,cls.Capacity);
                if(cls.ToString()!="#32770")return true;
                var title=new StringBuilder(256);GetWindowText(hwnd,title,title.Capacity);
                if(!seen.Add(hwnd.ToString()+":"+title))return true;
                try{
                    if(title.ToString()=="Commit to SQL"){Confirmations++;OnConfirm?.Invoke();PostMessage(hwnd,0x111,new IntPtr(6),IntPtr.Zero);}
                    else if(title.ToString()=="Commit"){DismissNotice(hwnd);}
                    else{failure=new Exception("Unexpected test dialog: "+title);DismissNotice(hwnd);}
                }catch(Exception ex){failure=ex;PostMessage(hwnd,0x111,new IntPtr(7),IntPtr.Zero);}
                return true;
            },IntPtr.Zero);
        }
        public void ThrowIfFailed(){if(failure!=null)throw failure;}
        public void Dispose()=>timer.Stop();
        static void DismissNotice(IntPtr hwnd){
            // This Windows message-box implementation exposes its OK button as ID 2.
            // Discover the existing button rather than assume IDOK's numeric value.
            var button=GetDlgItem(hwnd,1);if(button==IntPtr.Zero)button=GetDlgItem(hwnd,2);
            if(button==IntPtr.Zero)throw new Exception("Notice has no supported dismissal button");
            PostMessage(hwnd,0x111,new IntPtr(GetDlgCtrlID(button)),IntPtr.Zero);
        }
        delegate bool EnumProc(IntPtr window,IntPtr parameter);
        [DllImport("user32.dll")]static extern bool EnumWindows(EnumProc callback,IntPtr parameter);
        [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr hwnd,out int pid);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr hwnd,StringBuilder text,int max);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetWindowText(IntPtr hwnd,StringBuilder text,int max);
        [DllImport("user32.dll")]static extern bool PostMessage(IntPtr hwnd,uint message,IntPtr wParam,IntPtr lParam);
        [DllImport("user32.dll")]static extern IntPtr GetDlgItem(IntPtr hwnd,int id);
        [DllImport("user32.dll")]static extern int GetDlgCtrlID(IntPtr hwnd);
    }
}
