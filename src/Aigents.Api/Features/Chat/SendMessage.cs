// ═══════════════════════════════════════════════════════════════
// CHAT FEATURE - VERTICAL SLICE
// ═══════════════════════════════════════════════════════════════
// Contains: Endpoint, Request, Response, Handler, Validator
// All in one file for cohesion.
// ═══════════════════════════════════════════════════════════════

using Aigents.Infrastructure.Services.Chat;
using Carter;
using FluentValidation;
using MediatR;
using System.Security.Claims;

namespace Aigents.Api.Features.Chat;

// ───────────────────────────────────────────────────────────────
// ENDPOINT
// ───────────────────────────────────────────────────────────────

public class SendMessageEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/chat", async (
            SendMessageRequest request,
            ClaimsPrincipal principal,
            ISender sender) =>
        {
            if (!Guid.TryParse(
                    principal.FindFirstValue(ClaimTypes.NameIdentifier),
                    out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await sender.Send(new SendMessageCommand(
                request.ConversationId,
                userId,
                request.Messages,
                request.Mode
            ));

            return Results.Ok(result);
        })
        .WithName("SendMessage")
        .RequireAuthorization()
        .WithOpenApi()
        .Produces<SendMessageResponse>()
        .ProducesValidationProblem();
    }
}

// ───────────────────────────────────────────────────────────────
// REQUEST / RESPONSE
// ───────────────────────────────────────────────────────────────

public record SendMessageRequest(
    Guid? ConversationId,
    List<MessageDto> Messages,
    string Mode
);

public record MessageDto(string Role, string Content);

public record SendMessageResponse(
    Guid ConversationId,
    string Content,
    int TokensUsed
);

// ───────────────────────────────────────────────────────────────
// COMMAND
// ───────────────────────────────────────────────────────────────

public record SendMessageCommand(
    Guid? ConversationId,
    Guid UserId,
    List<MessageDto> Messages,
    string Mode
) : IRequest<SendMessageResponse>;

// ───────────────────────────────────────────────────────────────
// VALIDATOR
// ───────────────────────────────────────────────────────────────

public class SendMessageValidator : AbstractValidator<SendMessageCommand>
{
    public SendMessageValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("UserId is required");

        RuleFor(x => x.Messages)
            .NotEmpty()
            .WithMessage("At least one message is required");

        RuleFor(x => x.Mode)
            .Must(m => m == "buy" || m == "sell")
            .WithMessage("Mode must be 'buy' or 'sell'");
    }
}

// ───────────────────────────────────────────────────────────────
// HANDLER
// ───────────────────────────────────────────────────────────────

public class SendMessageHandler : IRequestHandler<SendMessageCommand, SendMessageResponse>
{
    private readonly IChatConversationService _chat;

    public SendMessageHandler(IChatConversationService chat) => _chat = chat;

    public async Task<SendMessageResponse> Handle(SendMessageCommand request, CancellationToken ct)
    {
        var reply = await _chat.SendAsync(
            request.UserId,
            request.ConversationId,
            request.Messages
                .Select(message => new ConversationInputMessage(
                    message.Role,
                    message.Content))
                .ToList(),
            request.Mode,
            ct);
        return new SendMessageResponse(
            reply.ConversationId,
            reply.Content,
            reply.TokensUsed
        );
    }
}
