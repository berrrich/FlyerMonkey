using System.IO;
using System.Transactions;
using Microsoft.Data.SqlClient;
using FlyerMonkey.Shared.Model;
using SQLServerConnection.Data;
using FlyerMonkey.Reviewer.Windows.Services;

internal static partial class Program
{
    private static async Task WriteExplicit(string source, bool fail = false, string? connectionString = null)
    {
        await using var batch = await ReviewerSqlBatch.OpenAsync(connectionString ?? Sql);
        for (var i = 0; i < 2; i++)
        {
            var id = await batch.AddProductAsync(new Product { Name = source + "-" + i });
            await batch.SaveProductCategoriesAsync(id, "Fixture category", new[] { "Fixture category" });
            await batch.AddOfferAsync(new Offer { ProductID = id, RetailerID = 1, RetailerLocationID = 1, AdvertisedPrice = 2.50m, SourceDescription = source });
            if (fail && i == 0) throw new InvalidOperationException("Injected explicit halfway failure");
        }
        await batch.CommitAsync();
    }

    private static async Task ExplicitCrashChild(string[] args)
    {
        var service = new SqlCommitService(args[1]);
        await service.ClaimAsync(long.Parse(args[2]));
        if (args[3] == "after")
        {
            await WriteExplicit("explicit-crash-after-" + Guid.NewGuid());
            Environment.Exit(23);
        }
        await using var batch = await ReviewerSqlBatch.OpenAsync(Sql);
        var id = await batch.AddProductAsync(new Product { Name = "explicit-crash-before" });
        await batch.SaveProductCategoriesAsync(id, "Fixture category", new[] { "Fixture category" });
        await batch.AddOfferAsync(new Offer { ProductID = id, RetailerID = 1, AdvertisedPrice = 2m });
        Environment.Exit(23);
    }

