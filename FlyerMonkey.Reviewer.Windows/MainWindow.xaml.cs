using FlyerMonkey.Reviewer.Windows.Models;
using FlyerMonkey.Reviewer.Windows.Services;
using FlyerMonkey.Shared.Model;
using PDFtoImage;
using SQLServerConnection.Data;
using Syncfusion.Pdf.Parsing;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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

        private async void FlyerList_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (FlyerList.SelectedItem is not FlyerFile flyer)
                return;

            _selectedFlyer = flyer;
            await LoadSplitPagesAsync(flyer);
        }

        private void PageList_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (PageList.SelectedItem is not FlyerPage page)
                return;

            _selectedPage = page;

            SelectedPageImage.Source = page.Thumbnail;
            SelectedPageImage.Visibility = Visibility.Visible;

            GetDataButton.Visibility = Visibility.Visible;
        }
        private async Task LoadSplitPagesAsync(FlyerFile flyer)
        {
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

                for (int i = 0; i < pageFiles.Count; i++)
                {
                    var page = new FlyerPage
                    {
                        PageNumber = i + 1,
                        FileName = Path.GetFileName(pageFiles[i]),
                        FullPath = pageFiles[i],
                        Thumbnail = CreateThumbnail(pageFiles[i])
                    };
                    var extractionState = extractionStates.FirstOrDefault(x =>
    x.FlyerFileName == flyer.FileName &&
    x.PageFileName == page.FileName);

                    if (extractionState != null)
                    {
                        page.ExtractionStatus = extractionState.Status;
                        page.ExtractionRunId = extractionState.Id;
                    }
                    PageList.Items.Add(page);
                }

                // Automatically select page 1.
                PageList.SelectedIndex = 0;
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

            var confirm = MessageBox.Show(
                $"Commit {saved.ProductCount} products from {saved.PageFileName} to SQL?",
                "Commit to SQL",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
                return;

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
                    var product = new Product
                    {
                        Name = extractedProduct.ProductName,
                        Brand = extractedProduct.Brand,
                        Variant = extractedProduct.Variant,
                        PackSizeText = extractedProduct.PackSizeText,
                        Category = extractedProduct.Category,
                        Barcode = extractedProduct.Barcode
                    };

                    var productId =
    await repository.AddProductAsync(product);

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
                        UnitPriceText = extractedProduct.UnitPrice
                    };

                    await offerRepository.AddOfferAsync(offer);

                    addedCount++;
                }
                await commitService.MarkCommittedAsync(saved.Id);

                await LoadSplitPagesAsync(_selectedFlyer);
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

            var apiService = new ImageGenerationApiService();

            try
            {
                button.IsEnabled = false;
                button.Content = "☄ Cratering...";

                var imageBytes =
                    await apiService.GenerateImageAsync(prompt);

                var imageFolder =
    @"C:\Users\richa\source\repos\FlyerMonkey\DATA\ProductImages";

                Directory.CreateDirectory(imageFolder);

                var fileName =
                    $"{Guid.NewGuid():N}.jpg";

                var imagePath =
                    Path.Combine(imageFolder, fileName);

                await File.WriteAllBytesAsync(
                    imagePath,
                    imageBytes);

                product.ImageFilePath = imagePath;

                GeneratedProductImage.Source = LoadImage(imageBytes);
                GeneratedProductImage.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "Image generation failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                button.Content = "☄ Crater It!";
                button.IsEnabled = true;
            }
        }
        private static BitmapImage LoadImage(byte[] imageBytes)
        {
            using var stream = new MemoryStream(imageBytes);

            var image = new BitmapImage();

            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();

            return image;
        }
        private async void GetDataButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_selectedPage == null)
                return;

            try
            {
                GetDataButton.IsEnabled = false;
                GetDataButton.Content = "Getting Data...";

                var extractor = new ProductExtractorService();

                var products =
                    await extractor.ExtractProductsAsync(
                        _selectedPage.FullPath);
                var productList = products.ToList();

                foreach (var product in productList)
                {
                    ProductValidationService.SanitizeProduct(product);
                }
                ProductList.ItemsSource = products;
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
        private async void SaveDataButton_Click(
    object sender,
    RoutedEventArgs e)
        {
            if (_selectedFlyer == null ||
                _selectedPage == null)
            {
                return;
            }

            if (ProductList.ItemsSource
                is not IEnumerable<ExtractedProduct> products)
            {
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

                await saver.SaveAsync(
                _selectedFlyer,
                _selectedPage,
                productList);

                MessageBox.Show(
                    $"Saved {productList.Count} products from {_selectedPage.FileName}.",
                    "Save complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                if (PageList.SelectedItem is FlyerPage page)
                {
                    page.ExtractionStatus = "Saved";
                }
                await LoadSplitPagesAsync(_selectedFlyer);
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
                await reader.GetSavedAsync();

            SavedExtractionList.ItemsSource = saved;
        }
        public MainWindow()
        {
            InitializeComponent();

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