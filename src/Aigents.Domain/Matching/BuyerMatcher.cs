namespace Aigents.Domain.Matching;

/// <summary>
/// What a buyer told us they want. Every field is optional; an unset field is
/// not scored (it neither helps nor hurts a listing).
/// </summary>
public sealed record BuyerPreferences(
    IReadOnlyList<string>? Suburbs = null,
    string? PropertyType = null,
    int? MinBedrooms = null,
    int? MinBathrooms = null,
    decimal? MinPrice = null,
    decimal? MaxPrice = null)
{
    public bool HasAny =>
        (Suburbs?.Any(s => !string.IsNullOrWhiteSpace(s)) ?? false)
        || !string.IsNullOrWhiteSpace(PropertyType)
        || MinBedrooms is not null || MinBathrooms is not null
        || MinPrice is not null || MaxPrice is not null;
}

/// <summary>
/// The agent-supplied facts a match is computed from. AskingPrice is the
/// agent's price; EstimatedValue is an AI/AVM estimate and is illustrative
/// only — it is never treated as verified market evidence.
/// </summary>
public sealed record ListingFacts(
    Guid ListingId,
    string Suburb,
    string PropertyType,
    int? Bedrooms,
    int? Bathrooms,
    decimal? AskingPrice,
    decimal? EstimatedValue);

public enum CriterionOutcome { Met, Partial, NotMet, Unknown }

public sealed record MatchCriterion(string Name, CriterionOutcome Outcome, int Weight, string Explanation);

public sealed record BuyerMatch(Guid ListingId, int Score, IReadOnlyList<MatchCriterion> Criteria)
{
    /// <summary>True when the price criterion leaned on an illustrative estimate.</summary>
    public bool PriceFromEstimate => Criteria.Any(c => c.Name == BuyerMatcher.PriceCriterion && c.Explanation.Contains(BuyerMatcher.EstimateDisclaimer, StringComparison.Ordinal));
}

/// <summary>
/// Deterministic, explainable buyer→listing matching. No AI: each criterion
/// carries its weight, outcome and a plain-language reason, and the score is
/// the weighted share of the criteria the buyer actually specified.
/// </summary>
public static class BuyerMatcher
{
    public const string LocationCriterion = "Location";
    public const string PriceCriterion = "Price";
    public const string BedroomsCriterion = "Bedrooms";
    public const string BathroomsCriterion = "Bathrooms";
    public const string TypeCriterion = "Property type";

    public const string EstimateDisclaimer = "illustrative estimate, not verified market evidence";

    /// <summary>Asking price up to this share above the buyer's max is "close", not a miss.</summary>
    public const decimal PriceStretch = 0.10m;

    private const int LocationWeight = 35, PriceWeight = 30, BedroomsWeight = 15, BathroomsWeight = 10, TypeWeight = 10;

    public static BuyerMatch Evaluate(BuyerPreferences prefs, ListingFacts listing)
    {
        var criteria = new List<MatchCriterion>();

        var suburbs = prefs.Suburbs?.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList() ?? [];
        if (suburbs.Count > 0)
        {
            var hit = suburbs.Any(s => string.Equals(s, listing.Suburb?.Trim(), StringComparison.OrdinalIgnoreCase));
            criteria.Add(new(LocationCriterion, hit ? CriterionOutcome.Met : CriterionOutcome.NotMet, LocationWeight,
                hit ? $"In {listing.Suburb}, one of your suburbs." : $"In {Display(listing.Suburb)}, not one of your suburbs ({string.Join(", ", suburbs)})."));
        }

        if (!string.IsNullOrWhiteSpace(prefs.PropertyType))
        {
            var hit = string.Equals(prefs.PropertyType.Trim(), listing.PropertyType?.Trim(), StringComparison.OrdinalIgnoreCase);
            criteria.Add(new(TypeCriterion, hit ? CriterionOutcome.Met : CriterionOutcome.NotMet, TypeWeight,
                hit ? $"{listing.PropertyType}, as you asked." : $"{Display(listing.PropertyType)}, not a {prefs.PropertyType.Trim()}."));
        }

        if (prefs.MinBedrooms is int minBeds)
            criteria.Add(Rooms(BedroomsCriterion, "bedroom", minBeds, listing.Bedrooms, BedroomsWeight));
        if (prefs.MinBathrooms is int minBaths)
            criteria.Add(Rooms(BathroomsCriterion, "bathroom", minBaths, listing.Bathrooms, BathroomsWeight));

        if (prefs.MinPrice is not null || prefs.MaxPrice is not null)
            criteria.Add(Price(prefs.MinPrice, prefs.MaxPrice, listing));

        var total = criteria.Sum(c => c.Weight);
        var earned = criteria.Sum(c => c.Weight * Credit(c.Outcome));
        var score = total == 0 ? 0 : (int)Math.Round(100m * earned / total, MidpointRounding.AwayFromZero);
        return new BuyerMatch(listing.ListingId, score, criteria);
    }

