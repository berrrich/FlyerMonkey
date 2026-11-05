using System.ComponentModel;

using System.Runtime.CompilerServices;

namespace FlyerMonkey.Reviewer.Windows.Models;

public class ExtractedProduct : INotifyPropertyChanged, IDataErrorInfo
{
    private bool _isCraterGenerating;
    private string _craterStatus = "";
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsCraterGenerating { get => _isCraterGenerating; set { SetField(ref _isCraterGenerating, value); PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(CanCrater))); } }
    [System.Text.Json.Serialization.JsonIgnore]
    public bool CanCrater => !IsCraterGenerating;
    [System.Text.Json.Serialization.JsonIgnore]
    public string CraterStatus { get => _craterStatus; set => SetField(ref _craterStatus,value); }
    [System.Text.Json.Serialization.JsonIgnore]
    public string Error => string.Join(" ", Services.ProductValidationService.ValidateBatch(new[] { this }));
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasErrors => !string.IsNullOrEmpty(Error);
    public string this[string field] => Services.ProductValidationService.FieldError(this, field) ?? "";
    public event PropertyChangedEventHandler? PropertyChanged;
    public string? ArtworkId { get; set; }
    public string? PackSizeEvidence { get; set; }
    public bool PackSizeManuallyCorrected { get; set; }
    public bool PossibleDuplicate { get; set; }
    private string _productName = string.Empty;
    public string ProductName { get => _productName; set => SetField(ref _productName, value); }

    private string? _brand;
    public string? Brand { get => _brand; set => SetField(ref _brand, value); }

    private string? _variant;
    public string? Variant { get => _variant; set => SetField(ref _variant, value); }

    private string? _packSizeText;
    public string? PackSizeText { get => _packSizeText; set => SetField(ref _packSizeText, value); }

    private string? _category;
    public string? Category { get => _category; set => SetField(ref _category, value); }
    // New structured product classifications
    private string? _primaryCategory;
    public string? PrimaryCategory { get => _primaryCategory; set => SetField(ref _primaryCategory, value); }

    public List<string> Categories { get; set; } = new();
    public string? Barcode { get; set; }

    private string? _price;
    public string? Price { get => _price; set => SetField(ref _price, value); }
    private string? _regularPrice;
    public string? RegularPrice { get => _regularPrice; set => SetField(ref _regularPrice, value); }

    private string? _calculatedRegularPrice;
    public string? CalculatedRegularPrice { get => _calculatedRegularPrice; set => SetField(ref _calculatedRegularPrice, value); }

    private string? _saving;
    public string? Saving { get => _saving; set => SetField(ref _saving, value); }

    private string? _unitPrice;
    public string? UnitPrice { get => _unitPrice; set => SetField(ref _unitPrice, value); }

    private int? _offerQuantity;
    public int? OfferQuantity { get => _offerQuantity; set => SetField(ref _offerQuantity, value); }
    private string? _promotion;
    public string? Promotion { get => _promotion; set => SetField(ref _promotion, value); }
    // New structured offer classifications
    public List<string> PromotionTypes { get; set; } = new();
    public string PromotionTypesDisplay =>
    PromotionTypes == null || PromotionTypes.Count == 0
        ? ""
        : string.Join(", ", PromotionTypes);
    // Editors and display TextBlocks bind independently to the same model.
    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasErrors)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Error)));
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasImage => !string.IsNullOrWhiteSpace(ImageFilePath);
    [System.Text.Json.Serialization.JsonIgnore]
    public string ImageActionText => "Crater It!";
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
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasImage)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ImageActionText)));
        }
    }

}
