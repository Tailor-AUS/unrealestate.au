using Aigents.Domain.Matching;

namespace Aigents.Domain.Tests;

public class BuyerMatcherTests
{
    private static ListingFacts L(string suburb = "Paddington", string type = "House", int? beds = 3, int? baths = 2,
        decimal? asking = 950_000m, decimal? estimate = null) =>
        new(Guid.NewGuid(), suburb, type, beds, baths, asking, estimate);

    private static MatchCriterion C(BuyerMatch m, string name) => Assert.Single(m.Criteria, c => c.Name == name);

    [Fact]
    public void Full_fit_scores_100_and_explains_every_criterion()
    {
        var prefs = new BuyerPreferences(["paddington"], "house", 3, 2, 800_000m, 1_000_000m);
        var m = BuyerMatcher.Evaluate(prefs, L());
        Assert.Equal(100, m.Score);
        Assert.Equal(5, m.Criteria.Count);
        Assert.All(m.Criteria, c => Assert.Equal(CriterionOutcome.Met, c.Outcome));
        Assert.Contains("one of your suburbs", C(m, BuyerMatcher.LocationCriterion).Explanation);
        Assert.Contains("Asking $950,000", C(m, BuyerMatcher.PriceCriterion).Explanation);
    }

    [Fact]
    public void Unspecified_preferences_are_not_scored()
    {
        var m = BuyerMatcher.Evaluate(new BuyerPreferences(MinBedrooms: 3), L(beds: 4));
        var only = Assert.Single(m.Criteria);
        Assert.Equal(BuyerMatcher.BedroomsCriterion, only.Name);
        Assert.Equal(100, m.Score);
    }

    [Fact]
    public void One_room_short_is_partial_two_short_is_a_miss()
    {
        var prefs = new BuyerPreferences(MinBedrooms: 4);
        Assert.Equal(CriterionOutcome.Partial, C(BuyerMatcher.Evaluate(prefs, L(beds: 3)), BuyerMatcher.BedroomsCriterion).Outcome);
        Assert.Equal(CriterionOutcome.NotMet, C(BuyerMatcher.Evaluate(prefs, L(beds: 2)), BuyerMatcher.BedroomsCriterion).Outcome);
    }

    [Fact]
    public void Missing_agent_data_is_unknown_and_earns_nothing()
    {
        var m = BuyerMatcher.Evaluate(new BuyerPreferences(MinBedrooms: 3, MaxPrice: 1_000_000m), L(beds: null, asking: null));
        Assert.Equal(CriterionOutcome.Unknown, C(m, BuyerMatcher.BedroomsCriterion).Outcome);
        Assert.Equal(CriterionOutcome.Unknown, C(m, BuyerMatcher.PriceCriterion).Outcome);
        Assert.Equal(0, m.Score);
    }

    [Fact]
    public void Price_within_ten_percent_over_max_is_close_beyond_is_a_miss()
    {
        var prefs = new BuyerPreferences(MaxPrice: 1_000_000m);
        var close = C(BuyerMatcher.Evaluate(prefs, L(asking: 1_080_000m)), BuyerMatcher.PriceCriterion);
        Assert.Equal(CriterionOutcome.Partial, close.Outcome);
        Assert.Contains("8% above your max $1,000,000", close.Explanation);
        Assert.Equal(CriterionOutcome.NotMet, C(BuyerMatcher.Evaluate(prefs, L(asking: 1_200_000m)), BuyerMatcher.PriceCriterion).Outcome);
    }

    [Fact]
    public void Estimate_is_labelled_illustrative_and_never_fully_satisfies_price()
    {
        var m = BuyerMatcher.Evaluate(new BuyerPreferences(MaxPrice: 1_000_000m), L(asking: null, estimate: 900_000m));
        var price = C(m, BuyerMatcher.PriceCriterion);
        Assert.Equal(CriterionOutcome.Partial, price.Outcome);
        Assert.Contains(BuyerMatcher.EstimateDisclaimer, price.Explanation);
        Assert.True(m.PriceFromEstimate);
        Assert.Equal(50, m.Score);
    }

    [Fact]
    public void Asking_price_wins_over_estimate()
    {
        var m = BuyerMatcher.Evaluate(new BuyerPreferences(MaxPrice: 1_000_000m), L(asking: 950_000m, estimate: 2_000_000m));
        Assert.Equal(CriterionOutcome.Met, C(m, BuyerMatcher.PriceCriterion).Outcome);
        Assert.False(m.PriceFromEstimate);
    }

    [Fact]
    public void Score_is_the_weighted_share_of_specified_criteria()
    {
        // Location 35 (miss) + Bedrooms 15 (met) => 15/50 = 30.
        var m = BuyerMatcher.Evaluate(new BuyerPreferences(["New Farm"], MinBedrooms: 3), L(suburb: "Paddington", beds: 3));
        Assert.Equal(30, m.Score);
        Assert.Contains("not one of your suburbs (New Farm)", C(m, BuyerMatcher.LocationCriterion).Explanation);
    }

    [Fact]
    public void Rank_orders_by_score_drops_zero_and_needs_a_preference()
    {
        var best = L(suburb: "Paddington", beds: 4);
        var partial = L(suburb: "Paddington", beds: 2);
        var none = L(suburb: "Paddington", beds: 1);
        var prefs = new BuyerPreferences(["Paddington"], MinBedrooms: 3);

        var ranked = BuyerMatcher.Rank(prefs, [partial, none, best]);
        Assert.Equal([best.ListingId, partial.ListingId, none.ListingId], ranked.Select(r => r.ListingId));
        Assert.Empty(BuyerMatcher.Rank(new BuyerPreferences(), [best]));
    }

    [Fact]
    public void Wrong_suburb_or_type_is_not_a_match_even_with_other_fits()
    {
        var elsewhere = L(suburb: "New Farm", beds: 5, asking: 500_000m);
        var unit = L(suburb: "Paddington", type: "Unit", beds: 5, asking: 500_000m);
        var prefs = new BuyerPreferences(["Paddington"], "House", 3, MaxPrice: 1_000_000m);
        Assert.True(BuyerMatcher.Evaluate(prefs, elsewhere).Score > 0); // still explainable on its own
        Assert.Empty(BuyerMatcher.Rank(prefs, [elsewhere, unit]));
    }
}
