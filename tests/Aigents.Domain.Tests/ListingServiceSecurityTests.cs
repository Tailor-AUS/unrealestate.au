using Aigents.Domain.Enquiries;
using Aigents.Domain.Entities;
using Aigents.Infrastructure.Data;
using Aigents.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aigents.Domain.Tests;

public class ListingServiceSecurityTests
{
    private static AigentsDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<AigentsDbContext>()
            .UseInMemoryDatabase(name)
            .Options);

    [Fact]
    public async Task AddPublishedListingInquiryAsync_ReusesSingleProxyAgent_PreventingUniqueEmailConstraintCrash()
    {
        await using var db = CreateDb(nameof(AddPublishedListingInquiryAsync_ReusesSingleProxyAgent_PreventingUniqueEmailConstraintCrash));
        var owner = new User { Id = Guid.NewGuid(), Email = "agent@example.test", NormalizedEmail = "AGENT@EXAMPLE.TEST", UserName = "agent@example.test" };
        db.Users.Add(owner);

        var listing = new Listing
        {
            Id = Guid.NewGuid(),
            UserId = owner.Id,
            Address = "42 Latrobe Tce",
            Suburb = "Paddington",
            State = "QLD",
            Postcode = "4064",
            Status = ListingStatus.Active
        };
        db.Listings.Add(listing);
        await db.SaveChangesAsync();

        var svc = new ListingService(db, NullLogger<ListingService>.Instance);

        var ok1 = await svc.AddPublishedListingInquiryAsync(listing.Id, new ListingInquiry
        {
            Message = EnquiryRecordCodec.Encode(EnquiryChannel.CallRequest, "Call me", "Buyer One", "0411111111")
        });
        var ok2 = await svc.AddPublishedListingInquiryAsync(listing.Id, new ListingInquiry
        {
            Message = EnquiryRecordCodec.Encode(EnquiryChannel.SmsText, "Text me", "Buyer Two", "0422222222")
        });

        Assert.True(ok1);
        Assert.True(ok2);

        // Must create only ONE canonical proxy Agent row (never duplicate Email="" rows that violate IX_Agents_Email in Postgres)
        var agents = await db.Set<Agent>().ToListAsync();
        Assert.Single(agents);
        Assert.Equal("buyer-enquiries@unrealestate.au", agents[0].Email);

        var inquiries = await db.Set<ListingInquiry>().ToListAsync();
        Assert.Equal(2, inquiries.Count);
        Assert.All(inquiries, i => Assert.Equal(agents[0].Id, i.AgentId));
    }

    [Fact]
    public async Task Enquiries_AreStrictlyScopedToListingOwner_NoCrossTenantReadOrMutation()
    {
        await using var db = CreateDb(nameof(Enquiries_AreStrictlyScopedToListingOwner_NoCrossTenantReadOrMutation));
        var ownerA = new User { Id = Guid.NewGuid(), Email = "a@example.test", NormalizedEmail = "A@EXAMPLE.TEST", UserName = "a@example.test" };
        var ownerB = new User { Id = Guid.NewGuid(), Email = "b@example.test", NormalizedEmail = "B@EXAMPLE.TEST", UserName = "b@example.test" };
        db.Users.AddRange(ownerA, ownerB);

        var listingA = new Listing { Id = Guid.NewGuid(), UserId = ownerA.Id, Address = "1 Owner A St", Suburb = "Paddington", Status = ListingStatus.Active };
        db.Listings.Add(listingA);
        await db.SaveChangesAsync();

        var svc = new ListingService(db, NullLogger<ListingService>.Instance);
        var inquiry = new ListingInquiry
        {
            Id = Guid.NewGuid(),
            Message = EnquiryRecordCodec.Encode(EnquiryChannel.Offer, "Cash offer", "Private Buyer", "0400000123", offerAmount: 1_250_000m),
            Status = InquiryStatus.New
        };
        Assert.True(await svc.AddPublishedListingInquiryAsync(listingA.Id, inquiry));

        // Owner A sees their own enquiry; Owner B sees ZERO
        Assert.Single(await svc.GetInquiriesForOwnerAsync(ownerA.Id));
        Assert.Empty(await svc.GetInquiriesForOwnerAsync(ownerB.Id));

        // Owner B cannot mutate Owner A's enquiry status
        Assert.False(await svc.UpdateInquiryStatusForOwnerAsync(inquiry.Id, ownerB.Id, InquiryStatus.Responded));
        Assert.True(await svc.UpdateInquiryStatusForOwnerAsync(inquiry.Id, ownerA.Id, InquiryStatus.Responded));
    }

    [Fact]
    public async Task CreateOwnedListingAsync_RejectsUnknownUser_AndClampsColumnLengths()
    {
        await using var db = CreateDb(nameof(CreateOwnedListingAsync_RejectsUnknownUser_AndClampsColumnLengths));
        var owner = new User { Id = Guid.NewGuid(), Email = "owner@example.test", NormalizedEmail = "OWNER@EXAMPLE.TEST", UserName = "owner@example.test" };
        db.Users.Add(owner);
        await db.SaveChangesAsync();

        var svc = new ListingService(db, NullLogger<ListingService>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.CreateOwnedListingAsync(Guid.NewGuid(), new Listing { Address = "Ghost St" }));

        var created = await svc.CreateOwnedListingAsync(owner.Id, new Listing
        {
            Address = "42 Latrobe Tce",
            Suburb = "Paddington",
            State = "QLD",
            Postcode = "4064",
            Headline = new string('H', 500),
            PriceDisplay = new string('$', 250),
            Status = ListingStatus.Active
        });

        Assert.Equal(owner.Id, created.UserId);
        Assert.Equal(200, created.Headline.Length);
        Assert.Equal(100, created.PriceDisplay.Length);
    }
}
