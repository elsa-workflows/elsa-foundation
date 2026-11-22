using System.Text;

namespace Kernel.Durable;

internal static class FoundationMetadataLimits
{
    internal const int MaxTurnSends = 10_000;
    internal const int NameBytes = 1_024;
    internal const int KindBytes = 256;
    internal const int TargetBytes = NameBytes + 70;
    internal const int SenderBytes = NameBytes + 160;
    internal const int TypeDataBytes = 16 * 1_024;
    internal const int MessageContentBytes = 16 * 1_024 * 1_024 - 2 * 24 * 1_024;
    internal const int StartContentBytes = MessageContentBytes - (TypeDataBytes - NameBytes);
    internal const int EventStartInputBytes = 64 * 1_024;
    internal const int EventContentBytes = MessageContentBytes - 40 * 1_024 - EventStartInputBytes;

    // The default UTF-8 fallback counts an unpaired UTF-16 code unit as three bytes. The permitted
    // key paths preserve the original code units separately; ordinary text has to round-trip.
    internal static bool FitsName(string value, bool preservesCodeUnits = false) =>
        Fits(value, NameBytes, preservesCodeUnits);

    internal static bool FitsKind(string value) => Fits(value, KindBytes, preservesCodeUnits: false);
    internal static bool FitsTarget(string value) => Fits(value, TargetBytes, preservesCodeUnits: false);
    internal static bool FitsSender(string? value) => FitsOptional(value, SenderBytes);

    internal static void RequireKind(string value, string parameter)
    {
        if (!FitsKind(value)) throw new MetadataLimitExceeded(parameter, KindBytes);
    }

    internal static bool FitsContent(string? value, int limit = MessageContentBytes) =>
        value is null || value.Length <= limit && Encoding.UTF8.GetByteCount(value) <= limit;

    internal static void RequireContent(string? value, string parameter, int limit = MessageContentBytes)
    {
        if (!FitsContent(value, limit)) throw new MetadataLimitExceeded(parameter, limit);
    }

    internal static bool FitsComposed(Message message)
    {
        var (id, scope, match) = message.InboxKey;
        var issued = message.InboxKey.HasIssuedOrigin;
        const int issuedIdBytes = 2 * NameBytes + 160;
        return FitsKind(message.Kind) &&
            Fits(message.Id, scope == 0 ? NameBytes : issuedIdBytes, issued) && Fits(id, issuedIdBytes, issued) &&
            FitsTarget(message.Target.Id) && FitsSender(message.Sender) && FitsOptional(message.ReplyTo, SenderBytes) &&
            FitsOptional(match, 64) && FitsComposedContent(message);
    }

    private static bool FitsComposedContent(Message message)
    {
        if (message.Start is not { } start) return !message.StoreStartInputOnce && FitsContent(message.Payload);

        if (!FitsName(start.Model) || !FitsContent(start.Definition, TypeDataBytes) || !FitsOptional(start.Parent, TargetBytes) ||
            !FitsOptional(start.Creation?.Intent, NameBytes) || !FitsContent(start.Input, StartContentBytes))
            return false;

        if (message.StoreStartInputOnce) return message.Payload == start.Input;
        var remaining = StartContentBytes - (start.Input is { } input ? Encoding.UTF8.GetByteCount(input) : 0);
        return FitsContent(message.Payload, remaining);
    }

    private static bool FitsOptional(string? value, int limit) => value is null || Fits(value, limit, preservesCodeUnits: false);

    private static bool Fits(string value, int limit, bool preservesCodeUnits)
    {
        return value.Length <= limit && Encoding.UTF8.GetByteCount(value) <= limit &&
            (preservesCodeUnits || !DurableKeyJson.HasUnpairedSurrogate(value));
    }
}

internal sealed class MetadataLimitExceeded(string parameter, int limit) :
    ArgumentException($"Metadata must be representable in its text encoding and fit in {limit:N0} bytes.", parameter)
{
    internal int LimitBytes { get; } = limit;
}
