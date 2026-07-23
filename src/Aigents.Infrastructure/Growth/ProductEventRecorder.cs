using Aigents.Domain.Entities;
using Aigents.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Buffers.Binary;
using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;

namespace Aigents.Infrastructure.Growth;

public interface IProductEventRecorder
{
    Task<bool> RecordAsync(
        Guid userId,
        string eventName,
        Guid? listingId = null,
        TimeSpan? deduplicationWindow = null,
        CancellationToken cancellationToken = default);

    Task<GrowthMetrics> GetGrowthMetricsAsync(
        CancellationToken cancellationToken = default);

    Task<ListingActivity> GetListingActivityAsync(
        Guid listingId,
        CancellationToken cancellationToken = default);
}

public sealed record GrowthMetrics(
    int MonthlyActiveUsers,
    int WeeklyActiveUsers,
    DateTime CalculatedAt);

public sealed record ListingActivity(
    int AuthenticatedViewsLast24Hours,
    int EnquiriesLast24Hours,
    int OffersTotal,
    DateTime? LastEnquiryAt);

/// <summary>
/// Writes privacy-minimised product events and maintains User.LastActiveAt.
/// "Active" means an authenticated user performed at least one event from the
/// stable ProductEventNames vocabulary.
/// </summary>
public sealed class ProductEventRecorder : IProductEventRecorder
{
    private static readonly Meter GrowthMeter = new("Aigents.Growth");
    private static readonly Counter<long> RecordedEvents =
        GrowthMeter.CreateCounter<long>("aigents.product_events.recorded");

    private readonly DbContextOptions<AigentsDbContext> _dbOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ProductEventRecorder> _logger;

    public ProductEventRecorder(
        DbContextOptions<AigentsDbContext> dbOptions,
        TimeProvider timeProvider,
        ILogger<ProductEventRecorder> logger)
    {
        _dbOptions = dbOptions;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<bool> RecordAsync(
        Guid userId,
        string eventName,
        Guid? listingId = null,
        TimeSpan? deduplicationWindow = null,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            return false;
        }

        if (!ProductEventNames.All.Contains(eventName))
        {
            _logger.LogError(
                "Skipped unknown product event {EventName}; use ProductEventNames",
                eventName);
            return false;
        }

        try
        {
            await using var db = new AigentsDbContext(_dbOptions);
            var executionStrategy = db.Database.CreateExecutionStrategy();
            return await executionStrategy.ExecuteAsync(async () =>
            {
                // A transaction-scoped PostgreSQL advisory lock serializes the
                // same deduplication predicate across app replicas without noisy
                // serialization failures.
                db.ChangeTracker.Clear();
                await using var transaction =
                    deduplicationWindow is not null && db.Database.IsRelational()
                        ? await db.Database.BeginTransactionAsync(cancellationToken)
                        : null;
                if (transaction is not null)
                {
                    var lockKey = GetDeduplicationLockKey(
                        userId,
                        eventName,
                        listingId);
                    await db.Database.ExecuteSqlInterpolatedAsync(
                        $"SELECT pg_advisory_xact_lock({lockKey})",
                        cancellationToken);
                }

                var user = await db.Users
                    .FirstOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);
                if (user is null)
                {
                    _logger.LogWarning(
                        "Skipped product event {EventName}: user {UserId} does not exist",
                        eventName,
                        userId);
                    return false;
                }

                var now = _timeProvider.GetUtcNow().UtcDateTime;
                if (deduplicationWindow is { } window)
                {
                    var cutoff = now - window;
                    var duplicateExists = await db.ProductEvents
                        .AnyAsync(
                            productEvent =>
                                productEvent.UserId == userId
                                && productEvent.Name == eventName
                                && productEvent.ListingId == listingId
                                && productEvent.OccurredAt >= cutoff,
                            cancellationToken);

                    if (duplicateExists)
                    {
                        return false;
                    }
                }

                db.ProductEvents.Add(new ProductEvent
                {
                    UserId = userId,
                    Name = eventName,
                    ListingId = listingId,
                    OccurredAt = now
                });

                user.LastActiveAt = now;
                user.UpdatedAt = now;
                await db.SaveChangesAsync(cancellationToken);
                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }
                RecordedEvents.Add(
                    1,
                    new KeyValuePair<string, object?>("event.name", eventName));

                _logger.LogInformation(
                    "Recorded product event {EventName} for user {UserId}",
                    eventName,
                    userId);
                return true;
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Product analytics is best-effort. A missing migration or transient
            // telemetry write must never break listing, search, chat, or enquiry
            // UX. Callers save their primary transaction before recording.
            _logger.LogError(
                ex,
                "Failed to record product event {EventName}; primary flow continues",
                eventName);
            return false;
        }
    }

    public async Task<GrowthMetrics> GetGrowthMetricsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = new AigentsDbContext(_dbOptions);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var monthlyCutoff = now.AddDays(-30);
        var weeklyCutoff = now.AddDays(-7);

        var monthlyActiveUsers = await db.ProductEvents
            .AsNoTracking()
            .Where(productEvent => productEvent.OccurredAt >= monthlyCutoff)
            .Select(productEvent => productEvent.UserId)
            .Distinct()
            .CountAsync(cancellationToken);

        var weeklyActiveUsers = await db.ProductEvents
            .AsNoTracking()
            .Where(productEvent => productEvent.OccurredAt >= weeklyCutoff)
            .Select(productEvent => productEvent.UserId)
            .Distinct()
            .CountAsync(cancellationToken);

        return new GrowthMetrics(monthlyActiveUsers, weeklyActiveUsers, now);
    }

    public async Task<ListingActivity> GetListingActivityAsync(
        Guid listingId,
        CancellationToken cancellationToken = default)
    {
        await using var db = new AigentsDbContext(_dbOptions);
        var last24Hours = _timeProvider.GetUtcNow().UtcDateTime.AddHours(-24);

        var authenticatedViews = await db.ProductEvents
            .AsNoTracking()
            .Where(
                productEvent =>
                    productEvent.ListingId == listingId
                    && productEvent.Name == ProductEventNames.ListingViewed
                    && productEvent.OccurredAt >= last24Hours)
            .Select(productEvent => productEvent.UserId)
            .Distinct()
            .CountAsync(cancellationToken);

        var enquiries = db.ListingInquiries
            .AsNoTracking()
            .Where(inquiry => inquiry.ListingId == listingId);

        var buyerEnquiries = enquiries.Where(inquiry =>
            inquiry.InquiryType == BuyerInquiryType.Question
            || inquiry.InquiryType == BuyerInquiryType.Inspection);

        var enquiriesLast24Hours = await buyerEnquiries
            .CountAsync(inquiry => inquiry.CreatedAt >= last24Hours, cancellationToken);
        var offersTotal = await enquiries
            .CountAsync(
                inquiry => inquiry.InquiryType == BuyerInquiryType.Offer,
                cancellationToken);
        var lastEnquiryAt = await buyerEnquiries
            .OrderByDescending(inquiry => inquiry.CreatedAt)
            .Select(inquiry => (DateTime?)inquiry.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return new ListingActivity(
            authenticatedViews,
            enquiriesLast24Hours,
            offersTotal,
            lastEnquiryAt);
    }

    private static long GetDeduplicationLockKey(
        Guid userId,
        string eventName,
        Guid? listingId)
    {
        var value =
            $"{userId:N}|{eventName}|{listingId?.ToString("N") ?? "-"}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return BinaryPrimitives.ReadInt64LittleEndian(hash);
    }
}
