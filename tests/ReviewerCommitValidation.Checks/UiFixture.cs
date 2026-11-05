using System.IO;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using FlyerMonkey.Reviewer.Windows;
using FlyerMonkey.Reviewer.Windows.Models;
using FlyerMonkey.Reviewer.Windows.Services;

internal static partial class Program
{
    private static int RunUi(bool mission009a = false, bool sqlFailure = false)
    {
        Directory.CreateDirectory(FixtureRoot);
        var path=System.IO.Path.Combine(FixtureRoot,"ui-"+Guid.NewGuid().ToString("N")+".db");
        var sourcePage = mission009a ? (sqlFailure ? "ui-009a-error-page.pdf" : "ui-009a-page.pdf") : "ui-page.pdf";
        if (sqlFailure) Execute("IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE name='CK_009A_UiFailure') ALTER TABLE dbo.Offers ADD CONSTRAINT CK_009A_UiFailure CHECK (NOT(SourceDescription='ui-009a-error-page.pdf' AND AdvertisedPrice=3.00))").GetAwaiter().GetResult();
        new ExtractionSaveService(path).SaveAsync(new FlyerFile{Retailer="Fixture",FileName="ui-fixture.pdf"},new FlyerPage{FileName=sourcePage,PageNumber=1},new[]{new ExtractedProduct{ProductName="Fixture milk",Price=sqlFailure ? "$2.50" : ""},new ExtractedProduct{ProductName="Fixture bread",Price="$3.00"}}).GetAwaiter().GetResult();
        if (mission009a) {
            new ExtractionSaveService(path).SaveAsync(new FlyerFile{Retailer="Fixture",FileName="recovery-fixture.pdf"},new FlyerPage{FileName="recovery-required.pdf",PageNumber=2},new[]{new ExtractedProduct{ProductName="Recovery fixture product",Price="$4.00"}}).GetAwaiter().GetResult();
            new SqlCommitService(path).ClaimAsync(2).GetAwaiter().GetResult();
        }
        Console.WriteLine("UI SQLite fixture: "+path);
        Environment.SetEnvironmentVariable("FLYERMONKEY_SQL_CONNECTION",Sql);
        Environment.SetEnvironmentVariable("FLYERMONKEY_BLOB_CONNECTION","UseDevelopmentStorage=true");
        // No actual extraction client is instantiated. The delay makes cancellation observable.
        async Task<List<ExtractedProduct>> FakeExtraction(string _, CancellationToken token) {
            await Task.Delay(TimeSpan.FromSeconds(45), token);
            return new(){new ExtractedProduct{ProductName="Fake replacement",Price="$1.00"}};
        }
        var app=new Application();
        var window=new MainWindow(path,FakeExtraction,loadFlyers:false){Title="Reviewer - DISPOSABLE VALIDATION ONLY"};
        window.Loaded+=(_,_)=>{
            var list=(ListBox)window.FindName("SavedExtractionList");
            if(list.Items.Count>0) list.SelectedItem=sqlFailure ? list.Items.Cast<SavedExtraction>().First(x=>x.Status=="Saved") : list.Items[0];
        };
        var prepare=new Button{Content="Prepare fake extraction (no paid calls)",Margin=new Thickness(12),Padding=new Thickness(10)};
        var controls=new Window{Title="Disposable validation controls",Content=prepare,Width=380,Height=110,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        prepare.Click+=(_,_)=>{
            var savedList=(ListBox)window.FindName("SavedExtractionList");
            savedList.SelectedItem=savedList.Items.Cast<SavedExtraction>().First(x=>x.Status=="Saved");
            typeof(MainWindow).GetField("_selectedFlyer",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(window,new FlyerFile{Retailer="Fixture",FileName="unsaved-fixture.pdf"});
            typeof(MainWindow).GetField("_selectedPage",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(window,new FlyerPage{FileName="unsaved-page.pdf",FullPath="not-opened-by-fake-provider.pdf"});
            var extract=(Button)window.FindName("GetDataButton");extract.Visibility=Visibility.Visible;
            controls.Hide();
        };
        window.Loaded+=(_,_)=> { controls.Owner=window; if(!sqlFailure) controls.Show(); };
        app.Run(window);
        return 0;
    }
}
