using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Aigents.Api.Features.VoiceNotes;

/// <summary>
/// API endpoints for Voice Notes - quick audio capture
/// </summary>
public static class VoiceNoteEndpoints
{
    public static IEndpointRouteBuilder MapVoiceNoteEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/voicenotes")
            .WithTags("Voice Notes")
            .RequireAuthorization();

        // Upload voice note
        group.MapPost("/", UploadVoiceNote)
            .WithName("UploadVoiceNote")
            .WithSummary("Upload a voice note with optional linking")
            .DisableAntiforgery();

        // Get voice notes for agent
        group.MapGet("/", GetVoiceNotes)
            .WithName("GetVoiceNotes")
            .WithSummary("Get agent's voice notes");

        // Get single voice note with transcript
        group.MapGet("/{noteId}", GetVoiceNote)
            .WithName("GetVoiceNote")
            .WithSummary("Get voice note details and transcript");

        // Delete voice note
        group.MapDelete("/{noteId}", DeleteVoiceNote)
            .WithName("DeleteVoiceNote")
            .WithSummary("Delete a voice note");

        // Link voice note to entity
        group.MapPost("/{noteId}/link", LinkVoiceNote)
            .WithName("LinkVoiceNote")
            .WithSummary("Link voice note to a contact, property, or inspection");

        return routes;
    }

    private static async Task<IResult> UploadVoiceNote(
        IFormFile audio,
        string agentId,
        string? contactId = null,
        string? propertyId = null,
        string? inspectionId = null,
        string? context = null,
        double? latitude = null,
        double? longitude = null)
    {
        if (audio == null || audio.Length == 0)
        {
            return Results.BadRequest(new { error = "No audio file provided" });
        }

        var noteId = Guid.NewGuid().ToString();

        // TODO: Upload to Azure Blob Storage
        // TODO: Trigger transcription

        await Task.CompletedTask;

        return Results.Ok(new
        {
            noteId,
            success = true,
            audioUrl = $"/storage/voicenotes/{noteId}.m4a",
            durationSeconds = 0, // Would be extracted from audio
            transcriptionStatus = "pending"
        });
    }

    private static Task<IResult> GetVoiceNotes(
        string agentId,
        string? contactId = null,
        string? propertyId = null,
        int page = 1,
        int pageSize = 20)
    {
        var notes = new List<VoiceNoteListItem>();

        return Task.FromResult(Results.Ok(new
        {
            notes,
            page,
            pageSize,
            totalItems = 0
        }));
    }

    private static Task<IResult> GetVoiceNote(string noteId)
    {
        var note = new VoiceNoteDetail
        {
            Id = noteId,
            AudioUrl = $"/storage/voicenotes/{noteId}.m4a",
            DurationSeconds = 45,
            Transcript = "Sample transcript...",
            TranscriptionStatus = "completed",
            Context = "PostInspection",
            RecordedAt = DateTimeOffset.UtcNow.AddHours(-2)
        };

        return Task.FromResult(Results.Ok(note));
    }

    private static Task<IResult> DeleteVoiceNote(string noteId)
    {
        // TODO: Delete from storage and database
        return Task.FromResult(Results.Ok(new { success = true }));
    }

    private static Task<IResult> LinkVoiceNote(
        string noteId,
        LinkRequest request)
    {
        // TODO: Update links
        return Task.FromResult(Results.Ok(new { success = true }));
    }

    // ═══════════════════════════════════════════════════════════════
    // MODELS
    // ═══════════════════════════════════════════════════════════════

    public record VoiceNoteListItem
    {
        public required string Id { get; init; }
        public required string AudioUrl { get; init; }
        public int DurationSeconds { get; init; }
        public string? TranscriptPreview { get; init; }
        public string? Context { get; init; }
        public string? LinkedContactName { get; init; }
        public string? LinkedPropertyAddress { get; init; }
        public DateTimeOffset RecordedAt { get; init; }
    }

    public record VoiceNoteDetail
    {
        public required string Id { get; init; }
        public required string AudioUrl { get; init; }
        public int DurationSeconds { get; init; }
        public string? Transcript { get; init; }
        public string? TranscriptionStatus { get; init; }
        public string? Context { get; init; }
        public string? ContactId { get; init; }
        public string? PropertyId { get; init; }
        public string? InspectionId { get; init; }
        public double? Latitude { get; init; }
        public double? Longitude { get; init; }
        public DateTimeOffset RecordedAt { get; init; }
    }

    public record LinkRequest(
        string? ContactId = null,
        string? PropertyId = null,
        string? InspectionId = null
    );
}
