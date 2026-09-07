using System;
using System.Collections.Generic;
using System.Text;

namespace FlyerMonkey.Shared.Model;

public class Offer
{
    public int ID { get; set; }

    public int ProductID { get; set; }

    public int RetailerID { get; set; }

    public int? RetailerLocationID { get; set; }

    public decimal? AdvertisedPrice { get; set; }

    public decimal? RegularPrice { get; set; }

    public decimal? AdvertisedSaving { get; set; }

    public DateTime? ValidFrom { get; set; }

    public DateTime? ValidTo { get; set; }

    public string? SourceType { get; set; }

    public string? SourceDescription { get; set; }

    public int? FlyerPageNumber { get; set; }

    public string? FlyerImageBlobPath { get; set; }

    public string? PromoText { get; set; }

    public string? UnitPriceText { get; set; }

    public int? MultiBuyQuantity { get; set; }

    public decimal? MultiBuyPrice { get; set; }

    public decimal? MemberPrice { get; set; }

    public bool? MemberOnlyFlag { get; set; }

    public string? SourceProductText { get; set; }

    public DateTime CreatedUtc { get; set; }
}