    private static async Task RunExplicitChecks()
    {
        var startChecks = checks;
        var baseline = new[] { await Count("Products"), await Count("Offers"), await Count("ProductCategories") };
        var (path, id) = await Draft("009a-explicit-success");
        var service = new SqlCommitService(path);
        await service.ClaimAsync(id);
        await WriteExplicit("009a-success-" + Guid.NewGuid());
        await service.MarkCommittedAsync(id);
        Check(await Status(path, id) == "Committed", "explicit success acknowledged Committed");
        Check(await Count("Products") == baseline[0] + 2 && await Count("Offers") == baseline[1] + 2 && await Count("ProductCategories") == baseline[2] + 2, "explicit success persisted 2 products, offers and category links");
        try { await service.ClaimAsync(id); throw new Exception("Repeat explicit claim accepted"); }
        catch (InvalidOperationException) { Check(true, "explicit completed batch cannot retry"); }

        var (failedPath, failedId) = await Draft("009a-explicit-failure");
        await new SqlCommitService(failedPath).ClaimAsync(failedId);
        try { await WriteExplicit("009a-halfway", fail: true); throw new Exception("Injection absent"); }
        catch (InvalidOperationException ex) when (ex.Message == "Injected explicit halfway failure") { }
        Check(await Count("Products") == baseline[0] + 2 && await Count("Offers") == baseline[1] + 2 && await Count("ProductCategories") == baseline[2] + 2, "explicit halfway failure rolls back all three tables");
        var recovery = (await new ExtractionReadService(failedPath).GetAllAsync()).Single();
        Check(recovery.RecoveryRequired && recovery.StatusDisplay == "Recovery Required", "failed explicit batch visible as Recovery Required");
        try { await new SqlCommitService(failedPath).ClaimAsync(failedId); throw new Exception("Uncertain explicit retry accepted"); }
        catch (InvalidOperationException) { Check(true, "explicit uncertain batch cannot retry"); }

        await using (var batch = await ReviewerSqlBatch.OpenAsync(Sql))
        {
            var productId = await batch.AddProductAsync(new Product { Name = "009a-sql-error" });
            await batch.SaveProductCategoriesAsync(productId, "Fixture category", new[] { "Fixture category" });
            try { await batch.AddOfferAsync(new Offer { ProductID = productId, RetailerID = int.MaxValue }); throw new Exception("SQL FK error absent"); }
            catch (SqlException ex) when (ex.Number == 547) { Check(true, "real SQL constraint failure is surfaced"); }
            try { await batch.CommitAsync(); throw new Exception("Faulted batch committed"); }
            catch (InvalidOperationException) { Check(true, "faulted batch cannot commit partial work"); }
        }
        Check(await Count("Products") == baseline[0] + 2 && await Count("Offers") == baseline[1] + 2 && await Count("ProductCategories") == baseline[2] + 2, "SQL constraint failure rolls back all three tables");

        await using (var batch = await ReviewerSqlBatch.OpenAsync(Sql))
        {
            await batch.CommitAsync();
            try { await batch.CommitAsync(); throw new Exception("Second SQL Commit accepted"); }
            catch (InvalidOperationException) { Check(true, "second Commit on explicit transaction rejected"); }
            try { await batch.AddProductAsync(new Product { Name = "must-not-write" }); throw new Exception("Write after Commit accepted"); }
            catch (InvalidOperationException) { Check(true, "write after explicit Commit rejected"); }
        }
        using (var ambient = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
        {
            try { await using var batch = await ReviewerSqlBatch.OpenAsync(Sql); throw new Exception("Ambient nesting accepted"); }
            catch (InvalidOperationException) { Check(true, "unexpected ambient transaction rejected before connection open"); }
        }
        var unpooled = new SqlConnectionStringBuilder(Sql) { Pooling = false, Enlist = false }.ConnectionString;
        await WriteExplicit("009a-unpooled-" + Guid.NewGuid(), connectionString: unpooled);
        Check(await Count("Offers") == baseline[1] + 4, "explicit batch succeeds without connection pooling or ambient enlistment");

        var (concurrentPath, concurrentId) = await Draft("009a-concurrent");
        var beforeConcurrent = await Count("Offers");
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(async () =>
        {
            var contender = new SqlCommitService(concurrentPath);
            try { await contender.ClaimAsync(concurrentId); }
            catch (InvalidOperationException) { return false; }
            await WriteExplicit("009a-concurrent-" + Guid.NewGuid());
            await contender.MarkCommittedAsync(concurrentId);
            return true;
        })));
        Check(outcomes.Count(x => x) == 1 && await Count("Offers") == beforeConcurrent + 2 && await Status(concurrentPath, concurrentId) == "Committed", "12 concurrent explicit commits produce one two-offer batch");

        foreach (var phase in new[] { "before", "after" })
        {
            var (crashPath, crashId) = await Draft("009a-explicit-crash-" + phase);
            var countsBefore = new[] { await Count("Products"), await Count("Offers"), await Count("ProductCategories") };
            var start = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { "--explicit-crash-child", crashPath, crashId.ToString(), phase }) start.ArgumentList.Add(argument);
            using var child = System.Diagnostics.Process.Start(start)!;
            var output = child.StandardOutput.ReadToEndAsync();
            var error = child.StandardError.ReadToEndAsync();
            await child.WaitForExitAsync();
            Check(child.ExitCode == 23, "explicit child crashed " + phase + " SQL completion: " + await error);
            var delta = phase == "after" ? 2 : 0;
            Check(await Count("Products") == countsBefore[0] + delta && await Count("Offers") == countsBefore[1] + delta && await Count("ProductCategories") == countsBefore[2] + delta, "explicit crash preserves correct three-table outcome " + phase + " SQL completion");
            var crashed = (await new ExtractionReadService(crashPath).GetAllAsync()).Single();
            Check(crashed.RecoveryRequired && await Status(crashPath, crashId) == "Committing", "explicit crash visible as recovery " + phase + " SQL completion");
            try { await new SqlCommitService(crashPath).ClaimAsync(crashId); throw new Exception("Crashed explicit retry accepted"); }
            catch (InvalidOperationException) { Check(true, "explicit crash cannot retry " + phase + " SQL completion"); }
        }
        Console.WriteLine($"MISSION 009A: {checks - startChecks} new explicit SQL/recovery checks passed.");
    }
}
