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
using System.Text.Json;

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

    public class FlyerFile
    {
        public string Retailer { get; set; } = "";
        public string FlyerDate { get; set; } = "";
        public string PageDescription { get; set; } = "";
        public string FileName { get; set; } = "";
        public string FullPath { get; set; } = "";
    }
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly CancellationTokenSource _classicGenerationLifetime = new();

        protected override void OnClosed(EventArgs e)
        {
            _classicGenerationLifetime.Cancel();
            _classicGenerationLifetime.Dispose();
            base.OnClosed(e);
        }
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
        private async void FlyerList_SelectionChanged(
    object sender,
    SelectionChangedEventArgs e)
        {
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
                MessageBox.Show(
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

            var result = MessageBox.Show(
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

            await LoadSplitPagesAsync(flyer);
        }

        private void PageList_SelectionChanged(
    object sender,
    SelectionChangedEventArgs e)
        {
            if (PageList.SelectedItem is not FlyerPage page)
            {
                _selectedPage = null;
                return;
            }

            _selectedPage = page;

            SelectedPageWebView.Visibility = Visibility.Visible;
            SelectedPageWebView.Source = new Uri(page.FullPath);

            GetDataButton.Visibility = Visibility.Visible;
        }
        private async Task LoadSplitPagesAsync(FlyerFile flyer)
        {
            // Remember the currently selected page.
            string? selectedPageFileName =
                _selectedPage?.FileName;

            PageList.Items.Clear();

            string splitRoot =
                @"C:\Users\richa\source\repos\FlyerMonkey\DATA\Flyers\Split";

            string flyerFolder = Path.Combine(
                splitRoot,
                Path.GetFileNameWithoutExtension(flyer.FileName));

            if (!Directory.Exists(flyerFolder))
                return;

            var pageFiles = Directory
                .GetFiles(flyerFolder, "page-*.pdf")
                .OrderBy(x => x)
                .ToList();

            if (pageFiles.Count > 0)
            {
                string sqlitePath =
                    @"C:\Users\richa\source\repos\FlyerMonkey\DATA\FlyerMonkey.db";

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
                        Thumbnail = CreateThumbnail(pageFile)
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

            string splitRoot =
                @"C:\Users\richa\source\repos\FlyerMonkey\DATA\Flyers\Split";

            string outputFolder =
                Path.Combine(splitRoot, fileNameWithoutExtension);

            Directory.CreateDirectory(outputFolder);

            using var pdfStream = File.OpenRead(sourcePdf);

            var splitter = new PdfSplitService();
            var pages = splitter.SplitPdf(pdfStream);

            for (int i = 0; i < pages.Count; i++)
            {
                string outputPath = Path.Combine(
                    outputFolder,
                    $"page-{i + 1:000}.pdf");

                File.WriteAllBytes(outputPath, pages[i]);
            }
        }

        private void AddOfferButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
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
            if (sender is not Button button ||
                button.Tag is not ExtractedProduct product)
                return;

            var result = MessageBox.Show(
                $"Delete '{product.ProductName}'?",
                "Delete offer",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            if (_selectedPage?.ExtractionRunId == null)
            {
                MessageBox.Show(
                    "This extraction has not been saved yet.",
                    "Delete offer",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            _products.Remove(product);

            string databasePath =
                @"C:\Users\richa\source\repos\FlyerMonkey\DATA\FlyerMonkey.db";

            var saver =
                new ExtractionSaveService(databasePath);

            await saver.UpdateProductsAsync(
                (int)_selectedPage.ExtractionRunId.Value,
                _products);

            await LoadSavedExtractionsAsync();
        }

        private void EditableField_Click(
    object sender,
    MouseButtonEventArgs e)
        {
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

            editor.Visibility = Visibility.Collapsed;
            display.Visibility = Visibility.Visible;
        }
        private static decimal? ParseMoney(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var cleaned = value
                .Replace("$", "")
                .Replace(",", "")
                .Replace("SAVE", "", StringComparison.OrdinalIgnoreCase)
                .Trim();

            return decimal.TryParse(
                cleaned,
                System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture,
                out var amount)
                    ? amount
                    : null;
        }


        private async void CommitButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (SavedExtractionList.SelectedItem is not SavedExtraction saved)
            {
                MessageBox.Show(
                    "Select a locally saved extraction first.",
                    "Commit",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            // Prevent an already committed batch being committed again
            if (saved.Status != "Saved")
            {
                MessageBox.Show(
                    "This extraction has already been committed.",
                    "Commit",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            var confirm = MessageBox.Show(
                $"Commit {saved.ProductCount} products from {saved.PageFileName} to SQL?",
                "Commit to SQL",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
                return;

            // Existing try block continues here...


            try
            {
                string sqlitePath =
                    @"C:\Users\richa\source\repos\FlyerMonkey\DATA\FlyerMonkey.db";

                var commitService =
                    new SqlCommitService(sqlitePath);

                var products =
                    await commitService.LoadProductsAsync(saved);

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

                var repository =
                    new ProductRepository(sqlConnectionString);

                var offerRepository =
    new OfferRepository(sqlConnectionString);

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

                var addedCount = 0;

                foreach (var extractedProduct in products)
                {
                    string? imageBlobPath = null;

                    if (!string.IsNullOrWhiteSpace(
                            extractedProduct.ImageFilePath))
                    {
                        imageBlobPath =
                            await imageUploadService.UploadAsync(
                                extractedProduct.ImageFilePath);
                    }

                    var product = new Product
                    {
                        Name = extractedProduct.ProductName,
                        Brand = extractedProduct.Brand,
                        Variant = extractedProduct.Variant,
                        PackSizeText = extractedProduct.PackSizeText,
                        Category = extractedProduct.Category,
                        Barcode = extractedProduct.Barcode,
                        ImageBlobPath = imageBlobPath
                    };

                    var productId =
    await repository.AddProductAsync(product);

                    await repository.SaveProductCategoriesAsync(
                        productId,
                        extractedProduct.PrimaryCategory
                            ?? extractedProduct.Category,
                        extractedProduct.Categories);

                    var offer = new Offer
                    {
                        ProductID = productId,
                        RetailerID = retailerId.Value,
                        RetailerLocationID = retailerLocationId,

                        AdvertisedPrice = ParseMoney(extractedProduct.Price),
                        RegularPrice = ParseMoney(extractedProduct.RegularPrice),
                        AdvertisedSaving = ParseMoney(extractedProduct.Saving),

                        ValidFrom = DateTime.UtcNow,
                        ValidTo = DateTime.UtcNow.AddDays(7),

                        SourceType = "Flyer",
                        SourceDescription = saved.PageFileName,
                        FlyerPageNumber = saved.PageNumber,

                        PromoText = extractedProduct.Promotion,

                        PromotionTypes = JsonSerializer.Serialize(
    extractedProduct.PromotionTypes ?? new List<string>()),

                        UnitPriceText = extractedProduct.UnitPrice
                    };

                    await offerRepository.AddOfferAsync(offer);

                    addedCount++;
                }
                await commitService.MarkCommittedAsync(saved.Id);

                if (_selectedFlyer is not null)
                {
                    await LoadSplitPagesAsync(_selectedFlyer);
                }

                await LoadSavedExtractionsAsync();

                MessageBox.Show(
                    $"SQL write succeeded.\n\nProducts added: {addedCount}",
                    "Commit complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "SQL commit failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async void CraterItButton_Click(
    object sender,
    RoutedEventArgs e)
        {
            if (sender is not Button button ||
                button.Tag is not ExtractedProduct product)
            {
                return;
            }

            var imageService = new ProductImageService();
            var prompt = imageService.BuildImagePrompt(product);

            try
            {
                button.IsEnabled = false;
                button.Content = "☄ Cratering...";

                var generationService = new ClassicImageGenerationService();
                var imageBytes =
                    await generationService.GenerateImageAsync(prompt, _classicGenerationLifetime.Token);

                var imageFolder =
    @"C:\Users\richa\source\repos\FlyerMonkey\DATA\ProductImages";

                var imagePath =
                    await ClassicImageFileService.SaveAsync(imageFolder, imageBytes);

                product.ImageFilePath = imagePath;
            }
            catch (OperationCanceledException) when (_classicGenerationLifetime.IsCancellationRequested)
            {
                // Closing the Reviewer cancels the request without opening another dialog.
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "Classic image operation failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                button.Content = "☄ Crater It!";
                button.IsEnabled = true;
            }
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

            previewWindow.Show();
        }
        private async void GetDataButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_selectedPage == null)
                return;
            string databasePath =
    @"C:\Users\richa\source\repos\FlyerMonkey\DATA\FlyerMonkey.db";

            var reader =
                new ExtractionReadService(databasePath);

            var savedProducts =
                await reader.GetProductsAsync(
                    _selectedFlyer!.FileName,
                    _selectedPage.FileName);

            if (savedProducts is { Count: > 0 })
            {
                _products.Clear();

                foreach (var product in savedProducts)
                {
                    _products.Add(product);
                }

                return;
            }
            try
            {
                GetDataButton.IsEnabled = false;
                GetDataButton.Content = "Getting Data...";

                var extractor = new ProductExtractorService();

                var products =
    await extractor.ExtractProductsAsync(
        _selectedPage.FullPath);

                _products.Clear();

                foreach (var product in products)
                {
                    ProductValidationService.SanitizeProduct(product);
                    _products.Add(product);
                }
                
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "Get Data failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                GetDataButton.Content = "Get Data";
                GetDataButton.IsEnabled = true;
            }
        }
        private async void SplitFlyerButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedFlyer == null)
                return;

            var result = MessageBox.Show(
                $"Split this flyer into individual pages?\n\n{_selectedFlyer.FileName}",
                "Split Flyer",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                SplitPdf(_selectedFlyer.FullPath);
                await LoadSplitPagesAsync(_selectedFlyer);

                MessageBox.Show(
                    "Flyer split successfully.",
                    "Split Flyer",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Split failed:\n\n{ex.Message}",
                    "Split Flyer",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
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
                return;
            }

            // Only locally saved batches can be committed
            CommitButton.IsEnabled = saved.Status == "Saved";

            try
            {
                string sqlitePath =
                    @"C:\Users\richa\source\repos\FlyerMonkey\DATA\FlyerMonkey.db";

                var commitService = new SqlCommitService(sqlitePath);

                // Retrieve this exact extraction run
                var products = await commitService.LoadProductsAsync(saved);

                // Ignore results if the user selected another batch
                // while the SQLite query was running.
                if (SavedExtractionList.SelectedItem is not SavedExtraction current
                    || current.Id != saved.Id)
                    return;

                // Refresh the existing UI collection
                _products.Clear();

                foreach (var product in products)
                {
                    _products.Add(product);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
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
            var savedExtraction =
                SavedExtractionList.SelectedItem as SavedExtraction;

            bool editingSavedExtraction =
                savedExtraction != null &&
                savedExtraction.Status == "Saved";

            if (!editingSavedExtraction &&
                (_selectedFlyer == null || _selectedPage == null))
            {
                MessageBox.Show(
                    "Select a flyer and page, or open a saved extraction.",
                    "Save",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            if (ProductList.ItemsSource
                is not IEnumerable<ExtractedProduct> products)
            {
                MessageBox.Show(
                    "No product data is available to save.",
                    "Save",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            var productList = products.ToList();

            if (productList.Count == 0)
            {
                MessageBox.Show(
                    "There is no extracted data to save.");
                return;
            }

            try
            {
                SaveDataButton.IsEnabled = false;
                SaveDataButton.Content = "Saving...";

                string databasePath =
                    @"C:\Users\richa\source\repos\FlyerMonkey\DATA\FlyerMonkey.db";

                var saver =
                    new ExtractionSaveService(databasePath);

                if (editingSavedExtraction)
                {
                    // Update the exact existing SQLite batch.
                    await saver.UpdateSavedExtractionAsync(
                        savedExtraction!.Id,
                        productList);

                    MessageBox.Show(
                        $"Updated {productList.Count} products in " +
                        $"{savedExtraction.PageFileName}.",
                        "Save complete",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    // Save a fresh extraction.
                    await saver.SaveAsync(
                        _selectedFlyer!,
                        _selectedPage!,
                        productList);

                    if (PageList.SelectedItem is FlyerPage page)
                    {
                        page.ExtractionStatus = "Saved";
                    }

                    await LoadSplitPagesAsync(_selectedFlyer!);

                    MessageBox.Show(
                        $"Saved {productList.Count} products from " +
                        $"{_selectedPage?.FileName}.",
                        "Save complete",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                await LoadSavedExtractionsAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "Save failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
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
                @"C:\Users\richa\source\repos\FlyerMonkey\DATA\FlyerMonkey.db";

            var reader =
                new ExtractionReadService(databasePath);

            var saved =
    await reader.GetAllAsync();

            SavedExtractionList.ItemsSource = saved;
        }


        public MainWindow()
        {
            InitializeComponent();
            ProductList.ItemsSource = _products;
            LoadFlyers();

            _ = LoadSavedExtractionsAsync();
        }
        private void RefreshFlyers_Click(object sender, RoutedEventArgs e)
        {
            LoadFlyers();
        }
        private void LoadFlyers()
        {
            FlyerList.Items.Clear(); 
            
            string incomingFolder =
                @"C:\Users\richa\source\repos\FlyerMonkey\DATA\Flyers\Incoming";

            if (!Directory.Exists(incomingFolder))
            {
                MessageBox.Show($"Incoming folder not found:\n{incomingFolder}");
                return;
            }

            var pdfFiles = Directory.GetFiles(
    incomingFolder,
    "*.pdf",
    SearchOption.AllDirectories);

            foreach (var pdfPath in pdfFiles.OrderByDescending(x => x))
            {
                var flyer = CreateFlyerFromFile(pdfPath);

                if (flyer != null)
                {
                    FlyerList.Items.Add(flyer);
                }
            }
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
            try
            {
                using var document = new PdfLoadedDocument(pdfPath);

                int pageCount = document.Pages.Count;

                flyer.PageDescription =
                    pageCount == 1
                        ? "1 page"
                        : $"{pageCount} pages";
            }
            catch
            {
                flyer.PageDescription = "Pages unknown";
            }
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
            }

            return flyer;
        }

    }
}
