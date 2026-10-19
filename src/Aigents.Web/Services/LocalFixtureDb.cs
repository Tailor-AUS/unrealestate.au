using Aigents.Domain.Entities;
using Aigents.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Aigents.Web.Services;

/// <summary>
/// Local-only synthetic data mode for exercising UI journeys without Postgres.
///
/// Enabled only when the host environment is Development AND
/// LOCAL_FIXTURE_DB=inmemory. Every seeded record is labelled [SYNTHETIC]; no
/// real property, person or price is represented, and estimated values are
/// illustrative. Never enabled in Production — the guard throws if asked to.
/// </summary>
public static class LocalFixtureDb
{
    public const string Marker = "[SYNTHETIC]";

    public static bool IsRequested(IConfiguration cfg) =>
        string.Equals(cfg["LOCAL_FIXTURE_DB"], "inmemory", StringComparison.OrdinalIgnoreCase);

    public static bool Use(WebApplicationBuilder builder)
    {
        if (!IsRequested(builder.Configuration)) return false;
        if (!builder.Environment.IsDevelopment())
            throw new InvalidOperationException("LOCAL_FIXTURE_DB=inmemory is only allowed in Development.");
        builder.Services.AddDbContext<AigentsDbContext>(o => o.UseInMemoryDatabase("aigents-local-fixture"));
        return true;
    }

    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AigentsDbContext>();
        if (await db.Listings.AnyAsync()) return;

        var owner = new User
        {
            Id = Guid.NewGuid(),
            UserName = "synthetic-agent@example.test",
            NormalizedUserName = "SYNTHETIC-AGENT@EXAMPLE.TEST",
            Email = "synthetic-agent@example.test",
            NormalizedEmail = "SYNTHETIC-AGENT@EXAMPLE.TEST",
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString(),
        };
        db.Users.Add(owner);

        Listing L(string addr, string suburb, string type, int? beds, int? baths, decimal? asking, decimal? estimate, ListingStatus status = ListingStatus.Active) => new()
        {
            UserId = owner.Id,
            User = owner,
            Address = $"{Marker} {addr}",
            Suburb = suburb,
            Postcode = "4000",
            PropertyType = type,
            Bedrooms = beds,
            Bathrooms = baths,
            AskingPrice = asking,
            EstimatedValue = estimate,
            Headline = $"{Marker} local test listing",
            Description = "Synthetic local fixture. Not a real property.",
            Status = status,
            PublishedAt = status == ListingStatus.Active ? DateTime.UtcNow : null,
        };

        db.Listings.AddRange(
            L("1 Example St", "Paddington", "House", 3, 2, 950_000m, null),
            L("2 Sample Ave", "Paddington", "House", 3, 1, 1_080_000m, null),
            L("3 Fixture Rd", "New Farm", "Unit", 2, 1, 720_000m, null),
            L("4 Placeholder Cres", "Paddington", "House", 4, 2, null, 900_000m),
            L("5 Missing Data Ln", "Paddington", "House", null, null, null, null),
            L("6 Draft Only St", "Paddington", "House", 3, 2, 950_000m, null, ListingStatus.Draft));
        await db.SaveChangesAsync();
    }
}
