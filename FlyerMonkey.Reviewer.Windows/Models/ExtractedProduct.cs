using System.ComponentModel;

using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using SQLServerConnection.Data;

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
    public string? FlyerValidFrom { get; set; }
    public string? FlyerValidTo { get; set; }
    private bool _memberOnly;
    public bool MemberOnly { get => _memberOnly; set => SetField(ref _memberOnly, value); }
    private string? _memberPrice;
    public string? MemberPrice { get => _memberPrice; set => SetField(ref _memberPrice, value); }
    private string? _multiBuyPrice;
    public string? MultiBuyPrice { get => _multiBuyPrice; set => SetField(ref _multiBuyPrice, value); }
    [JsonIgnore]
    public bool CanEditOffer { get; set; } = true;
    public int? ConfirmedOfferID { get; set; }
    private OfferReviewChoice? _offerReview;
    public OfferReviewChoice? OfferReview
    {
        get => _offerReview;
        set
        {
            SetField(ref _offerReview, value);
            PropertyChanged?.Invoke(this, new(nameof(OfferDecisionText)));
        }
    }
    [JsonIgnore]
    public string OfferDecisionText => OfferReview switch
    {
        OfferReviewChoice.ReuseExisting => $"Reuse OfferID {ConfirmedOfferID} if still equivalent at commit",
        OfferReviewChoice.KeepDistinct => "Keep as distinct offer",
        OfferReviewChoice.ReviewLater => "Review later. Commit saves this offer separately.",
        _ => ""
    };
    private IReadOnlyList<OfferCandidate> _similarOffers = [];
    [JsonIgnore]
    public IReadOnlyList<OfferCandidate> SimilarOffers { get => _similarOffers; set => SetField(ref _similarOffers, value); }
    private string _offerMatchStatus = "Check offers after confirming the flyer dates.";
    [JsonIgnore]
    public string OfferMatchStatus { get => _offerMatchStatus; set => SetField(ref _offerMatchStatus, value); }
    private bool _canCheckOffers = true;
    [JsonIgnore]
    public bool CanCheckOffers { get => _canCheckOffers; set => SetField(ref _canCheckOffers, value); }
    [JsonIgnore]
    public int OfferPreviewVersion { get; private set; }

    public void InvalidateOfferMatches()
    {
        OfferPreviewVersion++;
        SimilarOffers = [];
        OfferMatchStatus = "Check offers after confirming the flyer dates.";
    }

    private CatalogueReviewDecision? _catalogueReview;
    public CatalogueReviewDecision? CatalogueReview
    {
        get => _catalogueReview;
        set
        {
            SetField(ref _catalogueReview, value);
            PropertyChanged?.Invoke(this, new(nameof(CatalogueDecisionText)));
        }
    }

    [JsonIgnore]
    public string CatalogueDecisionText => CatalogueReview?.Choice switch
    {
        ProductReviewChoice.ReuseExisting when !CatalogueReview.AppliesTo(this) =>
            "Product details changed. Confirm reuse again or commit as separate.",
        ProductReviewChoice.ReuseExisting => $"Reuse ProductID {CatalogueReview.Target?.ProductID}",
        ProductReviewChoice.KeepSeparate => "Keep as separate product",
        ProductReviewChoice.ReviewLater => "Review later. Commit creates a separate product.",
        _ => "Automatic matching at commit"
    };

    private IReadOnlyList<CatalogueCandidate> _catalogueCandidates = [];
    [JsonIgnore]
    public IReadOnlyList<CatalogueCandidate> CatalogueCandidates
    {
        get => _catalogueCandidates;
        set => SetField(ref _catalogueCandidates, value);
    }

    private string _catalogueStatus = "Catalogue matches have not been checked.";
    [JsonIgnore]
    public string CatalogueStatus { get => _catalogueStatus; set => SetField(ref _catalogueStatus, value); }

    private bool _canCheckCatalogue = true;
    [JsonIgnore]
    public bool CanCheckCatalogue { get => _canCheckCatalogue; set => SetField(ref _canCheckCatalogue, value); }

    private string _productName = string.Empty;
    public string ProductName { get => _productName; set => SetIdentityField(ref _productName, value); }

    private string? _brand;
    public string? Brand { get => _brand; set => SetIdentityField(ref _brand, value); }

    private string? _variant;
    public string? Variant { get => _variant; set => SetIdentityField(ref _variant, value); }

    private string? _packSizeText;
    public string? PackSizeText { get => _packSizeText; set => SetIdentityField(ref _packSizeText, value); }

    private string? _category;
    public string? Category { get => _category; set => SetField(ref _category, value); }
    // New structured product classifications
    private string? _primaryCategory;
    public string? PrimaryCategory { get => _primaryCategory; set => SetField(ref _primaryCategory, value); }

    public List<string> Categories { get; set; } = new();
    private string? _barcode;
    public string? Barcode { get => _barcode; set => SetIdentityField(ref _barcode, value); }

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
    private List<string> _promotionTypes = new();
    public List<string> PromotionTypes { get => _promotionTypes; set => SetField(ref _promotionTypes, value); }
    public string PromotionTypesDisplay =>
    PromotionTypes == null || PromotionTypes.Count == 0
        ? ""
        : string.Join(", ", PromotionTypes);
    // Editors and display TextBlocks bind independently to the same model.
    private void SetIdentityField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        SetField(ref field, value, propertyName);
        CatalogueCandidates = [];
        InvalidateOfferMatches();
        CatalogueStatus = "Check catalogue matches for the current product details.";
        PropertyChanged?.Invoke(this, new(nameof(CatalogueDecisionText)));
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        if (propertyName is nameof(Price) or nameof(RegularPrice) or nameof(Saving) or nameof(UnitPrice)
            or nameof(OfferQuantity) or nameof(Promotion) or nameof(PromotionTypes) or nameof(MemberOnly)
            or nameof(MemberPrice) or nameof(MultiBuyPrice) or nameof(CatalogueReview))
            InvalidateOfferMatches();
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
