using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using FlyerMonkey.Reviewer.Windows;
using FlyerMonkey.Reviewer.Windows.Models;
using FlyerMonkey.Reviewer.Windows.Services;
using FlyerMonkey.Shared.Model;
using SQLServerConnection.Data;

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
            CheckCatalogueDecisions(saver, saved, path).GetAwaiter().GetResult();
            CheckOfferReview(saver, saved, path).GetAwaiter().GetResult();
            CheckCatalogueCard(path);
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

    private static async Task CheckCatalogueDecisions(ExtractionSaveService saver, SavedExtraction saved, string path)
    {
        var draft = new ExtractedProduct
        {
            ProductName = "Tasmanian Heritage Brie or Camembert", Brand = "Tasmanian Heritage",
            PackSizeText = "200g", Price = "$5.00"
        };
        var existing = new Product
        {
            ID = 91, Name = "Brie", Brand = "Tasmanian Heritage",
            Variant = "Double Brie or Camembert", PackSizeText = "200g"
        };
        var candidate = ProductIdentity.FindCandidates(draft.ToCatalogueProduct(), [existing]).Single();
        Check(candidate.Relationship == ProductRelationship.PossibleDuplicate, "Reviewer sees the missed cheese candidate");
        draft.CatalogueCandidates = [candidate];
        draft.CatalogueReview = CatalogueReviewDecision.For(draft, ProductReviewChoice.ReuseExisting, candidate);
        await saver.UpdateSavedExtractionAsync(saved.Id, [draft]).ConfigureAwait(false);
        var restored = (await new SqlCommitService(path).LoadProductsAsync(saved).ConfigureAwait(false)).Single();
        Check(restored.CatalogueReview == draft.CatalogueReview && restored.CatalogueCandidates.Count == 0,
            "SQLite retains the confirmed decision and refreshes transient matches independently");

        var offers = new List<Offer>();
        var nextId = 300;
        async Task<int> RecordOffer(ExtractedProduct card, IReadOnlyList<Product> catalogue)
        {
            var offer = new Offer { AdvertisedPrice = 5m, SourceDescription = "page.png" };
            var result = await ExtractedProductIdentity.SaveOfferAsync(card, card.ToCatalogueProduct(), offer, catalogue,
                _ => Task.FromResult(++nextId), savedOffer =>
                {
                    offers.Add(savedOffer);
                    return Task.FromResult(offers.Count);
                });
            Check(result.ProductId == offer.ProductID, "Reviewer attaches the offer to the selected or new ProductID");
            return result.ProductId;
        }

        Check(await RecordOffer(restored, [existing]) == existing.ID, "Commit honors explicit confirmation of a possible duplicate");
        Check(await RecordOffer(restored, []) == 301, "A deleted confirmed target preserves the product and offer");
        existing.PackSizeText = "400g";
        Check(await RecordOffer(restored, [existing]) == 302, "A changed confirmed target preserves a separate product and offer");
        existing.PackSizeText = "200g";
        restored.PackSizeText = "400g";
        Check(await RecordOffer(restored, [existing]) == 303, "An identity edit invalidates the earlier confirmation");

        restored.ProductName = existing.Name;
        restored.Variant = existing.Variant;
        restored.PackSizeText = existing.PackSizeText;
        restored.CatalogueReview = CatalogueReviewDecision.For(restored, ProductReviewChoice.KeepSeparate);
        Check(await RecordOffer(restored, [existing]) == 304, "Keep separate overrides a likely match");
        restored.CatalogueReview = CatalogueReviewDecision.For(restored, ProductReviewChoice.ReviewLater);
        Check(await RecordOffer(restored, [existing]) == 305, "Review later preserves a separate product and offer");
        restored.CatalogueReview = null;
        Check(await RecordOffer(restored, [existing]) == existing.ID, "An unambiguous likely match automatically reuses the ProductID");
        Check(offers.Select(offer => offer.ProductID).SequenceEqual([91, 301, 302, 303, 304, 305, 91])
            && offers.All(offer => offer.AdvertisedPrice == 5m && offer.SourceDescription == "page.png"),
            "Each commit decision sends one offer to the writer with its original data");
    }

    private static void CheckCatalogueCard(string path)
    {
        var extracted = new ExtractedProduct { ProductName = "Energy Drink", Brand = "Red Bull", PackSizeText = "4 x 250ml", Price = "$4" };
        var pending = new TaskCompletionSource<List<Product>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = 0;
        var settings = Path.Combine(Path.GetDirectoryName(path)!, "theme.txt");
        var window = new MainWindow(path, (_, _) => Task.FromResult(new List<ExtractedProduct> { extracted }),
            loadFlyers: false, readCatalogue: _ => ++requests == 1 ? pending.Task : Task.FromResult(new List<Product>
            {
                new() { ID = 130, Name = "Red Bull Energy Drink", Brand = "Red Bull", PackSizeText = "4x250ml" },
                new() { ID = 117, Name = "V Energy Drink", Brand = "V", PackSizeText = "4x250ml" }
            }), themeSettingsPath: settings, readOffers: (id, retailer, _) =>
            {
                Check(id == 130 && retailer == "Offline", "Offer lookup uses the confirmed ProductID and flyer retailer");
                var offer = ReviewerOffer.Create(extracted, 2, 2, "new-flyer.pdf", "prior-page.pdf", 1,
                    new DateOnly(2026, 11, 25), new DateOnly(2026, 12, 1));
                offer.ID = 70;
                offer.ProductID = id;
                var legacy = new Offer
                {
                    ID = 71, ProductID = id, AdvertisedPrice = 4m, RetailerID = 2, RetailerLocationID = 2,
                    ValidFrom = offer.ValidFrom, ValidTo = offer.ValidTo, SourceDescription = "prior-page.pdf"
                };
                return Task.FromResult<(int, int?, List<Offer>)>((2, 2, [offer, legacy]));
            });
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        try
        {
            window.Show();
            Pump();
            CheckThemes(window, settings);
            typeof(MainWindow).GetField("_selectedFlyer", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(window, new FlyerFile { FileName = "new-flyer.pdf", Retailer = "Offline" });
            typeof(MainWindow).GetField("_selectedPage", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(window, new FlyerPage { FileName = "new-page.pdf", FullPath = "unused.pdf" });
            var getData = (Button)window.FindName("GetDataButton");
            getData.Visibility = Visibility.Visible;
            Click(getData);
            var products = (ListBox)window.FindName("ProductList");
            WaitFor(() => products.Items.Count == 1 && window.Progress.State == "Success",
                "Extraction finishes and displays the offer while the catalogue read is pending");
            pending.SetException(new IOException("Offline catalogue"));
            WaitFor(() => extracted.CanCheckCatalogue, "Failed catalogue lookup allows retry");
            Check(products.Items.Contains(extracted) && extracted.CatalogueStatus.Contains("unavailable"),
                "Catalogue lookup failure keeps the extracted offer and reports its status");

            var expander = Visuals<Expander>(products).Single(item => Equals(item.Header, "Catalogue matches and decision"));
            expander.IsExpanded = true;
            window.UpdateLayout();
            Pump();
            Button Button(string label) => Visuals<Button>(expander).Single(button => Equals(button.Content, label));
            Click(Button("Check catalogue"));
            WaitFor(() => extracted.CatalogueCandidates.Count == 2, "Reviewer refresh shows both classified catalogue candidates");
            var combo = Visuals<ComboBox>(expander).Single();
            combo.SelectedItem = extracted.CatalogueCandidates.Single(item => item.ProductID == 117);
            Pump();
            Check(!Button("Reuse selected ProductID").IsEnabled, "Conflicting brand cannot be selected for reuse");
            combo.SelectedItem = extracted.CatalogueCandidates.Single(item => item.ProductID == 130);
            Pump();
            Check(Visuals<TextBlock>(expander).Any(item => item.Text == "Red Bull Energy Drink"), "Selected catalogue candidate displays matching details");
            Check(Button("Reuse selected ProductID").IsEnabled, "Likely duplicate can be selected for reuse");
            Click(Button("Reuse selected ProductID"));
            Check(extracted.CatalogueReview?.Target?.ProductID == 130, "Reviewer reuse action records the selected ProductID");
            Click(Button("Keep as separate product"));
            Check(extracted.CatalogueReview?.Choice == ProductReviewChoice.KeepSeparate, "Reviewer keep-separate action records its decision");
            Click(Button("Review later"));
            Check(extracted.CatalogueReview?.Choice == ProductReviewChoice.ReviewLater, "Reviewer review-later action records its decision");

            ((TextBox)window.FindName("FlyerValidFromInput")).Text = "2026-11-25";
            ((TextBox)window.FindName("FlyerValidToInput")).Text = "2026-12-01";
            Click(Button("Reuse selected ProductID"));
            WaitFor(() => extracted.SimilarOffers.Count == 2, "Confirmed product shows separate offer candidates");
            var similar = Visuals<Expander>(products).Single(item => Equals(item.Header, "Similar offers"));
            similar.IsExpanded = true;
            window.UpdateLayout();
            Pump();
            Button OfferButton(string label) => Visuals<Button>(similar).Single(button => Equals(button.Content, label));
            var offerList = Visuals<ComboBox>(similar).Single();
            offerList.SelectedItem = extracted.SimilarOffers.Single(offer => offer.OfferID == 71);
            Pump();
            Check(!OfferButton("Reuse selected OfferID").IsEnabled, "Unverified legacy offer cannot be reused from the card");
            offerList.SelectedItem = extracted.SimilarOffers.Single(offer => offer.OfferID == 70);
            Pump();
            Check(OfferButton("Reuse selected OfferID").IsEnabled
                && Visuals<TextBlock>(similar).Any(item => item.Text.Contains("Edition matches")),
                "Equivalent offer displays comparison details and enables reuse");
            Click(OfferButton("Reuse selected OfferID"));
            Check(extracted.OfferReview == OfferReviewChoice.ReuseExisting && extracted.ConfirmedOfferID == 70,
                "Offer reuse is recorded independently from product reuse");
            Click(OfferButton("Keep as distinct offer"));
            Check(extracted.OfferReview == OfferReviewChoice.KeepDistinct, "Offer keep-distinct action records its decision");
            Click(OfferButton("Review offer later"));
            Check(extracted.OfferReview == OfferReviewChoice.ReviewLater, "Offer review-later action records its decision");
            extracted.Promotion = "Bonus cookware credit";
            Check(extracted.SimilarOffers.Count == 0, "Editing a qualifier invalidates the stale offer preview");
        }
        finally
        {
            window.Close();
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    private static async Task CheckOfferReview(ExtractionSaveService saver, SavedExtraction saved, string path)
    {
        Check(!ReviewerOffer.TryDates("", "", out _, out _), "Missing flyer dates do not fall back to commit time");
        Check(!ReviewerOffer.TryDates("2026-12-01", "2026-11-25", out _, out _), "Reversed dates are rejected");
        Check(!ReviewerOffer.TryDates("2026-02-29", "2026-03-01", out _, out _), "Impossible dates are rejected");
        Check(ReviewerOffer.TryDates("2026-11-25", "2026-12-01", out var from, out var to), "Printed dates parse");
        Check(ReviewerOffer.TryDates("2026-11-25", "2026-11-25", out _, out _), "One-day flyer is valid");
        Check(ReviewerOffer.PrintedDates("Wed 25 Nov to Tue 1 Dec 2026") == ("2026-11-25", "2026-12-01"),
            "Coles cover supplies the actual dates");
        Check(ReviewerOffer.PrintedDates("28 December to 3 January 2027") == ("2026-12-28", "2027-01-03"),
            "A printed end year correctly resolves a New Year flyer");
        Check(ReviewerOffer.PrintedDates("25 Nov to 1 Dec 2026 and 23 Nov to 2 Dec 2026") is null,
            "Conflicting printed ranges require operator confirmation");
        var card = new ExtractedProduct
        {
            ProductName = "Finish Ultimate Plus", Brand = "Finish", PackSizeText = "70 Pack",
            Price = "$36", RegularPrice = "$76", Saving = "$40", UnitPrice = "$0.51",
            Promotion = "Bonus cookware credit", MemberOnly = true, MemberPrice = "$34",
            OfferQuantity = 2, MultiBuyPrice = "$68", PromotionTypes = ["Member-only", "Multi-buy"],
            FlyerValidFrom = "2026-11-25", FlyerValidTo = "2026-12-01",
            OfferReview = OfferReviewChoice.ReuseExisting, ConfirmedOfferID = 208
        };
        await saver.UpdateSavedExtractionAsync(saved.Id, [card]).ConfigureAwait(false);
        var restored = (await new SqlCommitService(path).LoadProductsAsync(saved).ConfigureAwait(false)).Single();
        Check(restored.OfferReview == OfferReviewChoice.ReuseExisting && restored.ConfirmedOfferID == 208
            && restored.FlyerValidFrom == card.FlyerValidFrom && restored.FlyerValidTo == card.FlyerValidTo,
            "Offer decision and printed dates survive the SQLite round trip");
        var dates = await new ExtractionReadService(path).GetFlyerDatesAsync(saved.Retailer, saved.FlyerFileName).ConfigureAwait(false);
        Check(dates == ("2026-11-25", "2026-12-01"), "Other pages can reuse dates from the same saved flyer edition");
        Check(await new ExtractionReadService(path).GetFlyerDatesAsync(saved.Retailer, "other-flyer.pdf").ConfigureAwait(false) is null,
            "Unrelated editions never inherit the dates");
        var catalogue = restored.ToCatalogueProduct();
        catalogue.ID = 216;
        Offer Create(string edition = "coles.pdf", int? location = 2)
        {
            var offer = ReviewerOffer.Create(restored, 2, location, edition, "page-005.pdf", 5, from, to);
            offer.ProductID = 216;
            return offer;
        }
        var prior = Create();
        prior.ID = 208;
        prior.SourceDescription = OfferIdentity.Source("coles.pdf", "page-001.pdf");
        prior.FlyerPageNumber = 1;
        Check(OfferIdentity.Candidates(Create(), [prior]).Single().CanReuse, "Equivalent promotion on another page qualifies for explicit reuse");
        Check(!OfferIdentity.Candidates(Create(location: null), [prior]).Single().CanReuse, "Unknown store scope needs review");
        Check(OfferIdentity.Candidates(Create(location: 3), [prior]).Count == 0, "Different store scope is distinct");
        Check(OfferIdentity.Candidates(Create("other.pdf"), [prior]).Count == 0, "Different known flyer edition is distinct");
        var legacy = Create();
        legacy.ID = 228;
        legacy.SourceDescription = "page-005.pdf";
        legacy.ValidFrom = new DateTime(2026, 11, 26);
        legacy.ValidTo = new DateTime(2026, 12, 3);
        Check(OfferIdentity.Candidates(Create(), [legacy]).Single().Relationship == OfferRelationship.PossibleExistingOffer,
            "The legacy same-price overlapping offer gets a warning without enabling reuse");
        foreach (var change in new Action<Offer>[]
        {
            offer => offer.PromoText = "",
            offer => offer.MemberPrice = 33m,
            offer => offer.MemberOnlyFlag = false,
            offer => offer.MultiBuyPrice = 67m,
            offer => offer.MultiBuyQuantity = 3
        })
        {
            var changed = Create();
            change(changed);
            Check(!OfferIdentity.Candidates(changed, [prior]).Single().CanReuse, "Material promotion differences prevent reuse");
        }
        var writes = new List<Offer>();
        async Task<int?> Commit() => (await ReviewerOffer.CommitAsync(restored, restored.ToCatalogueProduct(), Create(), [catalogue],
            _ => Task.FromResult(new List<Offer> { prior }), _ => throw new Exception("Unexpected product insert"),
            offer => { writes.Add(offer); return Task.FromResult(300 + writes.Count); })).ReusedOfferId;

        Check(await Commit() == 208 && writes.Count == 0, "Confirmed equivalent offer bypasses the insert");
        restored.Promotion = "Bonus cookware credit for two packs";
        Check(await Commit() is null && writes.Count == 1, "Changed bonus terms preserve a new offer");
        restored.Promotion = card.Promotion;
        restored.OfferReview = OfferReviewChoice.KeepDistinct;
        Check(await Commit() is null && writes.Count == 2, "Keep distinct inserts an otherwise matching offer");
        restored.OfferReview = OfferReviewChoice.ReviewLater;
        Check(await Commit() is null && writes.Count == 3, "Review later preserves the offer");
        Check(writes.All(offer => offer.ProductID == 216 && offer.ValidFrom == new DateTime(2026, 11, 25)
            && offer.ValidTo == new DateTime(2026, 12, 1) && offer.MemberOnlyFlag == true && offer.MemberPrice == 34m
            && offer.MultiBuyQuantity == 2 && offer.MultiBuyPrice == 68m && offer.PromoText!.Contains("cookware")),
            "Date, bonus, membership and multi-buy details reach the offer writer");
    }

    private static void CheckThemes(MainWindow window, string settings)
    {
        Check(!File.Exists(settings) && ReviewerTheme.ChoiceFor(window) == ReviewerThemeChoice.Dark
            && ((RadioButton)window.FindName("DarkThemeButton")).IsChecked == true, "Fresh Reviewer defaults to Dark");
        var foreground = Color.FromRgb(0, 0, 0);
        foreach (var choice in Enum.GetValues<ReviewerThemeChoice>())
        {
            var radio = (RadioButton)window.FindName(choice + "ThemeButton");
            radio.IsChecked = true;
            radio.RaiseEvent(new RoutedEventArgs(RadioButton.ClickEvent, radio));
            Pump();
            Check(ReviewerTheme.Load(settings) == choice && ReviewerTheme.ChoiceFor(window) == choice,
                choice + " applies and persists the local preference");
            var color = ((SolidColorBrush)window.Foreground).Color;
            Check(color != foreground, choice + " updates the live foreground resource");
            foreground = color;
            var saved = ((SolidColorBrush)window.FindResource("ReviewerSaved")).Color;
            var committed = ((SolidColorBrush)window.FindResource("ReviewerCommitted")).Color;
            Check(saved != committed && saved.B > saved.G && committed.G > committed.R,
                choice + " preserves blue Saved and green Committed");
            foreach (var surface in new[] { "Panel", "Surface", "Saved", "Committed", "Selection", "Error", "Warning" })
                Check(Contrast(color, ((SolidColorBrush)window.FindResource("Reviewer" + surface)).Color) >= 4.5,
                    choice + " text contrast against " + surface);

            var reopened = new MainWindow(Path.Combine(Path.GetDirectoryName(settings)!, "fixture.db"),
                loadFlyers: false, themeSettingsPath: settings);
            Check(ReviewerTheme.ChoiceFor(reopened) == choice
                && ((RadioButton)reopened.FindName(choice + "ThemeButton")).IsChecked == true,
                "Reopened Reviewer restores the saved " + choice + " theme");
            reopened.Close();
        }
    }

    private static double Contrast(Color left, Color right)
    {
        static double Channel(byte value) => value / 255d <= 0.04045 ? value / 255d / 12.92
            : Math.Pow((value / 255d + 0.055) / 1.055, 2.4);
        static double Luminance(Color color) => 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
        var a = Luminance(left);
        var b = Luminance(right);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));

    private static void WaitFor(Func<bool> condition, string message)
    {
        var timer = Stopwatch.StartNew();
        while (!condition() && timer.Elapsed < TimeSpan.FromSeconds(5))
        {
            Pump();
            Thread.Sleep(10);
        }
        Check(condition(), message);
    }

    private static IEnumerable<T> Visuals<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var descendant in Visuals<T>(child)) yield return descendant;
        }
    }
}
