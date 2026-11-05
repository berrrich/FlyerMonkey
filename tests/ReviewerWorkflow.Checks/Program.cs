using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FlyerMonkey.Reviewer.Windows;
using FlyerMonkey.Reviewer.Windows.Models;
using FlyerMonkey.Reviewer.Windows.Services;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Graphics;

internal static partial class Program
{
    static int checks;
    static readonly string Root = Path.GetFullPath(Path.Combine(".artifacts","reviewer-workflow",Guid.NewGuid().ToString("N")));
    static void Check(bool ok,string name){if(!ok)throw new Exception("FAIL "+name);checks++;Console.WriteLine("PASS "+name);}
    static async Task Fails<T>(Func<Task> action,string name) where T:Exception {try{await action();}catch(T){Check(true,name);return;}throw new Exception("FAIL "+name);}
    static byte[] Jpeg(byte shade=100){var encoder=new JpegBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(16,16,96,96,PixelFormats.Bgr24,null,Enumerable.Repeat(shade,768).ToArray(),48)));using var memory=new MemoryStream();encoder.Save(memory);return memory.ToArray();}
    [STAThread] static int Main(string[] args){try{Directory.CreateDirectory(Root);if(args.Contains("--layout-only")){LayoutChecks();Console.WriteLine($"PASS: {checks} focused window/thumbnail WPF checks. {Root}");return 0;}if(args.Contains("--ui")){Ui();return 0;}Offline().GetAwaiter().GetResult();UiChecks();Console.WriteLine($"PASS: {checks} workflow/progress/data-quality checks. {Root}");return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
    static async Task Offline()
    {
        foreach(var test in new[]{("Ham","$13/kg","$13/kg",""),("Boneless leg","$22/kg","$22/kg",""),("500g sliced ham pack","500g","500g sliced ham pack","500g"),("1kg chicken tray","1kg","1kg chicken tray","1kg"),("6 pack sausages","6 pack","6 pack sausages","6 pack"),("Ham with printed pack and price","500g","500g sliced ham $13/kg","500g"),("Ham with unit-price denominator","100g","$2/100g","")}){
            var product=new ExtractedProduct{ProductName=test.Item1,Category="Meat",PackSizeText=test.Item2,PackSizeEvidence=test.Item3};ExtractionNormalizationService.NormalizeFresh(product);Check(product.PackSizeText==test.Item4,test.Item1+" meat pack-size normalization");
        }
        var inferred=new ExtractedProduct{ProductName="Ham",Category="Deli",PackSizeText="1kg",Price="$13",UnitPrice="$13/kg",Saving="$5"};ExtractionNormalizationService.NormalizeFresh(inferred);Check(inferred.PackSizeText=="","price, unit price and savings cannot supply package evidence");
        var manual=new ExtractedProduct{ProductName="Ham",PackSizeText="750g",PackSizeManuallyCorrected=true};ExtractionNormalizationService.NormalizeFresh(manual);Check(manual.PackSizeText=="750g","manual pack-size correction survives normalization");
        var clock=new DateTimeOffset(2026,11,5,0,0,0,TimeSpan.Zero);var progress=new OperationProgress(()=>clock);using(var ticket=progress.Start("Saving SQLite draft")){clock=clock.AddSeconds(12);progress.RefreshElapsed();Check(progress.IsRunning&&progress.Timing=="00:12","running status reports operation and elapsed time");Check(progress.Recent.Count==0,"no completion recorded while persistence is pending");ticket.Success("Persisted");Check(progress.State=="Success"&&progress.Recent[0].Completed==clock,"successful completion records timestamp and summary");ticket.Fail("late failure");Check(progress.State=="Success","terminal result cannot be overwritten by stale completion");}
        var stale=progress.Start("Old operation");using(var current=progress.Start("New operation")){stale.Success("stale");Check(progress.IsRunning&&progress.Name=="New operation","stale asynchronous callback cannot replace newer operation");current.Cancel("local cancellation");}
        for(var i=0;i<10;i++){using var task=progress.Start("Task "+i);task.Fail("failure");}Check(progress.Recent.Count==8&&progress.Recent[0].State=="Failure","recent activity bounded and newest failure retained");
        var folder=Path.Combine(Root,"images");var archive=new ArtworkArchive(folder);var p=new ExtractedProduct{ProductName="Sliced Ham",Variant="Smoked",PackSizeText="500g",Price="$4"};var bytes=Jpeg();
        var flow=new ClassicArtworkWorkflow(p,archive,(_,_)=>Task.FromResult(bytes));await flow.GenerateAsync(null,true,CancellationToken.None);
        var original=p.ImageFilePath!;var originalId=p.ArtworkId!;
        Check(File.Exists(original)&&!string.IsNullOrWhiteSpace(originalId),"one-click generation archives and assigns working image");
        Check(Path.GetFileName(original).Contains("Sliced-Ham-Smoked-500g-")&&Path.GetFileName(original).Contains(originalId),"friendly filename includes product, variant, size, date and unique identity");
        var record=JsonSerializer.Deserialize<ArtworkRecord>(await File.ReadAllTextAsync(Path.Combine(folder,"provenance",originalId+".json")))!;
        Check(record.ArtworkId==originalId&&record.Prompt.Contains("Sliced Ham")&&record.Sha256.Length==64,"stable artwork identity and immutable generation provenance persisted");
        var proposed=new ClassicArtworkWorkflow(p,archive,(_,_)=>Task.FromResult(Jpeg(200)));await proposed.GenerateAsync("less shadow",false,CancellationToken.None);
        var proposalPath=proposed.ProposedPath!;Check(proposed.HasProposal&&p.ImageFilePath==original&&File.Exists(proposalPath),"Edit Image archives proposal without changing working image");proposed.Close();Check(p.ImageFilePath==original&&File.Exists(proposalPath),"reject/close preserves current image and proposed collateral");
        await Fails<InvalidOperationException>(()=>proposed.AcceptAsync(),"closed dialog cannot silently accept proposal");
        var accepted=new ClassicArtworkWorkflow(p,archive,(_,_)=>Task.FromResult(Jpeg(180)));await accepted.GenerateAsync("natural colour",false,CancellationToken.None);await accepted.AcceptAsync();var replacement=p.ImageFilePath!;Check(replacement!=original&&p.ArtworkId!=originalId&&File.Exists(original),"explicit acceptance changes working identity and retains archived original");
        var db=Path.Combine(Root,"drafts.db");var saver=new ExtractionSaveService(db);await saver.SaveAsync(new FlyerFile{Retailer="Fixture",FileName="fixture.pdf"},new FlyerPage{FileName="page.pdf",PageNumber=1},new[]{p,manual});var batch=(await new ExtractionReadService(db).GetAllAsync()).Single();var loaded=await new SqlCommitService(db).LoadProductsAsync(batch);
        Check(loaded[0].ArtworkId==p.ArtworkId&&loaded[0].ImageFilePath==replacement,"working image and stable identity survive SQLite Save/reload");Check(loaded[1].PackSizeText=="750g"&&loaded[1].PackSizeManuallyCorrected,"manual pack-size edit survives SQLite Save/reload");
        await archive.DetachAsync(p);Check(p.ImageFilePath==null&&p.ArtworkId==null&&File.Exists(original)&&File.Exists(replacement),"Remove / Archive detaches without deleting artwork");
        Check(Directory.GetFiles(Path.Combine(folder,"provenance","events")).Any(f=>File.ReadAllText(f).Contains("Detached / archived")),"detach leaves append-only provenance event");
        var delayed=new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);using var cancel=new CancellationTokenSource();var late=new ClassicArtworkWorkflow(p,archive,(_,_)=>delayed.Task);var request=late.GenerateAsync(null,true,cancel.Token);cancel.Cancel();delayed.SetResult(bytes);await Fails<OperationCanceledException>(()=>request,"late completed provider response reports local cancellation");Check(p.ImageFilePath==null&&Directory.GetFiles(Path.Combine(folder,"provenance","events")).Any(f=>File.ReadAllText(f).Contains("Completed after cancellation")),"late output archived without stale working-image assignment");
        var closedResult=new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);var closedFlow=new ClassicArtworkWorkflow(p,archive,(_,_)=>closedResult.Task);var pending=closedFlow.GenerateAsync(null,false,CancellationToken.None);closedFlow.Close();closedResult.SetResult(bytes);await Fails<OperationCanceledException>(()=>pending,"closing Edit Image before response rejects late result");Check(p.ImageFilePath==null,"closed edit never changes working image");
        var before=Directory.GetFiles(folder,"*.jpg").Length;var fail=new ClassicArtworkWorkflow(p,archive,(_,_)=>throw new IOException("offline failure"));await Fails<IOException>(()=>fail.GenerateAsync(null,true,CancellationToken.None),"provider failure reported without retry");Check(p.ImageFilePath==null&&Directory.GetFiles(folder,"*.jpg").Length==before,"failed generation preserves working reference and archive");
        var legacyPath=Path.Combine(Root,"legacy.jpg");await File.WriteAllBytesAsync(legacyPath,bytes);p.ImageFilePath=legacyPath;await archive.DetachAsync(p);Check(File.ReadAllBytes(legacyPath).SequenceEqual(bytes),"legacy image is retained in place with provenance when detached");
        foreach(var width in new[]{640d,800d,1024d,1400d}){var c=ReviewerWindowLayout.Columns(width);Check(c.Products>=220&&width-c.Flyers-c.Pages-c.Products-c.Saved>=219.9,"responsive layout reserves cards and PDF at "+width+" logical pixels");}
    }
    static void Pump()=>Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
    static void Until(Func<bool> condition,string text){var watch=System.Diagnostics.Stopwatch.StartNew();while(!condition()&&watch.Elapsed.TotalSeconds<8){Pump();Thread.Sleep(10);}Check(condition(),text);}
    static (MainWindow Window,ListBox List,ListBox Products) Fixture(Func<string,CancellationToken,Task<byte[]>> image, Action<MainWindow>? configure = null)
    {
        Environment.SetEnvironmentVariable("FLYERMONKEY_SQL_CONNECTION",null);Environment.SetEnvironmentVariable("FLYERMONKEY_BLOB_CONNECTION",null);Environment.SetEnvironmentVariable("FLYERMONKEY_OPENAI_KEY",null);
        var db=Path.Combine(Root,"ui-"+Guid.NewGuid().ToString("N")+".db");new ExtractionSaveService(db).SaveAsync(new FlyerFile{Retailer="Fixture",FileName="fixture.pdf"},new FlyerPage{FileName="page-001.pdf",PageNumber=1},new[]{new ExtractedProduct{ProductName="Ham",Price="$13",PackSizeText="",Category="Deli"}}).GetAwaiter().GetResult();
        var app=Application.Current??new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var split=Path.Combine(Root,"Split","fixture");Directory.CreateDirectory(split);Syncfusion.Telemetry.Telemetry.Disable();Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense(Environment.GetEnvironmentVariable("FLYERMONKEY_SYNCFUSION_KEY"));
        using(var pdf=new PdfDocument()){var page=pdf.Pages.Add();for(var i=0;i<30;i++)page.Graphics.DrawString("PDF layout fixture - line "+i,new PdfStandardFont(PdfFontFamily.Helvetica,14),PdfBrushes.Black,new Syncfusion.Drawing.PointF(20,30+i*22));pdf.Save(Path.Combine(split,"page-001.pdf"));}
        var window=new MainWindow(db,(_,_)=>Task.FromResult(new List<ExtractedProduct>()),false,Path.Combine(Root,"Split"),image,Path.Combine(Root,"ui-images")){Title="Reviewer - MISSION 009D OFFLINE FIXTURE"};configure?.Invoke(window);window.Show();Pump();var list=(ListBox)window.FindName("SavedExtractionList");list.SelectedIndex=0;Pump();return(window,list,(ListBox)window.FindName("ProductList"));
    }
    static IEnumerable<T> Children<T>(DependencyObject parent) where T:DependencyObject {for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);if(child is T typed)yield return typed;foreach(var descendant in Children<T>(child))yield return descendant;}}
    static void UiChecks() => ConcurrentCraterChecks();
    static void Ui(){var f=Fixture(async(_,token)=>{await Task.Delay(TimeSpan.FromSeconds(15),token);return Jpeg(150);});f.Window.Closed+=(_,_)=>Application.Current.Shutdown();Console.WriteLine("Fake-only UI fixture: "+Root);Application.Current.Run();}
}
