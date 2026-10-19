using Aigents.Domain.Entities;
using Aigents.Domain.Matching;
using Aigents.Infrastructure.Data;
using Aigents.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aigents.Domain.Tests;

public class BuyerMatcherTests
{
    private static ListingFacts L(string suburb = "Paddington", string type = "House", int? beds = 3, int? baths = 2,
        decimal? asking = 950_000m, decimal? estimate = null) =>
        new(Guid.NewGuid(), suburb, type, beds, baths, asking, estimate);

    private static MatchCriterion C(BuyerMatch m, string name) => Assert.Single(m.Criteria, c => c.Name == name);

    private static AigentsDbContext NewDb()
    {
        var opts = new DbContextOptionsBuilder<AigentsDbContext>()
            .UseInMemoryDatabase($"aigents-test-{Guid.NewGuid():N}")
            .Options;
        return new AigentsDbContext(opts);
    }

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

    [Fact]
    public async Task U2_GetPublishedListingByIdAsync_returns_only_Active_and_rejects_Draft_Withdrawn_PendingSignature()
    {
        await using var db = NewDb();
        var svc = new ListingService(db, NullLogger<ListingService>.Instance);

        var activeId = Guid.NewGuid();
        var draftId = Guid.NewGuid();
        var withdrawnId = Guid.NewGuid();
        var pendingSigId = Guid.NewGuid();

        db.Listings.AddRange(
            new Listing { Id = activeId, Address = "1 Active St", Suburb = "Paddington", PropertyType = "House", Status = ListingStatus.Active },
            new Listing { Id = draftId, Address = "2 Draft St", Suburb = "Paddington", PropertyType = "House", Status = ListingStatus.Draft },
            new Listing { Id = withdrawnId, Address = "3 Withdrawn St", Suburb = "Paddington", PropertyType = "House", Status = ListingStatus.Withdrawn },
            new Listing { Id = pendingSigId, Address = "4 PendingSignature St", Suburb = "Paddington", PropertyType = "House", Status = ListingStatus.PendingSignature }
        );
        await db.SaveChangesAsync();

        Assert.NotNull(await svc.GetPublishedListingByIdAsync(activeId));
        Assert.Null(await svc.GetPublishedListingByIdAsync(draftId));
        Assert.Null(await svc.GetPublishedListingByIdAsync(withdrawnId));
        Assert.Null(await svc.GetPublishedListingByIdAsync(pendingSigId));

        // Enquiries/inspections/offers must also be rejected on non-Active listings
        var draftInquiry = new ListingInquiry { Message = "Attempt on draft" };
        Assert.False(await svc.AddPublishedListingInquiryAsync(draftId, draftInquiry));
        Assert.True(await svc.AddPublishedListingInquiryAsync(activeId, new ListingInquiry { Message = "Valid enquiry" }));
    }

    [Fact]
    public async Task U3_QueryPublishedListingsForMatcherAsync_pushes_filters_before_Take_so_older_Paddington_match_is_never_dropped()
    {
        await using var db = NewDb();
        var svc = new ListingService(db, NullLogger<ListingService>.Instance);
        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var olderPaddingtonId = Guid.NewGuid();
        db.Listings.Add(new Listing
        {
            Id = olderPaddingtonId,
            Address = "10 Older Paddington Tce",
            Suburb = "Paddington",
            PropertyType = "House",
            Bedrooms = 3,
            Bathrooms = 2,
            AskingPrice = 950_000m,
            Status = ListingStatus.Active,
            PublishedAt = baseTime,
            CreatedAt = baseTime,
        });

        // Seed 220 newer published listings in other suburbs so an unfiltered Take(200) would drop the Paddington listing.
        for (var i = 1; i <= 220; i++)
        {
            db.Listings.Add(new Listing
            {
                Id = Guid.NewGuid(),
                Address = $"{i} Newer Elsewhere Rd",
                Suburb = "New Farm",
                PropertyType = "Unit",
                Bedrooms = 1,
                Bathrooms = 1,
                AskingPrice = 1_800_000m,
                Status = ListingStatus.Active,
                PublishedAt = baseTime.AddMinutes(i),
                CreatedAt = baseTime.AddMinutes(i),
            });
        }
        await db.SaveChangesAsync();

        var suburbPrefs = new BuyerPreferences(["Paddington"], "House", MinBedrooms: 3, MaxPrice: 1_000_000m);
        var suburbQuery = await svc.QueryPublishedListingsForMatcherAsync(suburbPrefs, take: 200);
        var matchedSuburb = Assert.Single(suburbQuery.Listings);
        Assert.Equal(olderPaddingtonId, matchedSuburb.Id);
        Assert.False(suburbQuery.Truncated);

        // Also verify when only soft criteria (e.g. MinBedrooms: 3) are provided without a suburb filter:
        // the 220 1-bedroom units cannot displace the older 3-bedroom house.
        var bedsOnlyPrefs = new BuyerPreferences(MinBedrooms: 3);
        var bedsOnlyQuery = await svc.QueryPublishedListingsForMatcherAsync(bedsOnlyPrefs, take: 200);
        var matchedBeds = Assert.Single(bedsOnlyQuery.Listings);
        Assert.Equal(olderPaddingtonId, matchedBeds.Id);
    }

