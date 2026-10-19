using System.Text.RegularExpressions;

namespace Aigents.Domain.Matching;

/// <summary>
/// Manages manual address entry and explicit Australian location fields (Suburb, State, Postcode)
/// without fabricating Brisbane coordinates (-27.4698, 153.0251) or defaulting State to QLD.
/// Clearing or changing the address always resets previously derived location fields.
/// </summary>
public sealed class ManualAddressResolution
{
    public static readonly IReadOnlyList<string> ValidAustralianStates =
    [
        "NSW", "VIC", "QLD", "WA", "SA", "TAS", "ACT", "NT"
    ];

    private static readonly Regex ExplicitAuAddressSuffix = new(
        @",\s*(?<suburb>[A-Za-z][A-Za-z\s\-']+?)\s+(?<state>NSW|VIC|QLD|WA|SA|TAS|ACT|NT)\s+(?<postcode>\d{4})\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex FourDigitPostcode = new(
        @"^\d{4}$",
        RegexOptions.Compiled);

    public string FullAddress { get; set; } = string.Empty;
    public string Suburb { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Postcode { get; set; } = string.Empty;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    public bool HasVerifiedCoordinates =>
        Latitude is double lat && double.IsFinite(lat) &&
        Longitude is double lng && double.IsFinite(lng);

    /// <summary>
    /// Resets all address and derived location fields so no prior address state is inherited.
    /// </summary>
    public void Reset()
    {
        FullAddress = string.Empty;
        Suburb = string.Empty;
        State = string.Empty;
        Postcode = string.Empty;
        Latitude = null;
        Longitude = null;
    }

    /// <summary>
    /// Applies a newly entered or selected address. Always clears prior derived fields first
    /// so editing an existing address cannot retain stale suburb, state, postcode, or coordinates.
    /// </summary>
    public void ApplyAddressSelection(
        string? fullAddress,
        string? explicitSuburb = null,
        string? explicitState = null,
        string? explicitPostcode = null,
        double? explicitLat = null,
        double? explicitLng = null)
    {
        Reset();

        var cleanedAddress = (fullAddress ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(cleanedAddress))
        {
            return;
        }

        FullAddress = cleanedAddress;

        if (explicitLat is double lat && double.IsFinite(lat) &&
            explicitLng is double lng && double.IsFinite(lng))
        {
            Latitude = lat;
            Longitude = lng;
        }

        // Parse explicit ", Suburb STATE 1234" only when all three tokens are unambiguously present in the input.
        var match = ExplicitAuAddressSuffix.Match(cleanedAddress);
        if (match.Success)
        {
            Suburb = match.Groups["suburb"].Value.Trim();
            State = match.Groups["state"].Value.Trim().ToUpperInvariant();
            Postcode = match.Groups["postcode"].Value.Trim();
        }

        if (!string.IsNullOrWhiteSpace(explicitSuburb))
        {
            Suburb = explicitSuburb.Trim();
        }

        if (!string.IsNullOrWhiteSpace(explicitState))
        {
            var normalizedState = explicitState.Trim().ToUpperInvariant();
            State = ValidAustralianStates.Contains(normalizedState, StringComparer.OrdinalIgnoreCase)
                ? normalizedState
                : string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(explicitPostcode))
        {
            var trimmedPc = explicitPostcode.Trim();
            Postcode = FourDigitPostcode.IsMatch(trimmedPc) ? trimmedPc : string.Empty;
        }
    }

    /// <summary>
    /// Validates that explicit street address, suburb, Australian state/territory, and 4-digit postcode
    /// are present before allowing listing intelligence fetch or publication.
    /// </summary>
    public bool TryValidateForListing(out string? error)
    {
        if (string.IsNullOrWhiteSpace(FullAddress))
        {
            error = "Enter the street address for the property.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Suburb))
        {
            error = "Enter the suburb for the property.";
            return false;
        }

        var normalizedState = (State ?? string.Empty).Trim().ToUpperInvariant();
        if (!ValidAustralianStates.Contains(normalizedState, StringComparer.Ordinal))
        {
            error = "Select a valid Australian state or territory (NSW, VIC, QLD, WA, SA, TAS, ACT, or NT).";
            return false;
        }

        var trimmedPostcode = (Postcode ?? string.Empty).Trim();
        if (!FourDigitPostcode.IsMatch(trimmedPostcode))
        {
            error = "Enter a valid 4-digit Australian postcode.";
            return false;
        }

        error = null;
        return true;
    }
}
