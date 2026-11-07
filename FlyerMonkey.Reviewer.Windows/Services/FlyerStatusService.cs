using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Syncfusion.Pdf.Parsing;

namespace FlyerMonkey.Reviewer.Windows.Services;

public sealed record FlyerSplitState(string[] Pages, string Warning);

public sealed class FlyerStatusService(string databasePath)
{
    // Retailer and complete source filename survive application restarts and workspace moves.
    public static string Identity(FlyerFile flyer) => JsonSerializer.Serialize(new[] { flyer.Retailer.Trim().ToUpperInvariant(), flyer.FileName.ToUpperInvariant() });

    public static FlyerSplitState Discover(string splitRoot, FlyerFile flyer, IEnumerable<FlyerFile> sources)
    {
        var stem = Path.GetFileNameWithoutExtension(flyer.FileName);
        var owners = sources.Where(x => string.Equals(Path.GetFileNameWithoutExtension(x.FileName), stem, StringComparison.OrdinalIgnoreCase)).ToList();
        if (owners.Select(x => Path.GetFullPath(x.FullPath)).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1
            || owners.Any(x => Identity(x) != Identity(flyer)))
            return new([], "Ambiguous split folder: multiple source flyers share this filename. Pages were not associated. Resolve source identity before splitting or reviewing.");
        var folder = Path.Combine(splitRoot, stem);
        var staged = Directory.Exists(splitRoot) && Directory.GetDirectories(splitRoot).Any(x => Path.GetFileName(x).StartsWith(stem + ".pending-", StringComparison.OrdinalIgnoreCase));
        if (!Directory.Exists(folder)) return new([], staged ? "Incomplete split: unpublished staging files exist. Existing files were preserved; no automatic repair was attempted." : "");
        var valid = new List<string>();
        var invalid = false;
        foreach (var file in Directory.GetFiles(folder, "*.pdf").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            if (!Regex.IsMatch(Path.GetFileName(file), @"^page-[0-9]{3,}\.pdf$", RegexOptions.IgnoreCase)
                || !int.TryParse(Path.GetFileNameWithoutExtension(file)[5..], out var number) || number < 1) { invalid = true; continue; }
            try { using var pdf = new PdfLoadedDocument(file); if (pdf.Pages.Count == 1) valid.Add(file); else invalid = true; }
            catch { invalid = true; }
        }
        var numbers = valid.Select(x => int.Parse(Path.GetFileNameWithoutExtension(x)[5..])).OrderBy(x => x).ToArray();
        if (!numbers.SequenceEqual(Enumerable.Range(1, numbers.Length))) invalid = true;
        if (File.Exists(flyer.FullPath))
        {
            try { using var source = new PdfLoadedDocument(flyer.FullPath); if (source.Pages.Count != valid.Count) invalid = true; }
            catch { invalid = true; }
        }
        invalid |= staged;
        return new(valid.ToArray(), valid.Count == 0 || invalid
            ? "Incomplete split folder: missing, invalid or unexpected pages. Existing files were preserved; no automatic repair was attempted." : "");
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS ReviewerFlyerStatus (FlyerIdentity TEXT PRIMARY KEY, IsComplete INTEGER NOT NULL, UpdatedUtc TEXT NOT NULL);";
        command.ExecuteNonQuery();
        return connection;
    }
    public bool IsComplete(FlyerFile flyer)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT IsComplete FROM ReviewerFlyerStatus WHERE FlyerIdentity=$id";
        command.Parameters.AddWithValue("$id", Identity(flyer));
        return Convert.ToInt32(command.ExecuteScalar() ?? 0) == 1;
    }
    public void SetComplete(FlyerFile flyer, bool complete)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO ReviewerFlyerStatus VALUES ($id,$complete,$utc) ON CONFLICT(FlyerIdentity) DO UPDATE SET IsComplete=excluded.IsComplete, UpdatedUtc=excluded.UpdatedUtc";
        command.Parameters.AddWithValue("$id", Identity(flyer)); command.Parameters.AddWithValue("$complete", complete ? 1 : 0);
        command.Parameters.AddWithValue("$utc", DateTime.UtcNow.ToString("O")); command.ExecuteNonQuery();
    }
}