    [Fact]
    public async Task V1_Interstate_manual_address_never_fabricates_Brisbane_coordinates_or_QLD_and_persists_explicit_location()
    {
        var loc = new ManualAddressResolution();

        // 1. Enter a street-only Melbourne address: coordinates must be null, State must NOT default to QLD,
        //    and validation must reject publishing until explicit Suburb, State, and Postcode are entered.
        loc.ApplyAddressSelection("120 Collins St");
        Assert.Equal("120 Collins St", loc.FullAddress);
        Assert.Equal(string.Empty, loc.Suburb);
        Assert.Equal(string.Empty, loc.State);
        Assert.Equal(string.Empty, loc.Postcode);
        Assert.Null(loc.Latitude);
        Assert.Null(loc.Longitude);
        Assert.False(loc.HasVerifiedCoordinates);
        Assert.False(loc.TryValidateForListing(out var missingError));
        Assert.NotNull(missingError);

        // 2. Seller fills explicit Melbourne VIC 3000 fields -> valid without fabricating coordinates
        loc.Suburb = "Melbourne";
        loc.State = "VIC";
        loc.Postcode = "3000";
        Assert.True(loc.TryValidateForListing(out var validError));
        Assert.Null(validError);
        Assert.Null(loc.Latitude);
        Assert.Null(loc.Longitude);

        // 3. Persist via ListingService and verify stored row has State == "VIC", Suburb == "Melbourne", Postcode == "3000"
        await using var db = NewDb();
        var svc = new ListingService(db, NullLogger<ListingService>.Instance);
        var created = await svc.CreateListingAsync(new Listing
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Address = loc.FullAddress,
            Suburb = loc.Suburb,
            State = loc.State,
            Postcode = loc.Postcode,
            PropertyType = "Unit",
            Bedrooms = 2,
            Bathrooms = 2,
            AskingPrice = 850_000m,
            Status = ListingStatus.Active,
        });

        var persisted = await svc.GetPublishedListingByIdAsync(created.Id);
        Assert.NotNull(persisted);
        Assert.Equal("Melbourne", persisted!.Suburb);
        Assert.Equal("VIC", persisted.State);
        Assert.Equal("3000", persisted.Postcode);
    }

    [Fact]
    public void V1_Editing_existing_address_clears_prior_suburb_state_postcode_and_coordinates_so_no_location_is_inherited()
    {
        var loc = new ManualAddressResolution();

        // Start with a fully populated Brisbane address including explicit coordinates
        loc.ApplyAddressSelection(
            fullAddress: "67 Northview Outlook, Paddington QLD 4064",
            explicitSuburb: "Paddington",
            explicitState: "QLD",
            explicitPostcode: "4064",
            explicitLat: -27.4598,
            explicitLng: 152.9992);

        Assert.Equal("Paddington", loc.Suburb);
        Assert.Equal("QLD", loc.State);
        Assert.Equal("4064", loc.Postcode);
        Assert.True(loc.HasVerifiedCoordinates);

        // Edit to a street-only Perth address ("45 St Georges Terrace"):
        // Prior Paddington / QLD / 4064 / coordinates MUST be cleared immediately.
        loc.ApplyAddressSelection("45 St Georges Terrace");
        Assert.Equal("45 St Georges Terrace", loc.FullAddress);
        Assert.Equal(string.Empty, loc.Suburb);
        Assert.Equal(string.Empty, loc.State);
        Assert.Equal(string.Empty, loc.Postcode);
        Assert.Null(loc.Latitude);
        Assert.Null(loc.Longitude);
        Assert.False(loc.HasVerifiedCoordinates);
        Assert.False(loc.TryValidateForListing(out _));

        // Edit to a complete interstate address ("45 St Georges Terrace, Perth WA 6000"):
        // Parses Perth / WA / 6000 while keeping coordinates null.
        loc.ApplyAddressSelection("45 St Georges Terrace, Perth WA 6000");
        Assert.Equal("Perth", loc.Suburb);
        Assert.Equal("WA", loc.State);
        Assert.Equal("6000", loc.Postcode);
        Assert.Null(loc.Latitude);
        Assert.Null(loc.Longitude);
        Assert.False(loc.HasVerifiedCoordinates);
        Assert.True(loc.TryValidateForListing(out _));
    }
}
