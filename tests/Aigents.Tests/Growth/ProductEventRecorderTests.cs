using Aigents.Domain.Entities;
using Aigents.Infrastructure.Data;
using Aigents.Infrastructure.Growth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aigents.Tests.Growth;

public sealed class ProductEventRecorderTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 22, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetGrowthMetricsAsync_CountsDistinctUsersInsideRollingWindows()
    {
        await using var db = CreateContext();
        var users = Enumerable.Range(0, 3)
            .Select(_ => new User { Name = "Test user" })
            .ToArray();
        db.Users.AddRange(users);
        db.ProductEvents.AddRange(
            Event(users[0], ProductEventNames.SearchPerformed, Now.AddDays(-1)),
            Event(users[0], ProductEventNames.ListingViewed, Now.AddDays(-2)),
            Event(users[1], ProductEventNames.EnquirySubmitted, Now.AddDays(-29)),
            Event(users[2], ProductEventNames.OfferSubmitted, Now.AddDays(-31)));
        await db.SaveChangesAsync();

        var recorder = CreateRecorder(db);
        var metrics = await recorder.GetGrowthMetricsAsync();

        Assert.Equal(2, metrics.MonthlyActiveUsers);
        Assert.Equal(1, metrics.WeeklyActiveUsers);
        Assert.Equal(Now.UtcDateTime, metrics.CalculatedAt);
    }

    [Fact]
    public async Task RecordAsync_DeduplicatesWithinWindow_AndRefreshesUserActivity()
    {
        await using var db = CreateContext();
        var user = new User { Name = "Test user" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var clock = new ManualTimeProvider(Now);
        var recorder = CreateRecorder(db, clock);

        var first = await recorder.RecordAsync(
            user.Id,
            ProductEventNames.SearchPerformed,
            deduplicationWindow: TimeSpan.FromMinutes(5));
        clock.Advance(TimeSpan.FromMinutes(1));
        var duplicate = await recorder.RecordAsync(
            user.Id,
            ProductEventNames.SearchPerformed,
            deduplicationWindow: TimeSpan.FromMinutes(5));
        clock.Advance(TimeSpan.FromMinutes(5));
        var afterWindow = await recorder.RecordAsync(
            user.Id,
            ProductEventNames.SearchPerformed,
            deduplicationWindow: TimeSpan.FromMinutes(5));

        Assert.True(first);
        Assert.False(duplicate);
        Assert.True(afterWindow);
        Assert.Equal(2, await db.ProductEvents.CountAsync());
        await db.Entry(user).ReloadAsync();
        Assert.Equal(clock.GetUtcNow().UtcDateTime, user.LastActiveAt);
    }

    [Fact]
    public async Task RecordAsync_ReturnsFalse_WhenTelemetryStoreFails()
    {
        var options = new DbContextOptionsBuilder<AigentsDbContext>().Options;
        var recorder = CreateRecorder(options);

        var recorded = await recorder.RecordAsync(
            Guid.NewGuid(),
            ProductEventNames.SearchPerformed);

        Assert.False(recorded);
    }

    [Fact]
    public async Task GetListingActivityAsync_CountsDistinctViewers_AndBuyerEnquiriesOnly()
    {
        await using var db = CreateContext();
        var firstUser = new User { Name = "First user" };
        var secondUser = new User { Name = "Second user" };
        var listing = new Listing
        {
            User = firstUser,
            UserId = firstUser.Id,
            Address = "1 Test Street",
            Status = ListingStatus.Active
        };
        db.AddRange(firstUser, secondUser, listing);
        db.ProductEvents.AddRange(
            Event(
                firstUser,
                ProductEventNames.ListingViewed,
                Now.AddHours(-1),
                listing.Id),
            Event(
                firstUser,
                ProductEventNames.ListingViewed,
                Now.AddHours(-2),
                listing.Id),
            Event(
                secondUser,
                ProductEventNames.ListingViewed,
                Now.AddHours(-3),
                listing.Id));
        db.ListingInquiries.AddRange(
            Inquiry(listing, BuyerInquiryType.Question, Now.AddHours(-4)),
            Inquiry(listing, BuyerInquiryType.Inspection, Now.AddHours(-2)),
            Inquiry(listing, BuyerInquiryType.Offer, Now.AddHours(-1)),
            Inquiry(listing, BuyerInquiryType.Agent, Now.AddMinutes(-30)),
            Inquiry(listing, BuyerInquiryType.Question, Now.AddHours(-25)));
        await db.SaveChangesAsync();

        var recorder = CreateRecorder(db);
        var activity = await recorder.GetListingActivityAsync(listing.Id);

        Assert.Equal(2, activity.AuthenticatedViewsLast24Hours);
        Assert.Equal(2, activity.EnquiriesLast24Hours);
        Assert.Equal(1, activity.OffersTotal);
        Assert.Equal(Now.AddHours(-2).UtcDateTime, activity.LastEnquiryAt);
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

    private static ProductEventRecorder CreateRecorder(
        AigentsDbContext db,
        TimeProvider? clock = null)
    {
        var options = (DbContextOptions<AigentsDbContext>)
            db.GetService<IDbContextOptions>();
        return CreateRecorder(options, clock);
    }

    private static ProductEventRecorder CreateRecorder(
        DbContextOptions<AigentsDbContext> options,
        TimeProvider? clock = null)
    {
        return new ProductEventRecorder(
            options,
            clock ?? new ManualTimeProvider(Now),
            NullLogger<ProductEventRecorder>.Instance);
    }

    private static ProductEvent Event(
        User user,
        string name,
        DateTimeOffset occurredAt,
        Guid? listingId = null)
    {
        return new ProductEvent
        {
            User = user,
            UserId = user.Id,
            Name = name,
            ListingId = listingId,
            OccurredAt = occurredAt.UtcDateTime
        };
    }

    private static ListingInquiry Inquiry(
        Listing listing,
        BuyerInquiryType type,
        DateTimeOffset createdAt)
    {
        return new ListingInquiry
        {
            Listing = listing,
            ListingId = listing.Id,
            InquiryType = type,
            Message = "Test",
            CreatedAt = createdAt.UtcDateTime
        };
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration)
        {
            _utcNow += duration;
        }
    }
}
