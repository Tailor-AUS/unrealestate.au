using System.Globalization;
using System.Text.RegularExpressions;
using Aigents.Domain.Entities;

namespace Aigents.Domain.Enquiries;

public enum EnquiryChannel
{
    Question,
    CallRequest,
    SmsText,
    VoiceAssistant,
    Inspection,
    Offer
}

public sealed record ParsedEnquiry(
    Guid InquiryId,
    Guid ListingId,
    string ListingAddress,
    string ListingSuburb,
    EnquiryChannel Channel,
    string BuyerName,
    string? BuyerPhone,
    string? BuyerEmail,
    string Summary,
    decimal? OfferAmount,
    string? PreferredSlot,
    InquiryStatus Status,
    DateTime CreatedAt,
    DateTime? RespondedAt);

/// <summary>
/// Backwards-compatible structured codec for <see cref="ListingInquiry.Message"/> so Call, Text (SMS),
/// ElevenLabs Voice AI, Inspection, Offer, and Question enquiries work with zero schema migrations.
/// Sanitizes delimiters (including AU landline parentheses like "(07) ...") to prevent spoofing or parse loss.
/// </summary>
public static partial class EnquiryRecordCodec
{
    public const int MaxMessageLength = 2000;

    private static readonly string[] KnownPrefixes =
        ["CALL:", "SMS:", "VOICE-AI:", "INSPECTION:", "OFFER:", "QUESTION:"];

    public static string Encode(
        EnquiryChannel channel,
        string summary,
        string? buyerName,
        string? buyerPhone = null,
        string? buyerEmail = null,
        string? preferredSlot = null,
        decimal? offerAmount = null)
    {
        var prefix = channel switch
        {
            EnquiryChannel.CallRequest => "CALL",
            EnquiryChannel.SmsText => "SMS",
            EnquiryChannel.VoiceAssistant => "VOICE-AI",
            EnquiryChannel.Inspection => "INSPECTION",
            EnquiryChannel.Offer => "OFFER",
            _ => "QUESTION"
        };

        var cleanSummary = SanitizeBody(summary);
        var cleanName = string.IsNullOrWhiteSpace(buyerName) ? "Prospective buyer" : SanitizeMeta(buyerName, 120);
        var parts = new List<string>();

        if (offerAmount.HasValue && offerAmount.Value > 0)
            parts.Add($"${offerAmount.Value:N0}");
        if (!string.IsNullOrWhiteSpace(preferredSlot))
            parts.Add($"slot: {SanitizeMeta(preferredSlot, 60)}");
        if (!string.IsNullOrWhiteSpace(cleanSummary))
            parts.Add(cleanSummary);

        var contactParts = new List<string> { $"from {cleanName}" };
        var cleanPhone = SanitizeMeta(buyerPhone, 32);
        if (!string.IsNullOrWhiteSpace(cleanPhone))
            contactParts.Add($"phone: {cleanPhone}");
        var cleanEmail = SanitizeMeta(buyerEmail, 160);
        if (!string.IsNullOrWhiteSpace(cleanEmail))
            contactParts.Add($"email: {cleanEmail}");

        var metaBlock = $" ({string.Join(" · ", contactParts)})";
        var body = parts.Count > 0 ? string.Join(" — ", parts) : "Buyer enquiry";

        var maxBodyLen = Math.Max(40, MaxMessageLength - prefix.Length - 2 - metaBlock.Length);
        if (body.Length > maxBodyLen)
            body = body[..maxBodyLen];

        return $"{prefix}: {body}{metaBlock}";
    }