    /// <summary>
    /// Rank listings for a buyer. A listing outside the buyer's suburbs or of
    /// another property type is not a match at all (hard criteria); the rest are
    /// ordered by score, zero scores dropped, ties keep input order.
    /// </summary>
    public static IReadOnlyList<BuyerMatch> Rank(BuyerPreferences prefs, IEnumerable<ListingFacts> listings)
    {
        if (!prefs.HasAny) return [];
        return listings
            .Select((l, i) => (Match: Evaluate(prefs, l), Index: i))
            .Where(x => x.Match.Score > 0 && !x.Match.Criteria.Any(IsHardMiss))
            .OrderByDescending(x => x.Match.Score)
            .ThenBy(x => x.Index)
            .Select(x => x.Match)
            .ToList();
    }

    public static bool IsHardMiss(MatchCriterion c) =>
        c.Outcome == CriterionOutcome.NotMet && (c.Name == LocationCriterion || c.Name == TypeCriterion);

    private static decimal Credit(CriterionOutcome o) => o switch
    {
        CriterionOutcome.Met => 1m,
        CriterionOutcome.Partial => 0.5m,
        _ => 0m, // Unknown earns nothing: missing data is never assumed to fit.
    };

    private static MatchCriterion Rooms(string name, string noun, int min, int? actual, int weight)
    {
        if (actual is not int n)
            return new(name, CriterionOutcome.Unknown, weight, $"Agent has not supplied the {noun} count.");
        if (n >= min)
            return new(name, CriterionOutcome.Met, weight, $"{n} {Plural(noun, n)} (you want {min}+).");
        if (n == min - 1)
            return new(name, CriterionOutcome.Partial, weight, $"{n} {Plural(noun, n)}, one short of your {min}+.");
        return new(name, CriterionOutcome.NotMet, weight, $"{n} {Plural(noun, n)}, fewer than your {min}+.");
    }

    private static MatchCriterion Price(decimal? min, decimal? max, ListingFacts l)
    {
        var fromEstimate = l.AskingPrice is null && l.EstimatedValue is not null;
        var price = l.AskingPrice ?? l.EstimatedValue;
        var range = RangeText(min, max);
        if (price is not decimal p || p <= 0)
            return new(PriceCriterion, CriterionOutcome.Unknown, PriceWeight, "Agent has not supplied a price.");

        var basis = fromEstimate ? $"Estimate {Money(p)} ({EstimateDisclaimer})" : $"Asking {Money(p)}";
        CriterionOutcome outcome;
        string why;
        if (max is decimal hi && p > hi)
        {
            var over = (p - hi) / hi;
            outcome = over <= PriceStretch ? CriterionOutcome.Partial : CriterionOutcome.NotMet;
            why = $"{basis}, {Math.Round(over * 100m, 0, MidpointRounding.AwayFromZero)}% above your max {Money(hi)}.";
        }
        else if (min is decimal lo && p < lo)
        {
            outcome = CriterionOutcome.Partial;
            why = $"{basis}, below your minimum {Money(lo)}.";
        }
        else
        {
            outcome = CriterionOutcome.Met;
            why = $"{basis}, within your range {range}.";
        }

        // An estimate can inform a match but never fully satisfy a price brief.
        if (fromEstimate && outcome == CriterionOutcome.Met) outcome = CriterionOutcome.Partial;
        return new(PriceCriterion, outcome, PriceWeight, why);
    }

    private static string RangeText(decimal? min, decimal? max) => (min, max) switch
    {
        (decimal a, decimal b) => $"{Money(a)}–{Money(b)}",
        (decimal a, null) => $"{Money(a)}+",
        (null, decimal b) => $"up to {Money(b)}",
        _ => "any",
    };

    private static string Money(decimal v) => v.ToString("$#,0", System.Globalization.CultureInfo.InvariantCulture);
    private static string Plural(string noun, int n) => n == 1 ? noun : noun + "s";
    private static string Display(string? s) => string.IsNullOrWhiteSpace(s) ? "an unspecified location" : s.Trim();
}
