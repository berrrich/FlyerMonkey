using System.ComponentModel;

namespace FlyerMonkey.Reviewer.Windows.Models;

public class ExtractedProduct : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public bool PossibleDuplicate { get; set; }
    public string ProductName { get; set; } = string.Empty;

    public string? Brand { get; set; }

    public string? Variant { get; set; }

    public string? PackSizeText { get; set; }

    public string? Category { get; set; }
    // New structured product classifications
    public string? PrimaryCategory { get; set; }

    public List<string> Categories { get; set; } = new();
    public string? Barcode { get; set; }

    public string? Price { get; set; }
    public string? RegularPrice { get; set; }

    public string? CalculatedRegularPrice { get; set; }

    public string? Saving { get; set; }

    public string? UnitPrice { get; set; }

    public int? OfferQuantity { get; set; }
    public string? Promotion { get; set; }
    // New structured offer classifications
    public List<string> PromotionTypes { get; set; } = new();
    public string PromotionTypesDisplay =>
    PromotionTypes == null || PromotionTypes.Count == 0
        ? ""
        : string.Join(", ", PromotionTypes);
    private string? _imageFilePath;

    public string? ImageFilePath
    {
        get => _imageFilePath;
        set
        {
            if (_imageFilePath == value)
                return;

            _imageFilePath = value;
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(ImageFilePath)));
        }
    }

}