using System.IO;
using FlyerMonkey.Reviewer.Windows;
using FlyerMonkey.Reviewer.Windows.Models;
using FlyerMonkey.Reviewer.Windows.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Data.SqlClient;

void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS " + name); }
if (args.Contains("--audit")) {
    using var c = new SqliteConnection("Data Source=DATA/FlyerMonkey.db;Mode=ReadOnly");
    await c.OpenAsync();
    using var cmd = c.CreateCommand();
    cmd.CommandText = "SELECT ID, Retailer, PageFileName, ProductCount, Status, json_array_length(ExtractedJson) FROM ExtractionRuns ORDER BY ID";
    using (var reader = await cmd.ExecuteReaderAsync()) while(await reader.ReadAsync()) Console.WriteLine(string.Join(" | ", Enumerable.Range(0,reader.FieldCount).Select(reader.GetValue)));
    cmd.CommandText = "SELECT Retailer, FlyerFileName, PageFileName, COUNT(*) FROM ExtractionRuns GROUP BY 1,2,3 HAVING COUNT(*) > 1";
    using (var reader = await cmd.ExecuteReaderAsync()) { Console.WriteLine("Duplicate page keys:"); while(await reader.ReadAsync()) Console.WriteLine(string.Join(" | ", Enumerable.Range(0,reader.FieldCount).Select(reader.GetValue))); }
    var sql = Environment.GetEnvironmentVariable("FLYERMONKEY_SQL_CONNECTION");
    if (string.IsNullOrWhiteSpace(sql)) { Console.WriteLine("SQL audit unavailable: connection environment variable not configured."); return; }
    using var sc = new SqlConnection(sql); await sc.OpenAsync();
    using var sq = sc.CreateCommand();
    sq.CommandText = "SELECT COUNT_BIG(*) FROM dbo.Offers; SELECT SourceDescription, FlyerPageNumber, COUNT_BIG(*) FROM dbo.Offers GROUP BY SourceDescription, FlyerPageNumber ORDER BY COUNT_BIG(*) DESC; SELECT ProductID, RetailerID, SourceDescription, FlyerPageNumber, AdvertisedPrice, COUNT_BIG(*) FROM dbo.Offers GROUP BY ProductID, RetailerID, SourceDescription, FlyerPageNumber, AdvertisedPrice HAVING COUNT_BIG(*) > 1;";
    using var sr = await sq.ExecuteReaderAsync(); do { while(await sr.ReadAsync()) Console.WriteLine("SQL | " + string.Join(" | ",Enumerable.Range(0,sr.FieldCount).Select(sr.GetValue))); } while(await sr.NextResultAsync());
    return;
}
var path = Path.Combine(Path.GetTempPath(), "reviewer-safety-" + Guid.NewGuid() + ".db");
try {
    var saver = new ExtractionSaveService(path);
    var flyer = new FlyerFile { Retailer="Fixture", FileName="fixture.pdf" };
    var page = new FlyerPage { FileName="page.pdf", PageNumber=1 };
    var p = new ExtractedProduct { ProductName="Milk" };
    Check(ProductValidationService.ValidateBatch(new[]{p}).Count==1,"missing advertised price");
    await saver.SaveAsync(flyer,page,new[]{p});
    await Task.WhenAll(Enumerable.Range(0,8).Select(_=>saver.SaveAsync(flyer,page,new[]{p})));
    using var c = new SqliteConnection($"Data Source={path}"); await c.OpenAsync();
    using var cmd = c.CreateCommand(); cmd.CommandText="SELECT COUNT(*) FROM ExtractionRuns";
    Check(Convert.ToInt32(await cmd.ExecuteScalarAsync())==1,"repeated draft saves stay one batch");
    p.Price="-2"; Check(p.HasErrors,"negative price rejected");
    p.Price="invalid"; Check(p.HasErrors,"invalid price rejected");
    p.Price="$2.50"; Check(!p.HasErrors,"missing-price recovery");
    p.ProductName=""; Check(p.HasErrors,"missing name rejected"); p.ProductName="Milk";
    await saver.UpdateSavedExtractionAsync(1,new[]{p});
    var commit = new SqlCommitService(path); await commit.ClaimAsync(1);
    try { await commit.ClaimAsync(1); throw new Exception("Repeated claim accepted"); } catch(InvalidOperationException) { Console.WriteLine("PASS repeated commit claim rejected"); }
    try { await saver.SaveAsync(flyer,page,new[]{p}); throw new Exception("Claimed save accepted"); } catch(InvalidOperationException) { Console.WriteLine("PASS claimed page cannot create a new draft"); }
    await commit.MarkCommittedAsync(1);
    try { await commit.ClaimAsync(1); throw new Exception("Committed claim accepted"); } catch(InvalidOperationException) { Console.WriteLine("PASS completed commit cannot retry"); }
    var gate = new ProcessingGate();
    using (var lease = gate.TryEnter()) {
        Check(lease != null, "first operation accepted");
        Check(Enumerable.Range(0, 10).AsParallel().All(_ => gate.TryEnter() == null), "repeated clicks and concurrent operations rejected");
    }
    using(var lease = gate.TryEnter()) Check(lease != null, "gate released after completion");
    using var cancellation = new CancellationTokenSource();
    var pending = new TaskCompletionSource<string>();
    var operation = ProcessingGate.CompleteAsync(pending.Task, cancellation.Token);
    cancellation.Cancel(); pending.SetResult("replacement");
    var displayed = "previous completed work";
    try { displayed = await operation; throw new Exception("Cancelled result published"); }
    catch(OperationCanceledException) { Check(displayed == "previous completed work", "cancelled extraction does not replace completed work"); }
    Check(await ProcessingGate.CompleteAsync(Task.FromResult("complete"), CancellationToken.None) == "complete", "successful completion preserved");
} finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
