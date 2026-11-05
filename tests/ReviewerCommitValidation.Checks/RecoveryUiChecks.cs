using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using FlyerMonkey.Reviewer.Windows;
using FlyerMonkey.Reviewer.Windows.Models;
using FlyerMonkey.Reviewer.Windows.Services;

internal static partial class Program
{
    private static void RunRecoveryUiChecks()
    {
        var start = checks;
        Directory.CreateDirectory(FixtureRoot);
        var path = Path.Combine(FixtureRoot, "009a-wpf-" + Guid.NewGuid().ToString("N") + ".db");
        var saver = new ExtractionSaveService(path);
        var flyer = new FlyerFile { Retailer = "Fixture", FileName = "009a-wpf.pdf" };
        for (var i = 1; i <= 3; i++)
            saver.SaveAsync(flyer, new FlyerPage { FileName = "page-" + i + ".pdf", PageNumber = i }, new[] { new ExtractedProduct { ProductName = "Fixture product " + i, Price = i == 1 ? null : "$2.50" } }).GetAwaiter().GetResult();
        var commit = new SqlCommitService(path);
        commit.ClaimAsync(2).GetAwaiter().GetResult();
        commit.ClaimAsync(3).GetAwaiter().GetResult();
        commit.MarkCommittedAsync(3).GetAwaiter().GetResult(); // Synthetic status fixture; no SQL call.
        var app = Application.Current ?? new Application();
        var window = new MainWindow(path, (_, _) => throw new Exception("Provider must not run in layout/recovery checks"), loadFlyers: false);
        void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        var list = (ListBox)window.FindName("SavedExtractionList");
        Pump();
        var entries = list.Items.Cast<SavedExtraction>().ToList();
        Check(entries.Count == 3, "Saved Extractions includes Saved, Committing and Committed fixtures");
        var recovery = entries.Single(x => x.Id == 2);
        Check(recovery.RecoveryRequired && recovery.StatusDisplay == "Recovery Required", "reopened inactive Committing entry has recovery label");
        Check(recovery.RecoveryExplanation!.Contains("cannot", StringComparison.OrdinalIgnoreCase) || recovery.RecoveryExplanation.Contains("blocked", StringComparison.OrdinalIgnoreCase), "recovery explanation warns against writing");

        var status = new TextBlock { DataContext = recovery };
        status.SetBinding(TextBlock.TextProperty, new Binding(nameof(SavedExtraction.StatusDisplay)));
        Pump();
        Check(status.Text == "Recovery Required", "WPF status binding renders recovery label");
        recovery.IsCommitActive = true;
        Pump();
        Check(status.Text == "Committing" && !recovery.RecoveryRequired, "active local batch changes bound label to Committing");
        recovery.IsCommitActive = false;
        Pump();
        Check(status.Text == "Recovery Required", "operation ending restores recovery label without status reset");

        list.SelectedItem = recovery;
        Pump();
        Check(!((Button)window.FindName("CommitButton")).IsEnabled, "recovery selection blocks Commit");
        Check(!((Button)window.FindName("SaveDataButton")).IsEnabled, "recovery selection blocks Save");
        var products = (ListBox)window.FindName("ProductList");
        Check(products.Items.Count == 1 && ((ExtractedProduct)products.Items[0]).ProductName == "Fixture product 2", "recovery selection still loads exact products for inspection");
        Check(Status(path, 2).GetAwaiter().GetResult() == "Committing", "viewing recovery preserves durable claim");
        try { saver.UpdateSavedExtractionAsync(2, new[] { new ExtractedProduct { ProductName = "Must not persist" } }).GetAwaiter().GetResult(); throw new Exception("Recovery draft update accepted"); }
        catch (InvalidOperationException) { Check(true, "service rejects updating recovery draft"); }
        try { saver.SaveAsync(flyer, new FlyerPage { FileName = "page-2.pdf", PageNumber = 2 }, new[] { new ExtractedProduct { ProductName = "Must not create a new draft" } }).GetAwaiter().GetResult(); throw new Exception("Recovery resave accepted"); }
        catch (InvalidOperationException) { Check(true, "service rejects creating replacement draft for recovery page"); }
        var restored = new ExtractionReadService(path).GetAllAsync().GetAwaiter().GetResult().Single(x => x.Id == 2);
        Check(restored.RecoveryRequired && !restored.IsCommitActive, "fresh read remains recovery after blocked writes");

        list.SelectedItem = entries.Single(x => x.Id == 1);
        Pump();
        Check(((Button)window.FindName("SaveDataButton")).IsEnabled && ((Button)window.FindName("CommitButton")).IsEnabled, "normal Saved selection retains Save and Commit eligibility");
        Check(((ExtractedProduct)products.Items[0]).HasErrors, "incomplete saved draft retains price validation");
        list.SelectedItem = entries.Single(x => x.Id == 3);
        Pump();
        Check(!((Button)window.FindName("CommitButton")).IsEnabled, "Committed selection remains protected from repeat Commit");

        var root = (FrameworkElement)window.Content;
        var getData = (Button)window.FindName("GetDataButton");
        getData.Visibility = Visibility.Visible;
        root.Measure(new Size(1384, 760));
        root.Arrange(new Rect(0, 0, 1384, 760));
        root.UpdateLayout();
        var actions = (StackPanel)window.FindName("PageActions");
        var actionBounds = new[] { "CancelProcessingButton", "SplitFlyerButton", "GetDataButton" }
            .Select(name => (Button)window.FindName(name))
            .Select(button => new Rect(button.TranslatePoint(new Point(), actions), button.RenderSize)).ToArray();
        Check(!actionBounds[0].IntersectsWith(actionBounds[1]) && !actionBounds[0].IntersectsWith(actionBounds[2]) && !actionBounds[1].IntersectsWith(actionBounds[2]), "page action click targets do not overlap");
        var actionBorder = (FrameworkElement)VisualTreeHelper.GetParent(VisualTreeHelper.GetParent(actions));
        foreach (var name in new[] { "CancelProcessingButton", "SplitFlyerButton", "GetDataButton" })
        {
            var button = (Button)window.FindName(name);
            var point = button.TranslatePoint(new Point(), actionBorder);
            Check(button.ActualWidth >= 140 && point.X >= 0 && point.Y >= 0 && point.X + button.ActualWidth <= actionBorder.ActualWidth + 0.1 && point.Y + button.ActualHeight <= actionBorder.ActualHeight + 0.1, name + " fully fits page-review border at default window size");
        }
        Check(((Button)window.FindName("CancelProcessingButton")).ActualHeight + ((Button)window.FindName("SplitFlyerButton")).ActualHeight + getData.ActualHeight <= actions.ActualHeight, "all action rows fit without overlap");
        window.Close();
        Console.WriteLine($"MISSION 009A WPF: {checks - start} recovery/layout checks passed; fixture retained at {path}.");
    }
}
