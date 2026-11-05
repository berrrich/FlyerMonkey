using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Threading;
using System.Xml.Linq;
using FlyerMonkey.Reviewer.Windows;
using FlyerMonkey.Reviewer.Windows.Models;
using FlyerMonkey.Reviewer.Windows.Services;

internal static class Program
{
    private static int _checks;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        _checks++;
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    [STAThread]
    private static int Main()
    {
        try { Run(); return 0; }
        catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex.Message); return 1; }
    }

    private static void Run()
    {
        using var bindingMessages = new StringWriter();
        var listener = new TextWriterTraceListener(bindingMessages);
        PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
        var source = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "OfferCards.xaml"));
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        // Use the actual production field grids/bindings. Replace only window event
        // hookups with the existing visibility transition, without starting Reviewer
        // services, reading operator data, opening dialogs or invoking any provider.
        Grid Card(ExtractedProduct product)
        {
            var card = new Grid { DataContext = product };
            var pairs = source.Descendants(wpf + "TextBox")
                .Where(e => ((string?)e.Attribute(x + "Name"))?.EndsWith("Editor") == true);
            foreach (var originalEditor in pairs)
            {
                var pair = new XElement(originalEditor.Parent!);
                // The product-name grid also contains a delete button; do not hook it.
                foreach (var element in pair.DescendantsAndSelf())
                    foreach (var attribute in element.Attributes().Where(a =>
                        a.Name.LocalName is "Click" or "MouseLeftButtonUp" or "LostFocus" or "KeyDown").ToList())
                        attribute.Remove();
                pair.SetAttributeValue(XNamespace.Xmlns + "x", x.NamespaceName);
                var grid = (Grid)XamlReader.Parse(pair.ToString());
                var editor = grid.Children.OfType<TextBox>().Single();
                var display = grid.Children.OfType<TextBlock>().Single();
                editor.LostFocus += (_, _) =>
                {
                    editor.Visibility = Visibility.Collapsed;
                    display.Visibility = Visibility.Visible;
                };
                card.Children.Add(grid);
            }
            Pump();
            return card;
        }

        (TextBox Editor, TextBlock Display) Field(Grid card, string property)
        {
            var grid = card.Children.OfType<Grid>().Single(g =>
                BindingOperations.GetBinding(g.Children.OfType<TextBox>().Single(), TextBox.TextProperty)?.Path.Path == property);
            var editor = grid.Children.OfType<TextBox>().Single();
            var display = grid.Children.OfType<TextBlock>().Single();
            Check(ReferenceEquals(editor.DataContext, card.DataContext) && ReferenceEquals(display.DataContext, card.DataContext), property + " shares model");
            Check(BindingOperations.GetBinding(display, TextBlock.TextProperty)?.Path.Path == property, property + " shares binding path");
            return (editor, display);
        }

        var ham = new ExtractedProduct { ProductName = "Half Leg Ham" };
        var other = new ExtractedProduct { ProductName = "Milk", Variant = "Full cream" };
        var card = Card(ham);
        var otherCard = Card(other);
        var variant = Field(card, "Variant");
        Check(ham.Variant is null && variant.Display.Text == "", "Half Leg Ham starts blank");
        var values = new Dictionary<string, string>
        {
            ["Variant"] = "Half leg ham", ["PackSizeText"] = "1 kg", ["ProductName"] = "Half Leg Ham edited",
            ["Brand"] = "Test brand", ["Price"] = "$10.00", ["RegularPrice"] = "$15.00",
            ["Saving"] = "$5.00", ["UnitPrice"] = "$10 per kg", ["OfferQuantity"] = "2",
            ["Promotion"] = "Special", ["Category"] = "Deli"
        };
        foreach (var (property, value) in values)
        {
            var field = Field(card, property);
            field.Display.Visibility = Visibility.Collapsed;
            field.Editor.Visibility = Visibility.Visible;
            field.Editor.Text = value;
            Pump();
            Check(typeof(ExtractedProduct).GetProperty(property)!.GetValue(ham)?.ToString() == value, property + " updates actual model while editing");
            field.Editor.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent, field.Editor));
            Pump();
            Check(field.Display.Visibility == Visibility.Visible && field.Editor.Visibility == Visibility.Collapsed, property + " exits edit mode");
            Check(field.Display.Text == value, property + " display updates after LostFocus");
            Console.WriteLine($"{property}: model and display retained after LostFocus");
        }
        Check(Field(otherCard, "Variant").Display.Text == "Full cream", "Other card unchanged");
        // Rebinding a card and rebuilding a card must use the edited model.
        card.DataContext = other;
        Pump();
        Check(variant.Display.Text == "Full cream", "Select other card");
        card.DataContext = ham;
        Pump();
        Check(variant.Display.Text == "Half leg ham", "Return retains unsaved edit");
        Check(Field(Card(ham), "Variant").Display.Text == "Half leg ham", "Card recreation retains unsaved edit");
        var cards = new ListBox { ItemsSource = new[] { ham, other } };
        cards.SelectedItem = ham;
        cards.SelectedItem = other;
        cards.Items.Refresh();
        cards.SelectedItem = ham;
        Check(ReferenceEquals(cards.SelectedItem, ham) && ham.Variant == "Half leg ham", "List selection/refresh preserves edited model");

        // Category dialog writes the same properties directly, then refreshes items.
        ham.PrimaryCategory = "Meat & Poultry";
        ham.Categories = new() { "Meat & Poultry", "Deli" };
        ham.Category = ham.PrimaryCategory;
        Pump();
        Check(Field(card, "Category").Display.Text == "Meat & Poultry", "Category dialog model assignment refreshes display");
        ProductValidationService.SanitizeProduct(ham);
        var calculated = new TextBlock { DataContext = ham };
        calculated.SetBinding(TextBlock.TextProperty, new Binding("CalculatedRegularPrice"));
        Pump();
        Check(calculated.Text == "$15.00", "Existing regular-price calculation");
        ham.Saving = "$6.00";
        ProductValidationService.SanitizeProduct(ham);
        Pump();
        Check(calculated.Text == "$16.00", "Calculated display observes existing sanitizer updates");
        ham.RegularPrice = "$15 per kg";
        ProductValidationService.SanitizeProduct(ham);
        Pump();
        Check(Field(card, "RegularPrice").Display.Text == "", "Existing unit-price sanitation updates display");
        ham.RegularPrice = "$16.00";

        var directory = Path.Combine(Path.GetTempPath(), "ReviewerEditing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "fixture.db");
            var saver = new ExtractionSaveService(path);
            var reader = new ExtractionReadService(path);
            saver.SaveAsync(new FlyerFile { Retailer = "Offline", FileName = "fixture.pdf" },
                new FlyerPage { FileName = "page.png", PageNumber = 1 }, new[] { ham, other }).GetAwaiter().GetResult();
            var saved = reader.GetAllAsync().GetAwaiter().GetResult().Single();
            var restored = new SqlCommitService(path).LoadProductsAsync(saved).GetAwaiter().GetResult();
            foreach (var property in values.Keys)
                Check(Equals(typeof(ExtractedProduct).GetProperty(property)!.GetValue(ham),
                    typeof(ExtractedProduct).GetProperty(property)!.GetValue(restored[0])), property + " SQLite round trip");
            restored[0].Variant = "Updated saved ham";
            restored[0].PackSizeText = "2 kg";
            saver.UpdateSavedExtractionAsync(saved.Id, restored).GetAwaiter().GetResult();
            var reloaded = new SqlCommitService(path).LoadProductsAsync(saved).GetAwaiter().GetResult();
            var reloadedCard = Card(reloaded[0]);
            Check(Field(reloadedCard, "Variant").Display.Text == "Updated saved ham", "Saved batch update/reload Variant display");
            Check(Field(reloadedCard, "PackSizeText").Display.Text == "2 kg", "Saved batch update/reload pack display");
            Check(reloaded[0].Category == "Meat & Poultry" && reloaded[0].Categories.SequenceEqual(ham.Categories), "Category persistence retained");
            Check(reloaded[0].CalculatedRegularPrice == "$16.00", "Calculated price persistence retained");
            Check(reader.GetAllAsync().GetAwaiter().GetResult().Count == 1, "Save updates existing fixture batch");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory, true);
        }
        Pump();
        listener.Flush();
        Check(string.IsNullOrWhiteSpace(bindingMessages.ToString()), "No WPF binding warnings/errors: " + bindingMessages);
        PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
        Console.WriteLine($"PASS: {_checks} offline Reviewer editing checks; temporary SQLite only, no provider calls.");
    }
}
