using Aigents.Domain.Entities;
using Aigents.Infrastructure.Data;
using Aigents.Infrastructure.Growth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;

namespace Aigents.Tests.Growth;

public sealed class ProductEventRecorderPostgreSqlTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task RecordAsync_ConcurrentDuplicates_CreateOnePostgreSqlRow()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine")
            .Build();
        await postgres.StartAsync();

        var options = new DbContextOptionsBuilder<AigentsDbContext>()
            .UseNpgsql(
                postgres.GetConnectionString(),
                npgsql => npgsql.EnableRetryOnFailure())
            .Options;

        var user = new User { Name = "Concurrent test user" };
        await using (var setup = new AigentsDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
            setup.Users.Add(user);
            await setup.SaveChangesAsync();
        }

        var recorder = new ProductEventRecorder(
            options,
            TimeProvider.System,
            NullLogger<ProductEventRecorder>.Instance);
        var starts = Enumerable.Range(0, 12)
            .Select(_ => recorder.RecordAsync(
                user.Id,
                ProductEventNames.SearchPerformed,
                deduplicationWindow: TimeSpan.FromMinutes(5)));

        var results = await Task.WhenAll(starts);

        Assert.Single(results, recorded => recorded);
        await using var verification = new AigentsDbContext(options);
        Assert.Equal(
            1,
            await verification.ProductEvents.CountAsync(productEvent =>
                productEvent.UserId == user.Id
                && productEvent.Name == ProductEventNames.SearchPerformed));
    }
}
