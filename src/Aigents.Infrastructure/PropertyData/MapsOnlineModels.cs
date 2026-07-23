namespace Aigents.Infrastructure.PropertyData;

/// <summary>
/// Models for QLD MapsOnline API responses
/// </summary>
public class MapsOnlineReportRequest
{
    public string ReportType { get; set; } = "ALLVEG";
    public string FeatureType { get; set; } = "LotPlan";
    public string Features { get; set; } = ""; // e.g., "1,RP123456" for Lot 1 on RP123456
    public string EmailAddress { get; set; } = "";
    public string? CustomerReference { get; set; }
}

public class MapsOnlineResponse
{
    public string Message { get; set; } = "";
    public string MessageCode { get; set; } = "";
    public List<string> Output { get; set; } = new();
    public int Status { get; set; }
    public bool Success { get; set; }
}

public class MapsOnlineReportType
{
    public string Type { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string> AllowedFeatureTypes { get; set; } = new();
    public List<string> FormatTypes { get; set; } = new();
    public bool AllowGeoJson { get; set; }
    public double MaxGeoJsonAreaSkm { get; set; }
}

public class MapsOnlineFeatureType
{
    public string Type { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string GeomType { get; set; } = "";
    public List<string> Fields { get; set; } = new();
    public List<string> FieldsPretty { get; set; } = new();
}

/// <summary>
/// Parsed property report data from MapsOnline
/// </summary>
public class PropertyReport
{
    public string LotPlan { get; set; } = "";
    public string Address { get; set; } = "";
    public DateTimeOffset ReportDate { get; set; } = DateTimeOffset.UtcNow;

    // Land Information
    public double? LandAreaSqm { get; set; }
    public string? LocalGovernmentArea { get; set; }
    public string? LandUseCategory { get; set; }

    // Zoning & Planning
    public string? ZoningCode { get; set; }
    public string? ZoningDescription { get; set; }
    public bool? IsStrategicCroppingLand { get; set; }

    // Environmental Overlays
    public bool? HasVegetationOverlay { get; set; }
    public string? VegetationCategory { get; set; }
    public bool? HasKoalaHabitat { get; set; }
    public bool? HasCoastalHazard { get; set; }
    public bool? HasFloodOverlay { get; set; }
    public bool? HasEnvironmentallySignificantAreas { get; set; }

    // Development Potential
    public string? DevelopmentPotentialSummary { get; set; }
    public List<string> DevelopmentConstraints { get; set; } = new();
    public List<string> DevelopmentOpportunities { get; set; } = new();
}

/// <summary>
/// Available report types for property intelligence
/// </summary>
public static class MapsOnlineReportTypes
{
    /// <summary>Vegetation Management Report - Most comprehensive property report</summary>
    public const string VegetationManagement = "ALLVEG";

    /// <summary>Queensland Land Use classification</summary>
    public const string LandUse = "QLUMP";

    /// <summary>Matters of State Environmental Significance</summary>
    public const string EnvironmentalSignificance = "MSES";

    /// <summary>Coastal Hazard Areas</summary>
    public const string CoastalHazard = "CHAZD";

    /// <summary>Strategic Cropping Land</summary>
    public const string StrategicCroppingLand = "SCL";

    /// <summary>Protected Plants Flora Survey</summary>
    public const string ProtectedPlants = "PP";

    /// <summary>Regional Ecosystems</summary>
    public const string RegionalEcosystems = "REREP";

    /// <summary>Agricultural Values Assessment</summary>
    public const string AgriculturalValues = "AGVALUES";

    /// <summary>Queensland Wetlands</summary>
    public const string Wetlands = "QWETL";
}
