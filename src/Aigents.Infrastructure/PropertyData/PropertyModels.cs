using System.Text.Json.Serialization;

namespace Aigents.Infrastructure.PropertyData;

/// <summary>
/// Unified property model for Buyer persona, combining listing data and historical data.
/// </summary>
public class BuyerProperty
{
    public required string Id { get; set; }
    public required string Source { get; set; } // "Domain", "CoreLogic"

    // Address
    public required string Address { get; set; }
    public string? UnitNumber { get; set; }
    public string? StreetNumber { get; set; }
    public string? StreetName { get; set; }
    public string? Suburb { get; set; }
    public string? State { get; set; }
    public string? Postcode { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    // Attributes
    public PropertyType Type { get; set; }
    public int Bedrooms { get; set; }
    public int Bathrooms { get; set; }
    public int CarSpaces { get; set; }
    public double? LandAreaSqm { get; set; }
    public double? FloorAreaSqm { get; set; }

    // Listing Info (if on market)
    public bool IsOnMarket { get; set; }
    public ListingStatus Status { get; set; }
    public string? PriceDisplay { get; set; }
    public decimal? PriceLow { get; set; }
    public decimal? PriceHigh { get; set; }
    public string? AgencyName { get; set; }
    public string? AgentName { get; set; }
    public string? ListingUrl { get; set; }
    public string? MainImageUrl { get; set; }
    public List<string> ImageUrls { get; set; } = new();
    public string? Headline { get; set; }
    public string? Description { get; set; }

    // Intelligence (AVM / History)
    public decimal? EstimatedValue { get; set; }
    public decimal? EstimatedValueLow { get; set; }
    public decimal? EstimatedValueHigh { get; set; }
    public string? EstimatedValueConfidence { get; set; } // High, Medium, Low
    public decimal? LastSalePrice { get; set; }
    public DateTimeOffset? LastSaleDate { get; set; }
    public decimal? RentalEstimate { get; set; }
    public decimal? RentalYield { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum PropertyType
{
    House,
    Apartment,
    Unit,
    Townhouse,
    Land,
    Rural,
    Commercial,
    Other
}

public enum ListingStatus
{
    OffMarket,
    ForSale,
    UnderOffer,
    Sold,
    ForRent,
    Leased,
    Withdrawn
}

public class PropertySearchFilter
{
    public string? Query { get; set; } // Address or Suburb
    public string? Suburb { get; set; }
    public string? State { get; set; }
    public string? Postcode { get; set; }

    public PropertyType? Type { get; set; }
    public int? MinBedrooms { get; set; }
    public int? MinBathrooms { get; set; }
    public int? MinCarSpaces { get; set; }

    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }

    public bool IncludeOffMarket { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class SearchResults
{
    public List<BuyerProperty> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public class SuburbProfile
{
    public required string Suburb { get; set; }
    public required string State { get; set; }
    public required string Postcode { get; set; }

    public decimal MedianHousePrice { get; set; }
    public decimal MedianUnitPrice { get; set; }
    public double HouseGrowthRate { get; set; } // Annual %
    public double UnitGrowthRate { get; set; }

    public int DaysOnMarket { get; set; }
    public double ClearanceRate { get; set; }

    public List<string> TopSchools { get; set; } = new();
    public string? DemographicsSummary { get; set; }
}
