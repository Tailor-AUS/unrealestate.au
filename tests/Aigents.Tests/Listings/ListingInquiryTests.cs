using Aigents.Domain.Entities;
using Aigents.Infrastructure.Data;
using Aigents.Infrastructure.Growth;
using Aigents.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aigents.Tests.Listings;

public sealed class ListingInquiryTests
{
    public static TheoryData<ListingInquiry> ContactlessInquiries => new()
    {
        new ListingInquiry
        {
            InquiryType = BuyerInquiryType.Question,
            Message = "Can I inspect?"
        },
        new ListingInquiry
        {
            InquiryType = BuyerInquiryType.Inspection,
            Message = "Saturday morning"
        },
        new ListingInquiry
        {
            InquiryType = BuyerInquiryType.Offer,
            Message = "Offer",
            OfferAmount = 500_000m
        },
        new ListingInquiry
        {
            InquiryType = BuyerInquiryType.Offer,
            Message = "Offer",
            BuyerEmail = "buyer@example.com",
            OfferAmount = 0m
        }
    };

    [Theory]
    [MemberData(nameof(ContactlessInquiries))]
    public async Task CreateInquiryAsync_RejectsContactlessOrInvalidInquiry(
        ListingInquiry inquiry)
    {
        await using var db = CreateContext();
        var listing = await AddActiveListingAsync(db);
        var service = CreateService(db);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateInquiryAsync(listing.Id, inquiry));

        Assert.Empty(await db.ListingInquiries.ToListAsync());
    }

    [Fact]
    public async Task CreateInquiryAsync_PersistsReplyChannelAndPositiveOffer()
    {
        await using var db = CreateContext();
        var listing = await AddActiveListingAsync(db);
        var service = CreateService(db);
        var inquiry = new ListingInquiry
        {
            InquiryType = BuyerInquiryType.Offer,
            Message = "  Offer subject to finance  ",
            BuyerName = "  Buyer  ",
            BuyerEmail = "buyer@example.com",
            OfferAmount = 500_000m
        };

        var saved = await service.CreateInquiryAsync(listing.Id, inquiry);

        Assert.Equal("Offer subject to finance", saved.Message);
        Assert.Equal("Buyer", saved.BuyerName);
        Assert.Equal("buyer@example.com", saved.BuyerEmail);
        Assert.Equal(500_000m, saved.OfferAmount);
    }

    [Fact]
    public async Task GetListingsForUserAsync_LoadsLegacyAgentInquiryIdentity()
    {
        await using var db = CreateContext();
        var listing = await AddActiveListingAsync(db);
        var agent = new Agent
        {
            Name = "Legacy agent",
            Email = "agent@example.com",
            Phone = "0400000000",
            AgencyName = "Test agency",
            LicenseNumber = "TEST-1"
        };
        db.ListingInquiries.Add(new ListingInquiry
        {
            ListingId = listing.Id,
            Listing = listing,
            AgentId = agent.Id,
            Agent = agent,
            InquiryType = BuyerInquiryType.Agent,
            Message = "Agent enquiry"
        });
        await db.SaveChangesAsync();

        var result = await CreateService(db)
            .GetListingsForUserAsync(listing.UserId);

        var inquiry = Assert.Single(Assert.Single(result).Inquiries);
        Assert.Equal("Legacy agent", inquiry.Agent?.Name);
        Assert.Equal("agent@example.com", inquiry.Agent?.Email);
    }

    private static AigentsDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AigentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new AigentsDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static async Task<Listing> AddActiveListingAsync(AigentsDbContext db)
    {
        var owner = new User { Name = "Owner" };
        var listing = new Listing
        {
            User = owner,
            UserId = owner.Id,
            Address = "1 Test Street",
            Suburb = "Brisbane",
            State = "QLD",
            Postcode = "4000",
            Status = ListingStatus.Active
        };
        db.AddRange(owner, listing);
        await db.SaveChangesAsync();
        return listing;
    }

    private static ListingService CreateService(AigentsDbContext db) =>
        new(
            db,
            new StubProductEventRecorder(),
            NullLogger<ListingService>.Instance);

    private sealed class StubProductEventRecorder : IProductEventRecorder
    {
        public Task<bool> RecordAsync(
            Guid userId,
            string eventName,
            Guid? listingId = null,
            TimeSpan? deduplicationWindow = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<GrowthMetrics> GetGrowthMetricsAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ListingActivity> GetListingActivityAsync(
            Guid listingId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
