using Aigents.Api.Features.Listings;
using Aigents.Domain.Entities;
using Aigents.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aigents.Api.Tests.Listings;

public sealed class ListingAuthorizationTests
{
    [Fact]
    public async Task AgreementHandlers_OnlyReadAndMutateOwnersListing()
    {
        await using var db = CreateContext();
        var owner = new User { Name = "Owner" };
        var otherUser = new User { Name = "Other user" };
        var listing = NewListing(owner);
        db.AddRange(owner, otherUser, listing);
        await db.SaveChangesAsync();

        var getHandler = new GetAgreementHandler(db);
        var signHandler = new SignAgreementHandler(
            db,
            NullLogger<SignAgreementHandler>.Instance);

        Assert.Null(await getHandler.Handle(
            new GetAgreementQuery(listing.Id, otherUser.Id),
            default));
        Assert.Null(await signHandler.Handle(
            new SignAgreementCommand(
                listing.Id,
                otherUser.Id,
                "Other user",
                "Other user",
                true,
                2m),
            default));
        Assert.False(listing.AgreementSigned);

        var response = await signHandler.Handle(
            new SignAgreementCommand(
                listing.Id,
                owner.Id,
                "Owner",
                "Owner",
                true,
                2m),
            default);

        Assert.NotNull(response);
        Assert.True(listing.AgreementSigned);
    }

    [Fact]
    public async Task PublishHandler_OnlyPublishesOwnersListing_WithoutFakeAgents()
    {
        await using var db = CreateContext();
        var owner = new User { Name = "Owner" };
        var otherUser = new User { Name = "Other user" };
        var listing = NewListing(owner);
        listing.AgreementSigned = true;
        listing.Status = ListingStatus.PendingSignature;
        db.AddRange(owner, otherUser, listing);
        await db.SaveChangesAsync();
        var handler = new PublishListingHandler(
            db,
            NullLogger<PublishListingHandler>.Instance);

        Assert.Null(await handler.Handle(
            new PublishListingCommand(listing.Id, otherUser.Id),
            default));
        Assert.Equal(ListingStatus.PendingSignature, listing.Status);

        var response = await handler.Handle(
            new PublishListingCommand(listing.Id, owner.Id),
            default);

        Assert.NotNull(response);
        Assert.Equal(ListingStatus.Active, listing.Status);
        Assert.Equal(0, response.AgentsNotified);
        Assert.Empty(response.Agents);
        Assert.Empty(await db.Agents.ToListAsync());
        Assert.Empty(await db.ListingDistributions.ToListAsync());
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

    private static Listing NewListing(User owner) => new()
    {
        User = owner,
        UserId = owner.Id,
        Address = "1 Test Street",
        Suburb = "Brisbane",
        State = "QLD",
        Postcode = "4000"
    };
}
