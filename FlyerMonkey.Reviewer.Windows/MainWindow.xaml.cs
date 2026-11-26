using FlyerMonkey.Reviewer.Windows.Models;
using FlyerMonkey.Reviewer.Windows.Services;
using FlyerMonkey.Shared.Model;
using Microsoft.Data.Sqlite;
using PDFtoImage;
using SQLServerConnection.Data;
using Syncfusion.Pdf.Parsing;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;


namespace FlyerMonkey.Reviewer.Windows
{

    public class FlyerPage
    {
        public int PageNumber { get; set; }

        public string FileName { get; set; } = "";

        public string FullPath { get; set; } = "";
        public string DisplayName => $"Page {PageNumber}";

        public ImageSource? Thumbnail { get; set; }

        public string ExtractionStatus { get; set; } = "";

        public long? ExtractionRunId { get; set; }
    }

    public class FlyerFile : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        public string StatusColour { get; private set; } = "White";
        public string StatusWarning { get; private set; } = "";
        public bool IsComplete { get; private set; }
        public void SetStatus(bool complete, FlyerSplitState split)
        {
            IsComplete = complete; StatusColour = complete ? "#8BD49B" : split.Pages.Length > 0 ? "#70B8F0" : "White";
            StatusWarning = split.Warning;
            foreach (var name in new[] { nameof(IsComplete), nameof(StatusColour), nameof(StatusWarning) }) PropertyChanged?.Invoke(this, new(name));
        }
        public string Retailer { get; set; } = "";
        public string FlyerDate { get; set; } = "";
        public string PageDescription { get; set; } = "";
        public string FileName { get; set; } = "";
        public string FullPath { get; set; } = "";
        internal DateOnly? PublicationDateForSort { get; set; }
    }
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly CancellationTokenSource _windowLifetime = new();
        private readonly OperationProgress _progress = new();
        private System.Windows.Threading.DispatcherTimer? _progressTimer;
        private bool _windowClosed;
        private readonly Func<string, CancellationToken, Task<byte[]>> _generateClassic;
        private readonly string _imageFolder;
        private readonly ArtworkArchive _artworkArchive;
        private readonly string _themeSettingsPath;
        private ReviewerThemeChoice _theme;

        private void Theme_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not RadioButton { Tag: ReviewerThemeChoice choice }) return;
            _theme = choice;
            ReviewerTheme.Apply(this, choice);
            foreach (Window window in OwnedWindows)
                ReviewerTheme.Apply(window, choice);
            try { ReviewerTheme.Save(_themeSettingsPath, choice); }
            catch (IOException) { ShowMessage("The theme is active for this session. The local preference could not be saved."); }
            catch (UnauthorizedAccessException) { ShowMessage("The theme is active for this session. The local preference could not be saved."); }
        }

        private MessageBoxResult ShowMessage(string message, string title = "Flyer Monkey Reviewer",
            MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None) =>
            ReviewerTheme.ShowMessage(this, _theme, message, title, buttons, icon);
        public OperationProgress Progress => _progress;
        private void RefreshResponsiveLayout()
        {
            if (FlyerColumn == null || ActualWidth <= 0) return;
            var columns = ReviewerWindowLayout.Columns(Math.Max(0, ActualWidth - 16));
            FlyerColumn.Width = new GridLength(columns.Flyers); PagesColumn.Width = new GridLength(columns.Pages);
            ProductColumn.Width = new GridLength(columns.Products); SavedColumn.Width = new GridLength(columns.Saved);
            SelectedPageReview.RefreshForDisplayChange();
        }
        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            Dispatcher.BeginInvoke(new Action(() => { if (_windowClosed) return; ReviewerWindowLayout.ConstrainToCurrentMonitor(this,newDpi); RefreshResponsiveLayout(); }));
        }

        protected override void OnClosed(EventArgs e)
        {
            _windowClosed = true; _processingCancellation?.Cancel(); _progressTimer?.Stop();
            SelectedPageReview.Dispose();
            _windowLifetime.Cancel();
            _windowLifetime.Dispose();
            base.OnClosed(e);
        }
        private bool _processing;
        private static readonly SemaphoreSlim ImageRequests = new(2, 2);
        private readonly Dictionary<ExtractedProduct, CancellationTokenSource> _craterJobs = new();
        private async Task<byte[]> GenerateLimitedAsync(string prompt, CancellationToken token, Action? started = null)
        {
            await ImageRequests.WaitAsync(token);
            try
            {
                token.ThrowIfCancellationRequested();
                if (started != null) await Dispatcher.InvokeAsync(started);
                // Includes synchronous provider setup/validation without blocking WPF.
                return await Task.Run(() => { token.ThrowIfCancellationRequested(); return _generateClassic(prompt, token); });
            }
            finally { ImageRequests.Release(); }
        }
        private long? _activeCommitId;
        private bool _restoringSavedSelection;
        private bool CanEditCurrentExtraction => SavedExtractionList.SelectedItem is not SavedExtraction extraction || extraction.Status == "Saved";
        private readonly ProcessingGate _processingGate = new();
        private CancellationTokenSource? _processingCancellation;
        private OperationSession? BeginProcessing(bool cancellable = false, string operation = "Processing")
        {
            if (_windowClosed) return null;
            if (_craterJobs.Count != 0)
            {
                EditingContextText.Text = "Finish or cancel pending card images before " + operation + ". Completed working images still require Save.";
                return null;
            }
            var lease = _processingGate.TryEnter();
            if (lease == null) return null;
            _processing = true;
            _processingCancellation = new();
            var progress = _progress.Start(operation);
            foreach (var c in new UIElement[] { FlyerCompletionButton, SplitFlyerButton, GetDataButton, SaveDataButton, CommitButton, FlyerList, PageList, SavedExtractionList, ProductList, AddOfferButton }) c.IsEnabled = false;
            CancelProcessingButton.Content = "Cancel " + (operation.Contains("image", StringComparison.OrdinalIgnoreCase) ? "image" : "extraction");
            CancelProcessingButton.IsEnabled = cancellable;
            return new OperationSession(progress, () => {
                lease.Dispose();
                _processing = false;
                _processingCancellation.Dispose();
                _processingCancellation = null;
                if (_windowClosed) return;
                foreach (var c in new UIElement[] { FlyerCompletionButton, SplitFlyerButton, GetDataButton, SaveDataButton, FlyerList, PageList, SavedExtractionList, ProductList, AddOfferButton }) c.IsEnabled = true;
                CommitButton.IsEnabled = SavedExtractionList.SelectedItem is SavedExtraction item && item.Status == "Saved";
                SaveDataButton.IsEnabled = CanEditCurrentExtraction;
                AddOfferButton.IsEnabled = CanEditCurrentExtraction;
                CancelProcessingButton.IsEnabled = false;
            });
        }
        private sealed class ProcessingLease(Action release) : IDisposable { public void Dispose() => release(); }
        private void CancelProcessing_Click(object sender, RoutedEventArgs e) => _processingCancellation?.Cancel();
        private FlyerFile? _selectedFlyer;
        private FlyerPage? _selectedPage;
        private BitmapImage CreateThumbnail(string pdfPath)
        {
            using var pdfStream = File.OpenRead(pdfPath);
            using var imageStream = new MemoryStream();

            Conversion.SavePng(
                imageStream,
                pdfStream,
                page: 0);

            imageStream.Position = 0;

            var bitmap = new BitmapImage();

            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = imageStream;
            bitmap.EndInit();
            bitmap.Freeze();

            return bitmap;
        }
        private readonly ObservableCollection<ExtractedProduct> _products = new();
        private void ProductList_PreviewMouseWheel(
    object sender,
    MouseWheelEventArgs e)
        {
            const double pixelsPerNotch = 30;

            var scrollViewer = FindVisualChild<ScrollViewer>(ProductList);

            if (scrollViewer == null)
                return;

            double notches = e.Delta / 120.0;

            scrollViewer.ScrollToVerticalOffset(
                scrollViewer.VerticalOffset -
                (notches * pixelsPerNotch));

            e.Handled = true;
        }
        private static T? FindVisualChild<T>(
    DependencyObject parent)
    where T : DependencyObject
        {
            for (int i = 0;
                 i < VisualTreeHelper.GetChildrenCount(parent);
                 i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is T match)
                    return match;

                var result = FindVisualChild<T>(child);

                if (result != null)
                    return result;
            }

            return null;
        }
        private bool _refreshingFlyerList;
        private async void FlyerList_SelectionChanged(
    object sender,
    SelectionChangedEventArgs e)
        {
            if (_refreshingFlyerList) return;
            if (FlyerList.SelectedItem is not FlyerFile flyer)
            {
                _selectedFlyer = null;
                _selectedPage = null;
                return;
            }

            string fileName = flyer.FileName;

            // Temporary: use the existing flyer date.
            if (!DateTime.TryParse(flyer.FlyerDate, out DateTime startDate))
            {
                ShowMessage(
                    $"Unable to determine the flyer date:\n{flyer.FlyerDate}",
                    "FlyerMonkey - Invalid Date",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                FlyerList.SelectedItem = null;
                return;
            }

            // Seven-day promotional period, inclusive.
            DateTime endDate = startDate.AddDays(6);

            string message =
                $"Retailer: {flyer.Retailer}\n\n" +
                $"Flyer: {fileName}\n\n" +
                $"Valid From: {startDate:dddd, dd MMMM yyyy}\n" +
                $"Valid To: {endDate:dddd, dd MMMM yyyy}\n\n" +
                "Please verify these dates against the catalogue.\n\n" +
                "YES = Confirm and proceed\n" +
                "NO = Cancel";

            var result = ShowMessage(
                message,
                "FlyerMonkey - Confirm Flyer Dates",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                FlyerList.SelectedItem = null;
                return;
            }

            _selectedFlyer = flyer;

            FlyerCompletionButton.Content = flyer.IsComplete ? "Reopen flyer" : "Mark Complete";
            await LoadSplitPagesAsync(flyer);
        }

        private void PageList_SelectionChanged(
    object sender,
    SelectionChangedEventArgs e)
        {
            if (_restoringSavedSelection) return;
            if (PageList.SelectedItem is not FlyerPage page)
            {
                _selectedPage = null;
                SelectedPageReview.Clear();
                GetDataButton.Visibility = Visibility.Collapsed;
                return;
            }

            _selectedPage = page;
            SavedExtractionList.SelectedItem = null;
            FlyerValidFromInput.IsReadOnly = FlyerValidToInput.IsReadOnly = false;
            ShowFlyerDates([]);
            _products.Clear();
            EditingContextText.Text = "";
            AddOfferButton.IsEnabled = !_processing;

            SelectedPageReview.LoadPage(page.FullPath);

            GetDataButton.Visibility = Visibility.Visible;
            if (page.ExtractionRunId.HasValue && SavedExtractionList.ItemsSource is IEnumerable<SavedExtraction> entries)
            {
                var entry = entries.FirstOrDefault(x => x.Id == page.ExtractionRunId.Value);
                if (entry != null) SavedExtractionList.SelectedItem = entry;
            }
        }
        private async Task LoadSplitPagesAsync(FlyerFile flyer)
        {
            // Remember the currently selected page.
            string? selectedPageFileName =
                _selectedPage?.FileName;

            PageList.Items.Clear();

            string splitRoot =
                _splitRoot;

            string flyerFolder = Path.Combine(
                splitRoot,
                Path.GetFileNameWithoutExtension(flyer.FileName));



            var discovery = DiscoverFlyer(flyer);
            RefreshFlyerStatus(flyer);
            FlyerStatusWarning.Text = discovery.Warning;
            var pageFiles = discovery.Pages.ToList();

            if (pageFiles.Count > 0)
            {
                string sqlitePath =
                    _databasePath;

                var extractionReadService =
                    new ExtractionReadService(sqlitePath);

                var extractionStates =
                    await extractionReadService.GetExtractionStatesAsync();

                foreach (var pageFile in pageFiles)
                {
                    var page = new FlyerPage
                    {
                        PageNumber = PageList.Items.Count + 1,
                        FileName = Path.GetFileName(pageFile),
                        FullPath = pageFile,
                        Thumbnail = await Task.Run(() => CreateThumbnail(pageFile))
                    };

                    var extractionState =
                        extractionStates.FirstOrDefault(x =>
                            x.FlyerFileName == flyer.FileName &&
                            x.PageFileName == page.FileName);

                    if (extractionState != null)
                    {
                        page.ExtractionStatus = extractionState.Status;
                        page.ExtractionRunId = extractionState.Id;
                    }

                    if (_windowClosed || (_selectedFlyer != null && !ReferenceEquals(_selectedFlyer, flyer))) return;
                    PageList.Items.Add(page);
                }

                // Restore the previously selected page.
                FlyerPage? pageToSelect = null;

                if (!string.IsNullOrEmpty(selectedPageFileName))
                {
                    pageToSelect = PageList.Items
                        .OfType<FlyerPage>()
                        .FirstOrDefault(p =>
                            p.FileName == selectedPageFileName);
                }

                // Default to page 1 if no previous selection.
                pageToSelect ??= PageList.Items
                    .OfType<FlyerPage>()
                    .FirstOrDefault();

                PageList.SelectedItem = pageToSelect;
            }
        }
        private void SplitPdf(string sourcePdf)
        {
            string fileNameWithoutExtension =
                Path.GetFileNameWithoutExtension(sourcePdf);

            string splitRoot = _splitRoot;

            string outputFolder =
                Path.Combine(splitRoot, fileNameWithoutExtension);

            if (Directory.Exists(outputFolder)) throw new InvalidOperationException("Split pages already exist. Existing completed work was preserved.");
            Directory.CreateDirectory(splitRoot);
            var stagingFolder = outputFolder + ".pending-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(stagingFolder);

            using var pdfStream = File.OpenRead(sourcePdf);

            var splitter = new PdfSplitService();
            var pages = splitter.SplitPdf(pdfStream);

            try {
            for (int i = 0; i < pages.Count; i++)
            {
                string outputPath = Path.Combine(
                    stagingFolder,
                    $"page-{i + 1:000}.pdf");

                File.WriteAllBytes(outputPath, pages[i]);
            }
            Directory.Move(stagingFolder, outputFolder);
            } catch { throw new IOException($"Split was not published. Existing work is preserved; staged files remain at {stagingFolder}."); }
        }

        private void AddOfferButton_Click(object sender, RoutedEventArgs e)
        {
            if (!CanEditCurrentExtraction) return;
            var result = ShowMessage(
                "Add a new offer?",
                "Add Offer",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            var newProduct = new ExtractedProduct
            {
                ProductName = "New Offer"
            };

            _products.Add(newProduct);

            ProductList.SelectedItem = newProduct;
            ProductList.ScrollIntoView(newProduct);
        }
        private async void DeleteOfferButton_Click(
    object sender,
    RoutedEventArgs e)
        {
            if (!CanEditCurrentExtraction) return;
            if (sender is not Button button ||
                button.Tag is not ExtractedProduct product)
                return;

            var result = ShowMessage(
                $"Delete '{product.ProductName}'?",
                "Delete offer",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            var targetId = (SavedExtractionList.SelectedItem as SavedExtraction)?.Id ?? _selectedPage?.ExtractionRunId;
            if (targetId == null)
            {
                ShowMessage(
                    "This extraction has not been saved yet.",
                    "Delete offer",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            _products.Remove(product);

            string databasePath =
                _databasePath;

            var saver =
                new ExtractionSaveService(databasePath);

            await saver.UpdateProductsAsync(
                checked((int)targetId.Value),
                _products);

            await LoadSavedExtractionsAsync();
        }

        private void EditableField_Click(
    object sender,
    MouseButtonEventArgs e)
        {
            if (!CanEditCurrentExtraction) return;
            if (sender is not TextBlock textBlock)
                return;

            if (textBlock.Parent is not Grid grid)
                return;

            var editor = grid.Children
                .OfType<TextBox>()
                .FirstOrDefault();

            if (editor is null)
                return;

            textBlock.Visibility = Visibility.Collapsed;
            editor.Visibility = Visibility.Visible;

            editor.Focus();
            editor.SelectAll();
        }
        private void CategoryDisplay_Click(
    object sender,
    MouseButtonEventArgs e)
        {
            if (!CanEditCurrentExtraction) return;
            if (sender is not TextBlock textBlock)
                return;

            if (textBlock.DataContext is not ExtractedProduct product)
                return;
            // Keep the offer selected while its category editor is open.
            ProductList.SelectedItem = product;
            // Temporary category catalogue.
            // Later we'll load this from the API.
            var categories = new[]
            {
        "Fruit & Vegetables",
        "Meat & Poultry",
        "Seafood",
        "Deli",
        "Bakery",
        "Dairy & Eggs",
        "Frozen Foods",
        "Pantry",
        "Snacks & Confectionery",
        "Drinks",
        "Coffee & Tea",
        "Breakfast & Cereals",
        "Health & Beauty",
        "Household & Cleaning",
        "Baby & Toddler",
        "Pet Supplies",
        "Health & Wellness",
        "General Merchandise",
        "Other / Uncategorised"
    };

            // Translate older AI category suggestions.
            var legacyCategories = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["Meat"] = "Meat & Poultry",
                ["Dairy"] = "Dairy & Eggs",
                ["Freezer"] = "Frozen Foods",
                ["Snacks"] = "Snacks & Confectionery",
                ["Beverages"] = "Drinks",
                ["Soft Drinks"] = "Drinks"
            };

            string? primary = product.PrimaryCategory;

            if (string.IsNullOrWhiteSpace(primary))
            {
                primary = product.Category?.Trim();

                if (primary != null &&
                    legacyCategories.TryGetValue(primary, out var mapped))
                {
                    primary = mapped;
                }
            }

            var selected = product.Categories.ToList();

            if (selected.Count == 0 && primary != null)
                selected.Add(primary);

            var editor = new CategoryEditorWindow(
    product.ProductName,
    categories,
    primary,
    selected)
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.Manual
            };

            // Position the editor near the left edge of Reviewer.
            // Position editor over the Upcoming Flyers column.
            editor.Left = this.Left - 150;
            editor.Top = this.Top + 100;

            ReviewerTheme.Apply(editor, _theme);
            if (editor.ShowDialog() == true)
            {
                product.PrimaryCategory = editor.PrimaryCategory;
                product.Categories = editor.SelectedCategories;

                // Preserve compatibility with existing extraction pipeline.
                product.Category = editor.PrimaryCategory;

                // Refresh the visible offer card.
                ProductList.Items.Refresh();
            }

            e.Handled = true;
        }


        private void PromotionTypes_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!CanEditCurrentExtraction) return;
            if (sender is not Button button)
                return;

            if (button.DataContext is not ExtractedProduct product)
                return;

            ProductList.SelectedItem = product;

            // Temporary lookup list, matching Azure SQL.
            var promotionTypes = new[]
            {
        "Standard special",
        "Half price",
        "Percentage off",
        "Dollar saving",
        "Multi-buy",
        "Member-only",
        "Bonus quantity",
        "Everyday low price",
        "Other / Unclassified"
    };

            var selected = product.PromotionTypes?.ToList()
                           ?? new List<string>();

            // Suggest a classification for previously unclassified offers.
            if (selected.Count == 0)
            {
                string promotion = product.Promotion?.Trim() ?? "";

                if (promotion.Contains("ANY 2 FOR",
                        StringComparison.OrdinalIgnoreCase) ||
                    promotion.Contains("MULTI-BUY",
                        StringComparison.OrdinalIgnoreCase))
                {
                    selected.Add("Multi-buy");
                }
                else if (promotion.Contains("HALF PRICE",
                             StringComparison.OrdinalIgnoreCase))
                {
                    selected.Add("Half price");
                }
                else if (promotion.Contains("EVERYDAY LOW PRICE",
                             StringComparison.OrdinalIgnoreCase))
                {
                    selected.Add("Everyday low price");
                }
            }

            var editor = new PromotionEditorWindow(
                product.ProductName,
                product.Promotion,
                promotionTypes,
                selected)
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.Manual
            };

            // Match the working Category Editor position.
            editor.Left = this.Left - 100;
            editor.Top = this.Top + 120;

            ReviewerTheme.Apply(editor, _theme);
            if (editor.ShowDialog() == true)
            {
                product.PromotionTypes =
                    editor.SelectedPromotionTypes;

                ProductList.Items.Refresh();
            }

            e.Handled = true;
        }

        private void EditableField_LostFocus(
            object sender,
            RoutedEventArgs e)
        {
            FinishEditing(sender);
        }

        private void EditableField_KeyDown(
            object sender,
            KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                FinishEditing(sender);
            }
        }

        private void FinishEditing(object sender)
        {
            if (sender is not TextBox editor)
                return;

            if (editor.Parent is not Grid grid)
                return;

            var display = grid.Children
                .OfType<TextBlock>()
                .FirstOrDefault();

            if (display is null)
                return;

            if (BindingOperations.GetBinding(editor, TextBox.TextProperty)?.Path.Path == "PackSizeText" && editor.DataContext is ExtractedProduct edited) edited.PackSizeManuallyCorrected = true;
            editor.Visibility = Visibility.Collapsed;
            display.Visibility = Visibility.Visible;
        }
        private async void CommitButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            using var processing = BeginProcessing(false, "Committing to SQL");
            if (processing == null) return;
            if (SavedExtractionList.SelectedItem is not SavedExtraction saved)
            {
                ShowMessage(
                    "Select a locally saved extraction first.",
                    "Commit",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            // Prevent an already committed batch being committed again
            if (saved.Status != "Saved")
            {
                ShowMessage(
                    saved.Status == "Committing" ? "Recovery Required. This batch has an uncertain SQL outcome and cannot be retried." : "This extraction has already been committed.",
                    "Commit",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            var currentProducts = _products.ToList();
            var errors = ProductValidationService.ValidateBatch(currentProducts);
            if (!ReviewerOffer.TryDates(FlyerValidFromInput.Text, FlyerValidToInput.Text, out var validFrom, out var validTo))
                errors.Add("Enter the printed flyer start and end dates as yyyy-MM-dd.");
            if (OfferIdentity.Source(saved.FlyerFileName, saved.PageFileName).Length > 250)
                errors.Add("Flyer and page names are too long to store the offer source.");
            if (string.IsNullOrWhiteSpace(saved.Retailer)) errors.Add("Retailer is required.");
            if (string.IsNullOrWhiteSpace(saved.PageFileName)) errors.Add("Source page is required.");
            if (errors.Count > 0) { ShowMessage(string.Join("\n", errors), "Fix errors before Commit"); return; }
            var confirm = ShowMessage(
                $"Commit {saved.ProductCount} products from {saved.PageFileName} to SQL?",
                "Commit to SQL",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
                return;

            // Existing try block continues here...


            bool sqlPersisted = false, acknowledged = false;
            string completedSummary = "SQL batch persisted.";
            try
            {
                string sqlitePath =
                    _databasePath;

                var commitService =
                    new SqlCommitService(sqlitePath);

                StoreFlyerDates(currentProducts);
                await new ExtractionSaveService(sqlitePath).UpdateSavedExtractionAsync(saved.Id, currentProducts);
                var products = await commitService.LoadProductsAsync(saved);

                var sqlConnectionString =
    Environment.GetEnvironmentVariable(
        "FLYERMONKEY_SQL_CONNECTION")
    ?? throw new InvalidOperationException(
        "FLYERMONKEY_SQL_CONNECTION is not set.");

var blobConnectionString =
    Environment.GetEnvironmentVariable(
        "FLYERMONKEY_BLOB_CONNECTION")
    ?? throw new InvalidOperationException(
        "FLYERMONKEY_BLOB_CONNECTION is not set.");

                var imageUploadService =
                    new ProductImageUploadService(
                        blobConnectionString);

                var retailerRepository =
    new RetailerRepository(sqlConnectionString);

                var retailerId =
    await retailerRepository.GetRetailerIdByNameAsync(
        saved.Retailer);

                if (retailerId == null)
                {
                    throw new InvalidOperationException(
                        $"Retailer '{saved.Retailer}' was not found.");
                }

                var retailerLocationRepository =
    new RetailerLocationRepository(sqlConnectionString);

                var retailerLocationId =
                    await retailerLocationRepository.GetLocationIdAsync(
                        retailerId.Value,
                        "Butler");

                await commitService.ClaimAsync(saved.Id);
                _activeCommitId = saved.Id;
                saved.IsCommitActive = true;
                saved.Status = "Committing";
                await using var sqlBatch = await ReviewerSqlBatch.OpenAsync(sqlConnectionString);
                var catalogue = await sqlBatch.LoadCatalogueForMatchingAsync();
                var identityReviews = new List<string>();
                var reusedCount = 0;
                var addedCount = 0;
                var reusedOfferCount = 0;

                foreach (var extractedProduct in products)
                {
                    var product = extractedProduct.ToCatalogueProduct();

                    var offer = ReviewerOffer.Create(extractedProduct, retailerId.Value, retailerLocationId,
                        saved.FlyerFileName, saved.PageFileName, saved.PageNumber, validFrom, validTo);
                    var (productId, match, reusedOfferId) = await ReviewerOffer.CommitAsync(
                        extractedProduct, product, offer, catalogue,
                        id => sqlBatch.LoadOffersForMatchingAsync(id, retailerId.Value), async newProduct =>
                        {
                            if (!string.IsNullOrWhiteSpace(extractedProduct.ImageFilePath))
                                newProduct.ImageBlobPath = await imageUploadService.UploadAsync(extractedProduct.ImageFilePath);

                            var id = await sqlBatch.AddProductAsync(newProduct);
                            await sqlBatch.SaveProductCategoriesAsync(id,
                                extractedProduct.PrimaryCategory ?? extractedProduct.Category,
                                extractedProduct.Categories);
                            return id;
                        }, sqlBatch.AddOfferAsync);

                    if (reusedOfferId.HasValue)
                    {
                        reusedOfferCount++;
                        continue;
                    }
                    if (extractedProduct.OfferReview == OfferReviewChoice.ReuseExisting)
                        identityReviews.Add($"{product.Name}: the confirmed offer could not be verified. A separate offer was saved.");
                    else if (extractedProduct.OfferReview == OfferReviewChoice.ReviewLater)
                        identityReviews.Add($"{product.Name}: a separate offer was saved for later review.");

                    if (match.ReuseId.HasValue)
                        reusedCount++;
                    else
                    {
                        product.ID = productId;
                        catalogue.Add(product);
                    }

                    var reviewChoice = extractedProduct.CatalogueReview?.Choice;
                    if (reviewChoice == ProductReviewChoice.ReuseExisting && match.ReuseId is null)
                        identityReviews.Add($"Product {productId} ({product.Name}) was saved separately. The confirmed catalogue match is no longer valid.");
                    else if (reviewChoice == ProductReviewChoice.ReviewLater)
                        identityReviews.Add($"Product {productId} ({product.Name}) was saved separately for later review.");
                    else if (reviewChoice != ProductReviewChoice.KeepSeparate && match.NeedsReview)
                        identityReviews.Add($"Product {productId} ({product.Name}) may match {string.Join(", ", match.CandidateIds)}.");

                    addedCount++;
                }
                await sqlBatch.CommitAsync();
                sqlPersisted = true;
                await commitService.MarkCommittedAsync(saved.Id);
                saved.Status = "Committed";
                acknowledged = true;
                completedSummary = $"Inserted {addedCount} offers. Reused {reusedOfferCount} existing offers and reused catalogue products for {reusedCount} new offers.";
                if (identityReviews.Count > 0)
                    completedSummary += $" Catalogue review needed for {identityReviews.Count} products.";
                processing.Report("SQL and SQLite persisted; refreshing the Reviewer...");

                if (_selectedFlyer is not null)
                {
                    await LoadSplitPagesAsync(_selectedFlyer);
                }

                await LoadSavedExtractionsAsync();
                processing.Success(completedSummary);
                if (identityReviews.Count > 0)
                    ShowMessage(string.Join(Environment.NewLine, identityReviews), "Catalogue matches to review",
                        MessageBoxButton.OK, MessageBoxImage.Information);



            }
            catch (Exception ex)
            {
                // Refresh durable state, including a claim acquired by another Reviewer.
                try { await LoadSavedExtractionsAsync(); } catch { /* Keep the original failure visible. */ }
                if (acknowledged) processing.Success(completedSummary + " Refresh issue: " + ex.Message);
                else processing.Fail((sqlPersisted ? "SQL succeeded but SQLite acknowledgement failed. Recovery Required; do not retry. " : "Commit failed or is uncertain. Review recovery state; do not retry. ") + ex.Message);
            }
            finally
            {
                _activeCommitId = null;
                saved.IsCommitActive = false;
                if (SavedExtractionList.ItemsSource is IEnumerable<SavedExtraction> entries)
                    foreach (var entry in entries) entry.IsCommitActive = false;
            }
        }

        private async void CraterItButton_Click(object sender, RoutedEventArgs e)
        {
            if (_windowClosed || _processing || !CanEditCurrentExtraction || sender is not Button button || button.Tag is not ExtractedProduct product
                || !_products.Contains(product) || _craterJobs.ContainsKey(product)) return;
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_windowLifetime.Token);
            _craterJobs.Add(product, cancellation);
            product.IsCraterGenerating = true; product.CraterStatus = "Queued";
            var workflow = new ClassicArtworkWorkflow(product, _artworkArchive,
                (prompt, token) => GenerateLimitedAsync(prompt, token, () => { if (!cancellation.IsCancellationRequested) product.CraterStatus = "Generating"; }));
            using var stopAssignment = cancellation.Token.Register(workflow.Close);
            try
            {
                await Task.Run(() => workflow.GenerateAsync(null, true, cancellation.Token, deferAssignment: true));
                cancellation.Token.ThrowIfCancellationRequested();
                if (_windowClosed || !_products.Contains(product)) throw new OperationCanceledException();
                // Assignment stays on WPF and targets the captured product, never a list index.
                await Task.Run(() => workflow.AcceptAsync(async assign => { await Dispatcher.InvokeAsync(assign); }));
                product.CraterStatus = "Complete — Save to persist";
            }
            catch (Exception) when (cancellation.IsCancellationRequested) { product.CraterStatus = "Cancelled — provider cancellation uncertain; returned artwork retained in archive"; }
            catch (OperationCanceledException) { product.CraterStatus = "Cancelled — returned artwork retained in archive"; }
            catch (Exception ex) { product.CraterStatus = "Failed — " + ex.Message; }
            finally { workflow.Close(); _craterJobs.Remove(product); product.IsCraterGenerating = false; }
        }
        private void CancelCrater_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: ExtractedProduct product } && _craterJobs.TryGetValue(product, out var cancellation))
            { product.CraterStatus = "Cancelling — provider cancellation uncertain"; cancellation.Cancel(); }
        }
        private void EditImage_Click(object sender, RoutedEventArgs e)
        {
            if (!CanEditCurrentExtraction || _processing || sender is not Button button || button.Tag is not ExtractedProduct product) return;
            var editor = new ClassicImageEditorWindow(product, _imageFolder, _windowLifetime.Token, (prompt,token) => GenerateLimitedAsync(prompt,token),
                (name, cancellable) => BeginProcessing(cancellable,name), () => _processingCancellation?.Token ?? CancellationToken.None, _artworkArchive) { Owner = this };
            ReviewerTheme.Apply(editor, _theme);
            editor.ShowDialog();
        }
        private async void RemoveImage_Click(object sender, RoutedEventArgs e)
        {
            if (!CanEditCurrentExtraction || sender is not Button button || button.Tag is not ExtractedProduct product) return;
            using var processing = BeginProcessing(false,"Archiving image"); if(processing == null) return;
            try { await _artworkArchive.DetachAsync(product); processing.Success("Image detached; archived artwork/provenance kept. Save to persist the draft reference."); }
            catch(Exception ex) { processing.Fail(ex.Message); }
        }

        private void ProductImage_Click(
    object sender,
    MouseButtonEventArgs e)
        {
            if (sender is not Image image ||
                image.DataContext is not ExtractedProduct product ||
                string.IsNullOrWhiteSpace(product.ImageFilePath) ||
                !File.Exists(product.ImageFilePath))
            {
                return;
            }

            var previewImage = new Image
            {
                Source = new BitmapImage(new Uri(product.ImageFilePath)),
                Stretch = Stretch.Uniform,
                Margin = new Thickness(10)
            };

            var previewWindow = new Window
            {
                Title = product.ProductName,
                Content = previewImage,
                Width = 800,
                Height = 800,
                WindowStartupLocation = WindowStartupLocation.Manual
            };

            previewWindow.Owner = this;
            previewWindow.Left = this.Left + 80;
            previewWindow.Top = this.Top + 80;

            
            previewWindow.KeyDown += (_, args) =>
            {
                if (args.Key == Key.Escape)
                {
                    args.Handled = true;
                    previewWindow.Close();
                }
            };

            ReviewerTheme.Apply(previewWindow, _theme);
            previewWindow.Show();
        }
        private async void GetDataButton_Click(object sender, RoutedEventArgs e)
        {
            using var processing = BeginProcessing(true, "Extracting products");
            if (processing == null || _selectedPage == null || _selectedFlyer == null) return;
            var flyer = _selectedFlyer; var page = _selectedPage;
            try
            {
                var reader = new ExtractionReadService(_databasePath);
                var savedProducts = await reader.GetProductsAsync(flyer.FileName,page.FileName);
                _processingCancellation!.Token.ThrowIfCancellationRequested();
                if (_windowClosed) throw new OperationCanceledException();
                if (savedProducts is { Count: > 0 })
                {
                    _products.Clear(); foreach(var product in savedProducts) _products.Add(product);
                    ShowFlyerDates(savedProducts);
                    _ = RefreshCatalogueMatchesAsync(savedProducts);
                    processing.Success($"Restored {savedProducts.Count} products from SQLite."); return;
                }
                GetDataButton.Content = "Getting Data...";
                var products = await ProcessingGate.CompleteAsync(_extractProducts(page.FullPath,_processingCancellation.Token),_processingCancellation.Token);
                if (_windowClosed) throw new OperationCanceledException();
                foreach(var product in products) { ExtractionNormalizationService.NormalizeFresh(product); ProductValidationService.SanitizeProduct(product); }
                _products.Clear();foreach(var product in products)_products.Add(product);
                if (string.IsNullOrWhiteSpace(FlyerValidFromInput.Text) && string.IsNullOrWhiteSpace(FlyerValidToInput.Text))
                {
                    var coverDates = await Task.Run(() => ReadCoverDates(flyer.FullPath));
                    if (_windowClosed) throw new OperationCanceledException();
                    if (string.IsNullOrWhiteSpace(FlyerValidFromInput.Text) && string.IsNullOrWhiteSpace(FlyerValidToInput.Text))
                    {
                        if (coverDates is { } printed)
                        {
                            FlyerValidFromInput.Text = printed.From;
                            FlyerValidToInput.Text = printed.To;
                        }
                        else
                        {
                            ShowFlyerDates(products);
                            if (string.IsNullOrEmpty(FlyerValidFromInput.Text))
                                try
                                {
                                    var savedDates = await reader.GetFlyerDatesAsync(flyer.Retailer, flyer.FileName);
                                    if (savedDates is { } dates && !_windowClosed
                                        && string.IsNullOrWhiteSpace(FlyerValidFromInput.Text)
                                        && string.IsNullOrWhiteSpace(FlyerValidToInput.Text))
                                    {
                                        FlyerValidFromInput.Text = dates.From;
                                        FlyerValidToInput.Text = dates.To;
                                    }
                                }
                                catch (SqliteException) { /* The operator can enter the printed dates directly. */ }
                        }
                    }
                }
                _ = RefreshCatalogueMatchesAsync(products);
                processing.Success($"Extracted {products.Count} products; review and Save the draft.");
            }
            catch(OperationCanceledException) { processing.Cancel("Extraction cancelled. Previous products preserved; provider processing may already have occurred."); }
            catch(Exception ex) { processing.Fail(ex.Message); }
            finally { if(!_windowClosed) GetDataButton.Content = "Get Data"; }
        }

        private async Task RefreshCatalogueMatchesAsync(IReadOnlyList<ExtractedProduct> products)
        {
            if (products.Count == 0) return;
            foreach (var product in products)
            {
                product.CanCheckCatalogue = false;
                product.InvalidateOfferMatches();
                product.CatalogueCandidates = [];
                product.CatalogueStatus = "Checking Azure SQL catalogue…";
            }

            try
            {
                var catalogue = await _readCatalogue(_windowLifetime.Token);
                if (_windowClosed) return;
                foreach (var product in products.Where(_products.Contains))
                {
                    var candidates = ProductIdentity.FindCandidates(product.ToCatalogueProduct(), catalogue);
                    product.CatalogueCandidates = candidates;
                    product.CatalogueStatus = candidates.Count == 0
                        ? "No catalogue candidates found."
                        : $"{candidates.Count} catalogue candidate(s). Select a result to inspect its details.";
                }
            }
            catch (Exception) when (_windowClosed) { }
            catch (Exception)
            {
                foreach (var product in products.Where(_products.Contains))
                    product.CatalogueStatus = "Catalogue lookup unavailable. You can continue editing and Save.";
            }
            finally
            {
                foreach (var product in products)
                    product.CanCheckCatalogue = true;
            }

            if (ReviewerOffer.TryDates(FlyerValidFromInput.Text, FlyerValidToInput.Text, out _, out _))
                foreach (var product in products.Where(_products.Contains).ToArray())
                    await RefreshOfferMatchesAsync(product);
        }

        private static Task<List<Product>> ReadCatalogueAsync(CancellationToken token)
        {
            var connection = Environment.GetEnvironmentVariable("FLYERMONKEY_SQL_CONNECTION");
            if (string.IsNullOrWhiteSpace(connection))
                throw new InvalidOperationException("The catalogue connection is missing.");

            return ReviewerSqlBatch.ReadCatalogueAsync(connection, token);
        }

        private async void CheckCatalogue_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { DataContext: ExtractedProduct product })
                await RefreshCatalogueMatchesAsync([product]);
        }

        private void CatalogueDecision_Click(object sender, RoutedEventArgs e)
        {
            if (_processing || !CanEditCurrentExtraction || sender is not Button { DataContext: ExtractedProduct product } button)
                return;
            if (button.Tag is ComboBox { SelectedItem: CatalogueCandidate { CanReuse: true } candidate }
                && product.CatalogueCandidates.Contains(candidate))
            {
                product.CatalogueReview = CatalogueReviewDecision.For(product, ProductReviewChoice.ReuseExisting, candidate);
                _ = RefreshOfferMatchesAsync(product, candidate);
            }
            else if (button.Tag is ProductReviewChoice choice)
                product.CatalogueReview = CatalogueReviewDecision.For(product, choice);
        }

        private void ShowFlyerDates(IEnumerable<ExtractedProduct> products)
        {
            var ranges = products.Select(product => (product.FlyerValidFrom, product.FlyerValidTo))
                .Where(range => ReviewerOffer.TryDates(range.FlyerValidFrom, range.FlyerValidTo, out _, out _))
                .Distinct().Take(2).ToArray();
            FlyerValidFromInput.Text = ranges.Length == 1 ? ranges[0].FlyerValidFrom : "";
            FlyerValidToInput.Text = ranges.Length == 1 ? ranges[0].FlyerValidTo : "";
        }

        private static (string From, string To)? ReadCoverDates(string path)
        {
            if (!File.Exists(path)) return null;
            try
            {
                using var cover = new PdfLoadedDocument(path);
                return ReviewerOffer.PrintedDates(cover.Pages[0].ExtractText());
            }
            catch (Exception) { return null; }
        }

        private void StoreFlyerDates(IEnumerable<ExtractedProduct> products)
        {
            foreach (var product in products)
            {
                product.FlyerValidFrom = FlyerValidFromInput.Text.Trim();
                product.FlyerValidTo = FlyerValidToInput.Text.Trim();
            }
        }

        private void FlyerDatesChanged(object sender, TextChangedEventArgs e)
        {
            foreach (var product in _products)
                product.InvalidateOfferMatches();
        }

        private async void CheckOffers_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { DataContext: ExtractedProduct card } button) return;
            var selected = (button.Tag as ComboBox)?.SelectedItem as CatalogueCandidate;
            await RefreshOfferMatchesAsync(card, selected, showPrerequisites: true);
        }

        private async Task RefreshOfferMatchesAsync(ExtractedProduct card, CatalogueCandidate? selected = null,
            bool showPrerequisites = false)
        {
            if (_windowClosed || !_products.Contains(card) || !card.CanCheckOffers) return;
            card.SimilarOffers = [];
            if (!ReviewerOffer.TryDates(FlyerValidFromInput.Text, FlyerValidToInput.Text, out var start, out var end))
            {
                if (showPrerequisites) card.OfferMatchStatus = "Enter the printed flyer dates in Page Review first.";
                return;
            }
            var decision = card.CatalogueReview;
            var duplicates = card.CatalogueCandidates.Where(candidate => candidate.CanReuse).Take(2).ToArray();
            var productId = selected is { CanReuse: true } ? selected.ProductID
                : decision is { Choice: ProductReviewChoice.ReuseExisting } && decision.AppliesTo(card)
                    ? decision.Target?.ProductID
                    : (decision is null or { Choice: ProductReviewChoice.Automatic })
                        && duplicates is [{ Relationship: ProductRelationship.LikelyDuplicate } likely] ? likely.ProductID : null;
            var draft = SavedExtractionList.SelectedItem as SavedExtraction;
            var retailer = draft?.Retailer ?? _selectedFlyer?.Retailer;
            var edition = draft?.FlyerFileName ?? _selectedFlyer?.FileName;
            if (productId is null || string.IsNullOrWhiteSpace(retailer) || string.IsNullOrWhiteSpace(edition))
            {
                if (showPrerequisites) card.OfferMatchStatus = "Select a reusable product candidate in Catalogue matches first.";
                return;
            }

            card.CanCheckOffers = false;
            var previewVersion = card.OfferPreviewVersion;
            card.OfferMatchStatus = $"Checking offers for ProductID {productId}…";
            try
            {
                var (retailerId, location, offers) = await _readOffers(productId.Value, retailer, _windowLifetime.Token);
                if (_windowClosed || !_products.Contains(card) || card.OfferPreviewVersion != previewVersion) return;
                var proposed = ReviewerOffer.Create(card, retailerId, location, edition,
                    draft?.PageFileName ?? _selectedPage?.FileName ?? "", draft?.PageNumber ?? _selectedPage?.PageNumber ?? 0, start, end);
                proposed.ProductID = productId.Value;
                card.SimilarOffers = OfferIdentity.Candidates(proposed, offers);
                card.OfferMatchStatus = card.SimilarOffers.Count == 0 ? $"No similar offers for ProductID {productId}."
                    : $"{card.SimilarOffers.Count} similar offer(s) for ProductID {productId}. Select an offer to compare.";
            }
            catch (Exception) when (_windowClosed) { }
            catch (Exception)
            {
                if (card.OfferPreviewVersion == previewVersion)
                    card.OfferMatchStatus = "Offer lookup unavailable. The draft can still be saved.";
            }
            finally { card.CanCheckOffers = true; }
        }

        private static async Task<(int RetailerId, int? Location, List<Offer> Offers)> ReadOffersForReviewAsync(
            int productId, string retailer, CancellationToken token)
        {
            var connection = Environment.GetEnvironmentVariable("FLYERMONKEY_SQL_CONNECTION")
                ?? throw new InvalidOperationException("Missing catalogue connection.");
            var retailerId = await new RetailerRepository(connection).GetRetailerIdByNameAsync(retailer)
                ?? throw new InvalidOperationException("Retailer was not found.");
            var location = await new RetailerLocationRepository(connection).GetLocationIdAsync(retailerId, "Butler");
            return (retailerId, location, await ReviewerSqlBatch.ReadOffersAsync(connection, productId, retailerId, token));
        }

        private void OfferDecision_Click(object sender, RoutedEventArgs e)
        {
            if (_processing || !CanEditCurrentExtraction || sender is not Button { DataContext: ExtractedProduct card } button) return;
            if (button.Tag is ComboBox { SelectedItem: OfferCandidate { CanReuse: true } candidate }
                && card.SimilarOffers.Contains(candidate))
            {
                card.ConfirmedOfferID = candidate.OfferID;
                card.OfferReview = OfferReviewChoice.ReuseExisting;
            }
            else if (button.Tag is OfferReviewChoice choice)
            {
                card.ConfirmedOfferID = null;
                card.OfferReview = choice;
            }
        }
        private async void SplitFlyerButton_Click(object sender, RoutedEventArgs e)
        {
            using var processing = BeginProcessing(false, "Splitting PDF pages");
            if (processing == null) return;
            if (_selectedFlyer == null)
                return;

            var result = ShowMessage(
                $"Split this flyer into individual pages?\n\n{_selectedFlyer.FileName}",
                "Split Flyer",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            bool persisted = false;
            try
            {
                var splitState = DiscoverFlyer(_selectedFlyer);
                if (splitState.Warning.Length > 0) throw new InvalidOperationException(splitState.Warning);
                var source = _selectedFlyer.FullPath;
                await Task.Run(() => SplitPdf(source));
                persisted = true; RefreshFlyerStatus(_selectedFlyer!); processing.Report("Split pages persisted; loading page previews...");
                await LoadSplitPagesAsync(_selectedFlyer);

                processing.Success("Split PDF pages persisted successfully.");
            }
            catch (Exception ex)
            {
                if (persisted) processing.Success("Split pages saved. Preview refresh issue: " + ex.Message); else processing.Fail(ex.Message);
            }
        }


        private async void SavedExtractionList_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (CommitButton == null)
                return;

            if (SavedExtractionList.SelectedItem
                is not SavedExtraction saved)
            {
                CommitButton.IsEnabled = false;
                SaveDataButton.IsEnabled = !_processing;
                return;
            }

            _restoringSavedSelection = true;
            try
            {
                if (_selectedPage?.ExtractionRunId != saved.Id) { PageList.SelectedItem = null; _selectedPage = null; }
            }
            finally { _restoringSavedSelection = false; }
            GetDataButton.Visibility = Visibility.Collapsed;
            AddOfferButton.IsEnabled = false;
            _products.Clear();
            EditingContextText.Text = saved.Status == "Saved" ? "Editing saved draft. Save updates this extraction only."
                : saved.Status == "Committed" ? "Committed: inspection only. SQL corrections are not supported."
                : "Recovery Required: inspection only. No automatic retry.";
            SelectedPageReview.LoadPage(_selectedPage?.FullPath ?? ResolveSavedPagePath(saved));
            // Do not allow committing cards from the previously selected batch.
            CommitButton.IsEnabled = false;
            SaveDataButton.IsEnabled = false;

            try
            {
                string sqlitePath =
                    _databasePath;

                var commitService = new SqlCommitService(sqlitePath);

                // Retrieve this exact extraction run
                var products = await commitService.LoadProductsAsync(saved);

                // Ignore results if the user selected another batch
                // while the SQLite query was running.
                if (SavedExtractionList.SelectedItem is not SavedExtraction current
                    || current.Id != saved.Id)
                    return;

                ShowFlyerDates(products);
                FlyerValidFromInput.IsReadOnly = FlyerValidToInput.IsReadOnly = saved.Status != "Saved";
                // Refresh the existing UI collection
                _products.Clear();

                foreach (var product in products)
                {
                    product.CanEditOffer = saved.Status == "Saved";
                    _products.Add(product);
                }
                CommitButton.IsEnabled = !_processing && saved.Status == "Saved";
                SaveDataButton.IsEnabled = !_processing && saved.Status == "Saved";
                AddOfferButton.IsEnabled = !_processing && saved.Status == "Saved";
                if (saved.Status == "Saved") _ = RefreshCatalogueMatchesAsync(products);
            }
            catch (Exception ex)
            {
                ShowMessage(
                    ex.Message,
                    "Could not restore extraction",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }


        private async void SaveDataButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            using var processing = BeginProcessing(false, "Saving SQLite draft");
            if (processing == null) return;
            var savedExtraction =
                SavedExtractionList.SelectedItem as SavedExtraction;

            if (savedExtraction != null && savedExtraction.Status != "Saved")
            {
                ShowMessage(savedExtraction.Status == "Committing" ? "Recovery Required. This batch cannot be saved or committed until its SQL outcome is reviewed." : "Committed extractions are inspection-only. SQL correction by ProductID/OfferID is not supported; no insertion or retry will be performed.");
                return;
            }

            bool editingSavedExtraction =
                savedExtraction != null &&
                savedExtraction.Status == "Saved";

            if (!editingSavedExtraction &&
                (_selectedFlyer == null || _selectedPage == null))
            {
                ShowMessage(
                    "Select a flyer and page, or open a saved extraction.",
                    "Save",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            if (ProductList.ItemsSource
                is not IEnumerable<ExtractedProduct> products)
            {
                ShowMessage(
                    "No product data is available to save.",
                    "Save",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            var productList = products.ToList();

            if (productList.Count == 0)
            {
                ShowMessage(
                    "There is no extracted data to save.");
                return;
            }

            StoreFlyerDates(productList);
            bool persisted = false;
            try
            {
                SaveDataButton.IsEnabled = false;
                SaveDataButton.Content = "Saving...";

                string databasePath =
                    _databasePath;

                var saver =
                    new ExtractionSaveService(databasePath);

                if (editingSavedExtraction)
                {
                    // Update the exact existing SQLite batch.
                    await saver.UpdateSavedExtractionAsync(
                        savedExtraction!.Id,
                        productList);

                    persisted = true; processing.Report("SQLite draft persisted; refreshing the Reviewer...");
                }
                else
                {
                    // Save a fresh extraction.
                    await saver.SaveAsync(
                        _selectedFlyer!,
                        _selectedPage!,
                        productList);
                    persisted = true; processing.Report("SQLite draft persisted; refreshing page previews...");

                    if (PageList.SelectedItem is FlyerPage page)
                    {
                        page.ExtractionStatus = "Saved";
                    }

                    await LoadSplitPagesAsync(_selectedFlyer!);

                    processing.Report("SQLite draft persisted; refreshing saved extractions...");
                }

                await LoadSavedExtractionsAsync();
                processing.Success($"Saved {productList.Count} products to SQLite.");
            }
            catch (Exception ex)
            {
                if (persisted) processing.Success("SQLite draft saved. Refresh issue: " + ex.Message); else processing.Fail(ex.Message);
            }
            finally
            {
                SaveDataButton.Content = "Save";
                SaveDataButton.IsEnabled = true;
            }
        }


        private async Task LoadSavedExtractionsAsync()
        {
            string databasePath =
                _databasePath;

            var reader =
                new ExtractionReadService(databasePath);

            var saved =
    await reader.GetAllAsync();

            foreach (var entry in saved) entry.IsCommitActive = entry.Id == _activeCommitId;
            var selectionId = (SavedExtractionList.SelectedItem as SavedExtraction)?.Id ?? _selectedPage?.ExtractionRunId;
            SavedExtractionList.ItemsSource = saved;
            if (selectionId.HasValue) SavedExtractionList.SelectedItem = saved.FirstOrDefault(x => x.Id == selectionId);
        }


        private const string DefaultDatabasePath = @"C:\Users\richa\source\repos\FlyerMonkey\DATA\FlyerMonkey.db";
        private readonly string _databasePath;
        private readonly string _splitRoot;
        private string? ResolveSavedPagePath(SavedExtraction saved)
        {
            if (string.IsNullOrWhiteSpace(saved.FlyerFileName) || string.IsNullOrWhiteSpace(saved.PageFileName)) return null;
            var flyer = new FlyerFile { Retailer = saved.Retailer, FileName = saved.FlyerFileName };
            return DiscoverFlyer(flyer).Pages.FirstOrDefault(x => Path.GetFileName(x).Equals(saved.PageFileName, StringComparison.OrdinalIgnoreCase));
        }
        private readonly Func<string, CancellationToken, Task<List<ExtractedProduct>>> _extractProducts;
        private readonly Func<CancellationToken, Task<List<Product>>> _readCatalogue;
        private readonly Func<int, string, CancellationToken, Task<(int RetailerId, int? Location, List<Offer> Offers)>> _readOffers;

        public MainWindow() : this(DefaultDatabasePath) { }

        // Explicit dependencies let validation exercise the real handlers without operator data or paid calls.
        public MainWindow(string databasePath,
            Func<string, CancellationToken, Task<List<ExtractedProduct>>>? extractProducts = null,
            bool loadFlyers = true, string? splitRoot = null,
            Func<string, CancellationToken, Task<byte[]>>? generateClassic = null, string? imageFolder = null,
            Func<CancellationToken, Task<List<Product>>>? readCatalogue = null, string? themeSettingsPath = null,
            Func<int, string, CancellationToken, Task<(int RetailerId, int? Location, List<Offer> Offers)>>? readOffers = null)
        {
            _databasePath = databasePath;
            _generateClassic = generateClassic ?? ((prompt,token) => new ClassicImageGenerationService().GenerateImageAsync(prompt,token));
            _imageFolder = imageFolder ?? @"C:\Users\richa\source\repos\FlyerMonkey\DATA\ProductImages";
            _splitRoot = splitRoot ?? @"C:\Users\richa\source\repos\FlyerMonkey\DATA\Flyers\Split";
            _extractProducts = extractProducts ?? ((path, token) => new ProductExtractorService().ExtractProductsAsync(path, token));
            _readCatalogue = readCatalogue ?? ReadCatalogueAsync;
            _readOffers = readOffers ?? ReadOffersForReviewAsync;
            _themeSettingsPath = themeSettingsPath ?? ReviewerTheme.SettingsPath;
            _theme = ReviewerTheme.Load(_themeSettingsPath);
            _artworkArchive = new ArtworkArchive(_imageFolder, generateClassic == null ? "OpenAI / gpt-image-1.5" : "Injected provider");
            InitializeComponent();
            ReviewerTheme.Apply(this, _theme);
            (_theme switch
            {
                ReviewerThemeChoice.Dark => DarkThemeButton,
                ReviewerThemeChoice.Light => LightThemeButton,
                _ => MediumThemeButton
            }).IsChecked = true;
            StatusSpace.DataContext = _progress;
            _progressTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _progressTimer.Tick += (_,_) => _progress.RefreshElapsed(); _progressTimer.Start();
            SizeChanged += (_,_) => RefreshResponsiveLayout();
            Loaded += (_,_) => ReviewerWindowLayout.ConstrainToCurrentMonitor(this, VisualTreeHelper.GetDpi(this));
            StateChanged += (_,_) => {
                if (WindowState == WindowState.Normal)
                    Dispatcher.BeginInvoke(new Action(() => {
                        if (!_windowClosed) ReviewerWindowLayout.ConstrainToCurrentMonitor(this, VisualTreeHelper.GetDpi(this));
                    }));
            };
            ProductList.ItemsSource = _products;
            _products.CollectionChanged += (_,_) => {
                foreach (var job in _craterJobs.ToArray())
                    if (!_products.Contains(job.Key)) job.Value.Cancel();
            };
            if (loadFlyers) LoadFlyers();

            _ = LoadSavedExtractionsAsync();
        }
        private FlyerSplitState DiscoverFlyer(FlyerFile flyer) => FlyerStatusService.Discover(_splitRoot, flyer, FlyerList.Items.OfType<FlyerFile>());
        private void RefreshFlyerStatus(FlyerFile flyer) => flyer.SetStatus(new FlyerStatusService(_databasePath).IsComplete(flyer), DiscoverFlyer(flyer));
        private bool HasPotentialUnsavedOffers => CanEditCurrentExtraction && (_products.Count > 0 || _selectedPage != null || SavedExtractionList.SelectedItem is SavedExtraction);
        private void FlyerCompletion_Click(object sender, RoutedEventArgs e)
        {
            using var processing = BeginProcessing(false, "Updating flyer status");
            if (processing == null || _selectedFlyer is not { } flyer) return;
            if (!flyer.IsComplete && HasPotentialUnsavedOffers
                && ShowMessage("There may be unsaved edits in the current review. Mark Complete does not save offers or commit them. Continue marking this flyer complete?", "Unsaved edits", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            try
            {
                var split = DiscoverFlyer(flyer);
                if (split.Warning.StartsWith("Ambiguous", StringComparison.Ordinal)) throw new InvalidOperationException(split.Warning);
                new FlyerStatusService(_databasePath).SetComplete(flyer, !flyer.IsComplete);
                RefreshFlyerStatus(flyer);
                FlyerCompletionButton.Content = flyer.IsComplete ? "Reopen flyer" : "Mark Complete";
                processing.Success(flyer.IsComplete ? "Flyer marked complete." : "Flyer reopened.");
            }
            catch (Exception ex) { processing.Fail(ex.Message); }
        }
        private void RefreshFlyers_Click(object sender, RoutedEventArgs e)
        {
            LoadFlyers();
        }
        private void LoadFlyers() =>
            LoadFlyersFrom(@"C:\Users\richa\source\repos\FlyerMonkey\DATA\Flyers\Incoming");

        private void LoadFlyersFrom(string incomingFolder)
        {
            if (!Directory.Exists(incomingFolder))
            {
                ShowMessage($"Incoming folder not found:\n{incomingFolder}");
                return;
            }

            var flyers = Directory.EnumerateFiles(incomingFolder, "*.pdf", SearchOption.AllDirectories)
                .Select(path => (Flyer: CreateFlyerFromFile(path), Modified: File.GetLastWriteTime(path)))
                .OrderByDescending(item => item.Flyer.PublicationDateForSort ?? DateOnly.FromDateTime(item.Modified))
                .ThenByDescending(item => item.Modified)
                .ThenBy(item => item.Flyer.FileName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Flyer.FullPath, StringComparer.Ordinal)
                .Select(item => item.Flyer).ToArray();

            var previous = FlyerList.SelectedItem as FlyerFile;
            var selected = flyers.FirstOrDefault(flyer =>
                string.Equals(flyer.FullPath, previous?.FullPath, StringComparison.OrdinalIgnoreCase));
            _refreshingFlyerList = selected is not null;
            try
            {
                FlyerList.Items.Clear();
                foreach (var flyer in flyers)
                    FlyerList.Items.Add(ReferenceEquals(flyer, selected) ? previous! : flyer);
                if (selected is not null) FlyerList.SelectedItem = previous;
            }
            finally { _refreshingFlyerList = false; }
            foreach (var flyer in FlyerList.Items.OfType<FlyerFile>()) RefreshFlyerStatus(flyer);
        }

        private FlyerFile CreateFlyerFromFile(string pdfPath)
        {
            string fileName = Path.GetFileName(pdfPath);
            string nameWithoutExtension = Path.GetFileNameWithoutExtension(pdfPath);

            // Parent folder now tells us the retailer.
            string retailerFolder =
                Directory.GetParent(pdfPath)?.Name ?? "Unknown";

            var flyer = new FlyerFile
            {
                Retailer = retailerFolder switch
                {
                    "Woolworths" => "Woolworths",
                    "Coles" => "Coles",
                    _ => retailerFolder
                },
                FlyerDate = "Date unknown",
                PageDescription = "Pages unknown",
                FileName = fileName,
                FullPath = pdfPath
            };
            string[] parts = nameWithoutExtension.Split('_');

            // Current retailer filenames both contain ddMMyy.
            // Woolworths: WW_WA_160926_6R9PARNL4
            // Coles:      COLWAMETRO_160926_WCNU2SU7
            string? datePart = parts
                .FirstOrDefault(p =>
                    p.Length == 6 &&
                    p.All(char.IsDigit));

            if (datePart != null &&
                DateTime.TryParseExact(
                    datePart,
                    "ddMMyy",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out DateTime flyerDate))
            {
                flyer.FlyerDate = flyerDate.ToString("dd MMM yyyy");
                var knownName = (retailerFolder.Equals("Coles", StringComparison.OrdinalIgnoreCase)
                    && nameWithoutExtension.StartsWith("COLWAMETRO_", StringComparison.OrdinalIgnoreCase))
                    || (retailerFolder.Equals("Woolworths", StringComparison.OrdinalIgnoreCase)
                    && nameWithoutExtension.StartsWith("WW_", StringComparison.OrdinalIgnoreCase));
                if (knownName) flyer.PublicationDateForSort = DateOnly.FromDateTime(flyerDate);
            }

            try
            {
                using var document = new PdfLoadedDocument(pdfPath);
                var pageCount = document.Pages.Count;
                flyer.PageDescription = pageCount == 1 ? "1 page" : $"{pageCount} pages";
                if (flyer.PublicationDateForSort is null && pageCount > 0
                    && ReviewerOffer.PrintedDates(document.Pages[0].ExtractText()) is { } printed
                    && ReviewerOffer.TryDates(printed.From, printed.To, out var start, out _))
                    flyer.PublicationDateForSort = start;
            }
            catch
            {
                // A missing or unreadable cover leaves file time as the sort fallback.
            }

            return flyer;
        }

    }
}
