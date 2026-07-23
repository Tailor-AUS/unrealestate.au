using Aigents.Domain.Entities;
using Aigents.Infrastructure.Data;
using Aigents.Infrastructure.Growth;
using Aigents.Infrastructure.Services.AI;
using Aigents.Infrastructure.Services.Chat;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aigents.Tests.Chat;

public sealed class ChatConversationServiceTests
{
    [Fact]
    public async Task SendAsync_PersistsConversationMessagesAndGrowthEvent()
    {
        await using var db = CreateContext();
        var user = new User { Name = "Test user" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var service = CreateService(db, new StubAiService());

        var reply = await service.SendAsync(
            user.Id,
            conversationId: null,
            [new ConversationInputMessage("user", "Find me a home")],
            "buy");

        Assert.NotEqual(Guid.Empty, reply.ConversationId);
        Assert.Equal("AI reply", reply.Content);
        Assert.Equal(2, await db.Messages.CountAsync());
        Assert.Single(await db.Conversations.ToListAsync());
        var productEvent = Assert.Single(await db.ProductEvents.ToListAsync());
        Assert.Equal(ProductEventNames.AiChatStarted, productEvent.Name);
        Assert.Equal(user.Id, productEvent.UserId);
    }

    [Fact]
    public async Task SendAsync_DoesNotOpenAnotherUsersConversation()
    {
        await using var db = CreateContext();
        var owner = new User { Name = "Owner" };
        var otherUser = new User { Name = "Other user" };
        var conversation = new Conversation
        {
            User = owner,
            UserId = owner.Id,
            Mode = AgentMode.Buy
        };
        db.AddRange(owner, otherUser, conversation);
        await db.SaveChangesAsync();
        var service = CreateService(db, new StubAiService());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SendAsync(
                otherUser.Id,
                conversation.Id,
                [new ConversationInputMessage("user", "Private conversation")],
                "buy"));
    }

    [Fact]
    public async Task SendAsync_DoesNotChangeModeInsideConversation()
    {
        await using var db = CreateContext();
        var user = new User { Name = "Owner" };
        var conversation = new Conversation
        {
            User = user,
            UserId = user.Id,
            Mode = AgentMode.Buy
        };
        db.AddRange(user, conversation);
        await db.SaveChangesAsync();
        var service = CreateService(db, new StubAiService());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SendAsync(
                user.Id,
                conversation.Id,
                [new ConversationInputMessage("user", "Help me sell")],
                "sell"));

        Assert.Empty(await db.Messages.ToListAsync());
    }

    [Fact]
    public async Task SendAsync_DoesNotPersistUserMessageWhenAiFails()
    {
        await using var db = CreateContext();
        var user = new User { Name = "Test user" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var service = CreateService(db, new StubAiService(shouldFail: true));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SendAsync(
                user.Id,
                conversationId: null,
                [new ConversationInputMessage("user", "Try once")],
                "sell"));

        Assert.Empty(await db.Conversations.ToListAsync());
        Assert.Empty(await db.Messages.ToListAsync());
        Assert.Empty(await db.ProductEvents.ToListAsync());
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

    private static ChatConversationService CreateService(
        AigentsDbContext db,
        IAiService aiService)
    {
        var options = (DbContextOptions<AigentsDbContext>)
            db.GetService<IDbContextOptions>();
        var productEvents = new ProductEventRecorder(
            options,
            TimeProvider.System,
            NullLogger<ProductEventRecorder>.Instance);
        return new ChatConversationService(
            db,
            aiService,
            productEvents,
            NullLogger<ChatConversationService>.Instance);
    }

    private sealed class StubAiService(bool shouldFail = false) : IAiService
    {
        public Task<AiResponse> ChatAsync(
            IEnumerable<Aigents.Infrastructure.Services.AI.ChatMessage> messages,
            string mode,
            CancellationToken ct = default)
        {
            return shouldFail
                ? Task.FromException<AiResponse>(
                    new InvalidOperationException("Synthetic AI failure"))
                : Task.FromResult(new AiResponse
                {
                    Content = "AI reply",
                    TokensUsed = 12,
                    Model = "stub"
                });
        }

        public Task<PropertyIntelligenceResponse> SearchPropertyIntelligenceAsync(
            string address,
            CancellationToken ct = default) =>
            Task.FromResult(new PropertyIntelligenceResponse());
    }
}
