using FlyerMonkey.Reviewer.Windows.Services;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PDFtoImage;
using System.Windows.Media.Imaging;
using FlyerMonkey.Reviewer.Windows.Models;
using FlyerMonkey.Shared.Model;
using SQLServerConnection.Data;

namespace FlyerMonkey.Reviewer.Windows
{

    public class FlyerPage
    {
        public int PageNumber { get; set; }
        public string FileName { get; set; } = "";
        public string FullPath { get; set; } = "";

        public string DisplayName => $"Page {PageNumber}";

        public ImageSource? Thumbnail { get; set; }
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

        private void FlyerList_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (FlyerList.SelectedItem is not FlyerFile flyer)
                return;

            _selectedFlyer = flyer;

            //SelectedRetailer.Text = flyer.Retailer;
            //SelectedDate.Text = flyer.FlyerDate;
            //SelectedPages.Text = flyer.PageDescription;
            //SelectedFileName.Text = flyer.FileName;

            //NoFlyerSelectedText.Visibility = Visibility.Collapsed;
            //FlyerFocusPanel.Visibility = Visibility.Visible;
            LoadSplitPages(flyer);
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
        private void LoadSplitPages(FlyerFile flyer)
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

            for (int i = 0; i < pageFiles.Count; i++)
            {
                PageList.Items.Add(new FlyerPage
                {
                    PageNumber = i + 1,
                    FileName = Path.GetFileName(pageFiles[i]),
                    FullPath = pageFiles[i],
                    Thumbnail = CreateThumbnail(pageFiles[i])
                });
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

                var first = products.First();

                var product = new Product
                {
                    Name = first.ProductName
                };

                // Use your existing SQL connection string here

                var sqlConnectionString =
    Environment.GetEnvironmentVariable(
        "FLYERMONKEY_SQL_CONNECTION")
    ?? throw new InvalidOperationException(
        "FLYERMONKEY_SQL_CONNECTION is not set.");

                var repository =
                    new ProductRepository(sqlConnectionString);

                var id =
                    await repository.AddProductAsync(product);

                MessageBox.Show(
                    $"SQL write succeeded.\n\nID: {id}\nProduct: {product.Name}",
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
        private void SplitFlyerButton_Click(
    object sender,
    RoutedEventArgs e)
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
                LoadSplitPages(_selectedFlyer);

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

        private void LoadFlyers()
        {
            string incomingFolder =
                @"C:\Users\richa\source\repos\FlyerMonkey\DATA\Flyers\Incoming";

            if (!Directory.Exists(incomingFolder))
            {
                MessageBox.Show($"Incoming folder not found:\n{incomingFolder}");
                return;
            }

            var pdfFiles = Directory.GetFiles(incomingFolder, "*.pdf");

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

            // Defaults: every PDF gets displayed,
            // even if we can't understand its filename.
            var flyer = new FlyerFile
            {
                Retailer = "Unknown",
                FlyerDate = "Date unknown",
                PageDescription = "Pages unknown",
                FileName = fileName,
                FullPath = pdfPath
            };

            // Expected example:
            // 20260114_Woolies_p1-3
            string[] parts = nameWithoutExtension.Split('_');

            if (parts.Length < 3)
                return flyer;

            string datePart = parts[0];
            string retailerPart = parts[1];
            string pagesPart = parts[2];

            if (DateTime.TryParseExact(
                    datePart,
                    "yyyyMMdd",
                    null,
                    System.Globalization.DateTimeStyles.None,
                    out DateTime flyerDate))
            {
                flyer.FlyerDate = flyerDate.ToString("dd MMM yyyy");
            }

            flyer.Retailer = retailerPart switch
            {
                "Woolies" => "Woolworths",
                "Coles" => "Coles",
                _ => retailerPart
            };

            if (pagesPart.StartsWith("p"))
            {
                flyer.PageDescription =
                    $"Pages {pagesPart[1..].Replace("-", "–")}";
            }

            return flyer;
        }

    }
}