    public static ParsedEnquiry Parse(ListingInquiry inquiry, string listingAddress = "", string listingSuburb = "")
    {
        var raw = inquiry.Message ?? string.Empty;
        var channel = DetectChannel(raw, inquiry.Status);

        var body = StripKnownPrefix(raw);

        string buyerName = !string.IsNullOrWhiteSpace(inquiry.Agent?.Name)
            && !string.Equals(inquiry.Agent.Name, "Direct Buyer Enquiry", StringComparison.OrdinalIgnoreCase)
            ? inquiry.Agent.Name
            : "Prospective buyer";
        string? phone = null;
        string? email = null;

        var metaMatch = TrailingMetaRegex().Match(body);
        if (metaMatch.Success)
        {
            var meta = metaMatch.Groups[1].Value;
            body = body[..metaMatch.Index].Trim();

            var segments = meta.Split('·', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            foreach (var seg in segments)
            {
                if (seg.StartsWith("from ", StringComparison.OrdinalIgnoreCase))
                    buyerName = seg[5..].Trim();
                else if (seg.StartsWith("by ", StringComparison.OrdinalIgnoreCase))
                    buyerName = seg[3..].Trim();
                else if (seg.StartsWith("phone:", StringComparison.OrdinalIgnoreCase))
                    phone = seg[6..].Trim();
                else if (seg.StartsWith("email:", StringComparison.OrdinalIgnoreCase))
                    email = seg[6..].Trim();
                else if (seg.Contains('@'))
                    email ??= seg.Trim();
                else if (AuPhoneRegex().IsMatch(seg))
                    phone ??= seg.Trim();
            }
        }

        decimal? offerAmount = null;
        var moneyMatch = MoneyRegex().Match(body);
        if (channel == EnquiryChannel.Offer && moneyMatch.Success)
        {
            var digits = moneyMatch.Groups[1].Value.Replace(",", "");
            if (decimal.TryParse(digits, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
                offerAmount = parsed;
        }

        string? slot = null;
        var slotMatch = SlotRegex().Match(body);
        if (slotMatch.Success)
            slot = slotMatch.Groups[1].Value.Trim();

        return new ParsedEnquiry(
            InquiryId: inquiry.Id,
            ListingId: inquiry.ListingId,
            ListingAddress: listingAddress,
            ListingSuburb: listingSuburb,
            Channel: channel,
            BuyerName: string.IsNullOrWhiteSpace(buyerName) ? "Prospective buyer" : buyerName,
            BuyerPhone: phone,
            BuyerEmail: email,
            Summary: string.IsNullOrWhiteSpace(body) ? raw : body,
            OfferAmount: offerAmount,
            PreferredSlot: slot,
            Status: inquiry.Status,
            CreatedAt: inquiry.CreatedAt,
            RespondedAt: inquiry.RespondedAt);
    }

    public static string? BuildTelHref(string? phone)
    {
        var normalized = NormalizeDialablePhone(phone);
        return normalized is null ? null : $"tel:{normalized}";
    }

    public static string? BuildSmsHref(string? phone, string? prefillBody = null)
    {
        var normalized = NormalizeDialablePhone(phone);
        if (normalized is null) return null;
        if (string.IsNullOrWhiteSpace(prefillBody))
            return $"sms:{normalized}";
        return $"sms:{normalized}?body={Uri.EscapeDataString(prefillBody.Trim())}";
    }

    public static string? NormalizeDialablePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;
        var trimmed = phone.Trim();
        var hasLeadingPlus = trimmed.StartsWith('+');
        var digits = new string(trimmed.Where(char.IsAsciiDigit).ToArray());

        if (digits.Length < 8 || digits.Length > 15) return null;
        if (!hasLeadingPlus && digits.StartsWith("04", StringComparison.Ordinal) && digits.Length == 10)
            return "+61" + digits[1..];
        if (!hasLeadingPlus && digits.Length == 10 && digits[0] == '0' && digits[1] is '2' or '3' or '7' or '8')
            return "+61" + digits[1..];

        return hasLeadingPlus ? "+" + digits : digits;
    }

    private static string StripKnownPrefix(string raw)
    {
        foreach (var prefix in KnownPrefixes)
        {
            if (raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return raw[prefix.Length..].Trim();
        }
        return raw;
    }

    private static EnquiryChannel DetectChannel(string raw, InquiryStatus status)
    {
        if (raw.StartsWith("CALL:", StringComparison.OrdinalIgnoreCase)) return EnquiryChannel.CallRequest;
        if (raw.StartsWith("SMS:", StringComparison.OrdinalIgnoreCase)) return EnquiryChannel.SmsText;
        if (raw.StartsWith("VOICE-AI:", StringComparison.OrdinalIgnoreCase)) return EnquiryChannel.VoiceAssistant;
        if (raw.StartsWith("INSPECTION:", StringComparison.OrdinalIgnoreCase) || status == InquiryStatus.InspectionBooked)
            return EnquiryChannel.Inspection;
        if (raw.StartsWith("OFFER:", StringComparison.OrdinalIgnoreCase) || status == InquiryStatus.OfferMade)
            return EnquiryChannel.Offer;
        return EnquiryChannel.Question;
    }

    private static string SanitizeBody(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";
        return input.Replace('\r', ' ').Replace('\n', ' ').Trim();
    }

    private static string SanitizeMeta(string? input, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";
        // Strip metadata delimiters ('·', '(', ')') so AU landlines like "(07) 3123 4567" stay parseable
        // and user input cannot spoof additional metadata segments.
        var cleaned = input
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('·', '-')
            .Replace("(", "")
            .Replace(")", " ");
        cleaned = MultiSpaceRegex().Replace(cleaned, " ").Trim();
        return cleaned.Length > maxLen ? cleaned[..maxLen] : cleaned;
    }

    [GeneratedRegex(@"\(([^()]+)\)\s*$", RegexOptions.None, matchTimeoutMilliseconds: 500)]
    private static partial Regex TrailingMetaRegex();

    [GeneratedRegex(@"\$([0-9][0-9,]*)", RegexOptions.None, matchTimeoutMilliseconds: 500)]
    private static partial Regex MoneyRegex();

    [GeneratedRegex(@"(?:slot:\s*|requested\s+)([^—(]+)", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 500)]
    private static partial Regex SlotRegex();

    [GeneratedRegex(@"^(?:\+?61|0)[23478][\d\s\-]{7,12}$", RegexOptions.None, matchTimeoutMilliseconds: 500)]
    private static partial Regex AuPhoneRegex();

    [GeneratedRegex(@"\s+", RegexOptions.None, matchTimeoutMilliseconds: 500)]
    private static partial Regex MultiSpaceRegex();
}
