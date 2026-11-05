using System.IO;
using System.Transactions;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using FlyerMonkey.Reviewer.Windows;
using FlyerMonkey.Reviewer.Windows.Models;
using FlyerMonkey.Reviewer.Windows.Services;
using FlyerMonkey.Shared.Model;
using SQLServerConnection.Data;

internal static partial class Program
{
    private const string Instance = @"(localdb)\ReviewerSafetyValidation";
    private const string Database = "ReviewerSafety_Disposable_20261105";
    internal static readonly string Sql = $"Data Source={Instance};Initial Catalog={Database};Integrated Security=True;Encrypt=False;Enlist=True;Connect Timeout=5;Application Name=ReviewerSafetyValidation";
    internal static readonly string FixtureRoot = Path.GetFullPath(".artifacts/reviewer-commit-validation");
    private static int checks;
    internal static void Check(bool ok, string name) { if(!ok) throw new Exception("FAIL " + name); Console.WriteLine("PASS " + ++checks + " " + name); }
    private static async Task Execute(string sql, string? connection = null) { await using var c = new SqlConnection(connection ?? Sql); await c.OpenAsync(); using var cmd=c.CreateCommand(); cmd.CommandText=sql; await cmd.ExecuteNonQueryAsync(); }
    internal static async Task<int> Count(string table) { await using var c=new SqlConnection(Sql); await c.OpenAsync(); using var cmd=c.CreateCommand(); cmd.CommandText=$"SELECT COUNT(*) FROM dbo.{table}"; return Convert.ToInt32(await cmd.ExecuteScalarAsync()); }
    private static async Task<string> Status(string path, long id) { using var c=new SqliteConnection($"Data Source={path};Mode=ReadOnly"); await c.OpenAsync(); using var cmd=c.CreateCommand(); cmd.CommandText="SELECT Status FROM ExtractionRuns WHERE ID=$id";cmd.Parameters.AddWithValue("$id",id);return (string)(await cmd.ExecuteScalarAsync())!; }
    private static async Task<(string, long)> Draft(string label) {
        var path=Path.Combine(FixtureRoot,label+"-"+Guid.NewGuid().ToString("N")+".db");
        await new ExtractionSaveService(path).SaveAsync(new FlyerFile {Retailer="Fixture",FileName=label+".pdf"},new FlyerPage{FileName="page-001.pdf",PageNumber=1},new[]{new ExtractedProduct{ProductName="Fixture milk",Price="$2.50"},new ExtractedProduct{ProductName="Fixture bread",Price="$3.00"}});
        return (path,1);
    }
    private static async Task WriteBatch(string source, bool fail=false, bool forcePromotion=false) {
        var products=new ProductRepository(Sql);var offers=new OfferRepository(Sql);
        using var tx=new TransactionScope(TransactionScopeOption.Required,TransactionScopeAsyncFlowOption.Enabled);
        for(int i=0;i<2;i++) {
            var id=await products.AddProductAsync(new Product{Name=source+"-"+i});
            await products.SaveProductCategoriesAsync(id,"Fixture category",new[]{"Fixture category"});
            await offers.AddOfferAsync(new Offer{ProductID=id,RetailerID=1,RetailerLocationID=1,AdvertisedPrice=2.50m,SourceType="Flyer",SourceDescription=source,FlyerPageNumber=1,ValidFrom=DateTime.UtcNow,ValidTo=DateTime.UtcNow.AddDays(7)});
            if (i==0 && fail) throw new InvalidOperationException("Injected halfway failure");
        }
        if(forcePromotion) { using var extra=new SqlConnection(Sql+";Pooling=False");await extra.OpenAsync(); }
        Console.WriteLine("Ambient distributed ID: "+Transaction.Current!.TransactionInformation.DistributedIdentifier);
        tx.Complete();
    }
    [STAThread] private static int Main(string[] args) {
        try { if(args.Contains("--recovery-ui-checks")) { RunRecoveryUiChecks(); return 0; } if(args.Contains("--explicit-crash-child")) { ExplicitCrashChild(args).GetAwaiter().GetResult(); return 23; } if(args.Contains("--crash-child")) { CrashChild(args).GetAwaiter().GetResult(); return 23; } if(args.Length == 0 || args.Contains("--ui") || args.Contains("--ui-009a") || args.Contains("--ui-009a-error")) return RunUi(args.Contains("--ui-009a") || args.Contains("--ui-009a-error"), args.Contains("--ui-009a-error")); Run(args).GetAwaiter().GetResult();return 0; }
        catch(Exception ex) { Console.Error.WriteLine(ex.ToString());return 1; }
    }
    private static async Task CrashChild(string[] args) {
        var service=new SqlCommitService(args[1]);await service.ClaimAsync(long.Parse(args[2]));
        if(args[3]=="after-sql-complete") {await WriteBatch("crash-after-"+Guid.NewGuid());Environment.Exit(23);}
        using var tx=new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
        var id=await new ProductRepository(Sql).AddProductAsync(new Product{Name="crash-before"});
        await new OfferRepository(Sql).AddOfferAsync(new Offer{ProductID=id,RetailerID=1,AdvertisedPrice=2m,SourceDescription="crash-before"});
        Environment.Exit(23);
    }
    private static async Task Run(string[] args) {
        Directory.CreateDirectory(FixtureRoot);
        if(args.Contains("--ui-status")) {
            foreach(var fixture in Directory.GetFiles(FixtureRoot,"ui-*.db")) {
                using var c=new SqliteConnection($"Data Source={fixture};Mode=ReadOnly");await c.OpenAsync();using var cmd=c.CreateCommand();cmd.CommandText="SELECT ID,Status,ProductCount,CommittedUtc FROM ExtractionRuns";
                using var r=await cmd.ExecuteReaderAsync();while(await r.ReadAsync())Console.WriteLine(Path.GetFileName(fixture)+" | "+string.Join(" | ",Enumerable.Range(0,r.FieldCount).Select(r.GetValue)));
            }
            return;
        }
        if(args.Contains("--inspect")) {
            var raw=Environment.GetEnvironmentVariable("FLYERMONKEY_SQL_CONNECTION");
            if(string.IsNullOrWhiteSpace(raw)) {Console.WriteLine("Production SQL connection absent");return;}
            var b=new SqlConnectionStringBuilder(raw);
            Console.WriteLine($"SQL settings: AzureEndpoint={b.DataSource.EndsWith(".database.windows.net",StringComparison.OrdinalIgnoreCase)}; Enlist={b.Enlist}; Pooling={b.Pooling}; MARS={b.MultipleActiveResultSets}; Encrypt={b.Encrypt}; TrustServerCertificate={b.TrustServerCertificate}; Authentication={b.Authentication}; IntegratedSecurity={b.IntegratedSecurity}");
            b.ConnectTimeout=30; b.ApplicationIntent=ApplicationIntent.ReadOnly; b.ApplicationName="ReviewerReadOnlyAudit";
            using var c=new SqlConnection(b.ConnectionString);try {await c.OpenAsync();} catch(SqlException ex) {Console.WriteLine($"READ-ONLY AUDIT BLOCKED: SQL error {ex.Number}: {ex.Message}");return;}
            using var cmd=c.CreateCommand();cmd.CommandTimeout=15;
            cmd.Parameters.AddWithValue("@Today", new DateTime(2026,11,5));
            cmd.CommandText="SELECT COUNT_BIG(*) AS TotalOffers FROM dbo.Offers; SELECT SourceDescription,FlyerPageNumber,COUNT_BIG(*) AS Offers FROM dbo.Offers GROUP BY SourceDescription,FlyerPageNumber; SELECT RetailerID, SourceDescription, FlyerPageNumber, AdvertisedPrice, COUNT_BIG(*) AS Offers FROM dbo.Offers GROUP BY RetailerID,SourceDescription,FlyerPageNumber,AdvertisedPrice HAVING COUNT_BIG(*)>1; SELECT CASE WHEN ValidFrom <= @Today AND ValidTo >= @Today THEN 'Current' WHEN ValidTo < @Today THEN 'Previous' WHEN ValidFrom > @Today THEN 'Upcoming' ELSE 'Unclassified' END AS DateState,COUNT_BIG(*) FROM dbo.Offers GROUP BY CASE WHEN ValidFrom <= @Today AND ValidTo >= @Today THEN 'Current' WHEN ValidTo < @Today THEN 'Previous' WHEN ValidFrom > @Today THEN 'Upcoming' ELSE 'Unclassified' END; SELECT r.Name,CONVERT(date,o.ValidFrom),CONVERT(date,o.ValidTo),COUNT_BIG(*) FROM dbo.Offers o JOIN dbo.Retailers r ON r.ID=o.RetailerID GROUP BY r.Name,CONVERT(date,o.ValidFrom),CONVERT(date,o.ValidTo) ORDER BY 2,1; SELECT COUNT_BIG(*) AS MissingAdvertisedPrice FROM dbo.Offers WHERE AdvertisedPrice IS NULL; SELECT p.Name,p.Brand,p.Variant,p.PackSizeText,o.RetailerID,o.SourceDescription,o.FlyerPageNumber,o.AdvertisedPrice,CONVERT(date,o.ValidFrom),CONVERT(date,o.ValidTo),COUNT_BIG(*) FROM dbo.Offers o JOIN dbo.Products p ON p.ID=o.ProductID GROUP BY p.Name,p.Brand,p.Variant,p.PackSizeText,o.RetailerID,o.SourceDescription,o.FlyerPageNumber,o.AdvertisedPrice,CONVERT(date,o.ValidFrom),CONVERT(date,o.ValidTo) HAVING COUNT_BIG(*)>1; SELECT o.ID,o.ProductID,p.Name,o.AdvertisedPrice,o.ValidFrom,o.ValidTo FROM dbo.Offers o JOIN dbo.Products p ON p.ID=o.ProductID WHERE o.ValidFrom <= @Today AND o.ValidTo >= @Today ORDER BY o.ID; SELECT name,is_nullable FROM sys.columns WHERE object_id=OBJECT_ID('dbo.Offers') AND name='AdvertisedPrice'; SELECT COUNT_BIG(*) FROM dbo.Products p WHERE NOT EXISTS(SELECT 1 FROM dbo.Offers o WHERE o.ProductID=p.ID);";
            using var r=await cmd.ExecuteReaderAsync();do {Console.WriteLine("AUDIT RESULT");while(await r.ReadAsync()) Console.WriteLine(string.Join(" | ",Enumerable.Range(0,r.FieldCount).Select(r.GetValue)));}while(await r.NextResultAsync());
            using(var sqlite=new SqliteConnection("Data Source=DATA/FlyerMonkey.db;Mode=ReadOnly")) {
                await sqlite.OpenAsync();using var query=sqlite.CreateCommand();query.CommandText="SELECT ID,FlyerFileName,PageFileName,ProductCount,Status,ExtractedJson FROM ExtractionRuns WHERE ID IN (21,22,23,24)";
                using var rows=await query.ExecuteReaderAsync();while(await rows.ReadAsync()) {
                    Console.WriteLine($"SQLITE {rows.GetInt64(0)} | {rows.GetString(1)} | {rows.GetString(2)} | {rows.GetInt32(3)} | {rows.GetString(4)}");
                    var items=System.Text.Json.JsonSerializer.Deserialize<List<ExtractedProduct>>(rows.GetString(5))!;
                    foreach(var item in items)Console.WriteLine($"  {item.ProductName} | {item.Price}");
                }
            }
            return;
        }
        if (!args.Contains("--integration")) throw new ArgumentException("Use --integration, --inspect, --ui-status or --ui.");
        var master=$"Data Source={Instance};Initial Catalog=master;Integrated Security=True;Encrypt=False;Connect Timeout=5";
        // This literal LocalDB endpoint and unique database are the only write destination.
        await Execute($"IF DB_ID(N'{Database}') IS NULL CREATE DATABASE [{Database}]",master);
        await Execute("""
IF OBJECT_ID('dbo.Products') IS NULL BEGIN
CREATE TABLE Retailers(ID int PRIMARY KEY,Name nvarchar(200)); INSERT Retailers VALUES(1,'Fixture');
CREATE TABLE RetailerLocations(ID int PRIMARY KEY,RetailerID int,Suburb nvarchar(100),IsActive bit); INSERT RetailerLocations VALUES(1,1,'Butler',1);
CREATE TABLE Products(ID int IDENTITY PRIMARY KEY,Name nvarchar(400) NOT NULL,Brand nvarchar(400),Variant nvarchar(400),PackSizeText nvarchar(400),Barcode nvarchar(400),Category nvarchar(400),ImageBlobPath nvarchar(1000));
CREATE TABLE Categories(ID int PRIMARY KEY,Name nvarchar(200),IsActive bit);INSERT Categories VALUES(1,'Fixture category',1);
CREATE TABLE ProductCategories(ProductID int REFERENCES Products(ID),CategoryID int REFERENCES Categories(ID),IsPrimary bit);
CREATE TABLE Offers(ID int IDENTITY PRIMARY KEY,ProductID int REFERENCES Products(ID),RetailerID int REFERENCES Retailers(ID),RetailerLocationID int REFERENCES RetailerLocations(ID),AdvertisedPrice decimal(18,2),RegularPrice decimal(18,2),AdvertisedSaving decimal(18,2),ValidFrom datetime2,ValidTo datetime2,SourceType nvarchar(100),SourceDescription nvarchar(400),FlyerPageNumber int,FlyerImageBlobPath nvarchar(1000),PromoText nvarchar(max),PromotionTypes nvarchar(max),UnitPriceText nvarchar(400),MultiBuyQuantity int,MultiBuyPrice decimal(18,2),MemberPrice decimal(18,2),MemberOnlyFlag bit,SourceProductText nvarchar(max));
END
""");
        var baseline=new[]{await Count("Products"),await Count("Offers"),await Count("ProductCategories")};
        var (success,id)=await Draft("success");var commit=new SqlCommitService(success);
        Check(await Status(success,id)=="Saved","new draft Saved");
        await commit.ClaimAsync(id);Check(await Status(success,id)=="Committing","atomic claim Committing");
        await WriteBatch("success-"+Guid.NewGuid());await commit.MarkCommittedAsync(id);
        Check(await Status(success,id)=="Committed","SQL success acknowledged Committed");
        Check(await Count("Products")==baseline[0]+2 && await Count("Offers")==baseline[1]+2 && await Count("ProductCategories")==baseline[2]+2,"successful transaction persisted 2 products, 2 offers, 2 categories");
        try{await commit.ClaimAsync(id);throw new Exception("repeat accepted");}catch(InvalidOperationException){Check(true,"repeated completed claim refused");}
        var (failed,fid)=await Draft("failure");var failing=new SqlCommitService(failed);await failing.ClaimAsync(fid);
        try{await WriteBatch("failure",true);throw new Exception("injection missing");}catch(InvalidOperationException ex)when(ex.Message=="Injected halfway failure"){}
        Check(await Count("Products")==baseline[0]+2 && await Count("Offers")==baseline[1]+2 && await Count("ProductCategories")==baseline[2]+2,"halfway failure rolled back product, offer and category inserts");
        Check(await Status(failed,fid)=="Committing","rollback remains Committing without automatic retry");
        try{await new SqlCommitService(failed).ClaimAsync(fid);throw new Exception("restart retry accepted");}catch(InvalidOperationException){Check(true,"fresh service cannot retry uncertain batch");}
        var (concurrent,cid)=await Draft("concurrent");
        var claims=await Task.WhenAll(Enumerable.Range(0,12).Select(_=>Task.Run(async()=>{try{var contender=new SqlCommitService(concurrent);await contender.ClaimAsync(cid);await WriteBatch("concurrent-"+Guid.NewGuid());await contender.MarkCommittedAsync(cid);return true;}catch(InvalidOperationException){return false;}})));
        Check(claims.Count(x=>x)==1,"12 concurrent commit attempts: exactly one winner persisted and acknowledged");
        Check(await Count("Offers")==baseline[1]+4 && await Status(concurrent,cid)=="Committed","concurrent winner added exactly 2 offers; losers added none");
        var (gap,gid)=await Draft("ack-gap");var gapCommit=new SqlCommitService(gap);await gapCommit.ClaimAsync(gid);await WriteBatch("ack-gap-"+Guid.NewGuid());
        Check(await Status(gap,gid)=="Committing","SQL success without local acknowledgement remains Committing");
        try{await new SqlCommitService(gap).ClaimAsync(gid);throw new Exception("ack gap retry accepted");}catch(InvalidOperationException){Check(true,"SQL success/ack gap blocks restart retry");}
        Check(await Count("Offers")==baseline[1]+6,"ack gap preserved successful SQL offers exactly once");
        foreach(var phase in new[]{"before-sql-complete","after-sql-complete"}) {
            var (crashPath,crashId)=await Draft("crash-"+phase);
            var countBefore=await Count("Offers");
            var start=new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true };
            foreach(var argument in new[]{"--crash-child",crashPath,crashId.ToString(),phase}) start.ArgumentList.Add(argument);
            using var child=System.Diagnostics.Process.Start(start)!;
            var output=child.StandardOutput.ReadToEndAsync();var error=child.StandardError.ReadToEndAsync();await child.WaitForExitAsync();
            Check(child.ExitCode==23,"abrupt child exit at "+phase+": "+await error);
            Check(await Status(crashPath,crashId)=="Committing","crash preserves durable claim at "+phase);
            Check(await Count("Offers")==countBefore+(phase=="after-sql-complete"?2:0),"crash SQL persistence correct at "+phase);
            try{await new SqlCommitService(crashPath).ClaimAsync(crashId);throw new Exception("crash retry accepted");}catch(InvalidOperationException){Check(true,"new process/service cannot repeat crashed commit at "+phase);}
        }
        var visible=await new ExtractionReadService(failed).GetAllAsync();
        Console.WriteLine("OBSERVATION: Committing batch visible in saved list = "+visible.Any(x=>x.Id==fid));
        var before=await Count("Offers");
        try{await WriteBatch("promotion",forcePromotion:true);Console.WriteLine("PROMOTION: succeeded");}catch(Exception ex){Console.WriteLine("PROMOTION BLOCKED: "+ex.GetType().Name+": "+ex.Message);Check(await Count("Offers")==before,"promotion failure rolled back all offers");}
        Console.WriteLine($"BASELINE RESULT: {checks} checks passed. Disposable fixture retained at {FixtureRoot}; SQL database {Database}.");
        await RunExplicitChecks();
    }

}
