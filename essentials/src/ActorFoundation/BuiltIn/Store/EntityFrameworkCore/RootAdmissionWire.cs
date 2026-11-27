using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.EntityFramework;

internal static class RootAdmissionWire
{
    private static readonly JsonSerializerOptions OperatorJson = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false
    };

    internal static Contract.OperatorCommand? ReadOperatorCommand(string payload)
    {
        try { return JsonSerializer.Deserialize<Contract.OperatorCommand>(payload, OperatorJson); }
        catch (JsonException) { return null; }
    }

    internal const string CreatorPrefix = "!kernel-root/1:";
    internal const string DirectPrefix = "!kernel-direct/1:";
    internal const string HeldDirectPrefix = "!kernel-held-direct/1:";
    internal const string AuthoredEventDirectPrefix = "!kernel-authored-event/1:";
    internal const string PublicEventDirectPrefix = "!kernel-public-event/1:";
    internal const string HeldPublicEventDirectPrefix = "!kernel-held-public-event/1:";
    internal const string KeyedDirectPrefix = "!kernel-keyed-direct/1:";
    internal const string HeldKeyedDirectPrefix = "!kernel-held-keyed-direct/1:";

    internal static string AuthoredEventOccurrence(string reference, string id, string wait) =>
        $"{reference}/{FoundationRootCreator.OperationDigest(id)}/{FoundationRootCreator.OperationDigest(wait)}";

    internal static string PublicEventOccurrence(string reference, string publication, string id, string wait) =>
        AuthoredEventOccurrence(reference, DurableKeyJson.Write(new[] { publication, id }), wait);

    internal sealed record StoredRoot(string Id, StartSpec Spec, bool EscapedInput, bool ExplicitZeroDue = false);
    internal sealed record StoredEventWait(string Key, string Wait);
    internal sealed record StoredScheduleControl(string Tenant, string ActorType, string Definition, string Key, long Revision,
        Contract.ScheduleControlKind Kind, string Fingerprint);
    internal sealed record StoredDirect(string Operation, long Creation, bool EscapedPayload, bool ExplicitZeroDue, string? EscapedKind = null)
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public StoredEventWait? EventWait { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? EventOccurrence { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public StoredScheduleControl? ScheduleControl { get; init; }
    }

    internal static void RequireCreator(string operation, StartSpec spec)
    {
        if (!FoundationMetadataLimits.IsOutsideMessageId(operation) || spec is { Parent: not null } or { Creation: not null } ||
            spec.ParentCreation != 0 || !FoundationMetadataLimits.FitsName(spec.Model))
            throw new ArgumentException("Invalid root creator metadata.");
        FoundationMetadataLimits.RequireContent(spec.Definition, nameof(spec), FoundationMetadataLimits.TypeDataBytes);
        FoundationMetadataLimits.RequireContent(spec.Input, nameof(spec), FoundationMetadataLimits.StartContentBytes);
    }

    internal static TimeSpan ReceiptWindow(TimeSpan deduplicationWindow, TimeSpan? keepFor)
    {
        var window = keepFor is { } requested && requested < deduplicationWindow ? requested : deduplicationWindow;
        ArgumentOutOfRangeException.ThrowIfLessThan(window, TimeSpan.Zero);
        return window;
    }

    internal static (string Header, string? Payload) EncodeCreator(Message creator)
    {
        // Keep the input raw when it can travel as text. The reversible envelope also carries every UTF-16 code unit.
        var spec = creator.Start!;
        var escaped = RequiresEnvelope(spec.Input);
        var storedSpec = escaped ? spec : spec with { Input = null };
        return (CreatorPrefix + DurableKeyJson.Write(new StoredRoot(creator.Id, storedSpec, escaped, creator.Due == 0)),
            escaped ? null : spec.Input);
    }

    internal static (string Header, string? Payload, string Kind) EncodeDirect(string id, long creation, string kind, string? payload,
        long? due, bool held, StoredEventWait? eventWait = null, StoredScheduleControl? scheduleControl = null,
        bool authoredEvent = false, string? eventOccurrence = null, bool keyed = false) =>
        EncodeDirect(id, creation, kind, EncodePayload(payload), due, held, eventWait, scheduleControl, authoredEvent, eventOccurrence, keyed);

    internal static (string Header, string? Payload, string Kind) EncodeDirect(string id, long creation, string kind,
        (string? Text, bool Escaped) stored, long? due, bool held, StoredEventWait? eventWait = null,
        StoredScheduleControl? scheduleControl = null, bool authoredEvent = false, string? eventOccurrence = null, bool keyed = false)
    {
        var (storedPayload, escaped) = stored;
        var escapedKind = kind.Contains('\0') ? kind : null;
        var prefix = (eventOccurrence is not null, authoredEvent, keyed, held) switch
        {
            (true, _, _, true) => HeldPublicEventDirectPrefix,
            (true, _, _, false) => PublicEventDirectPrefix,
            (_, true, _, _) => AuthoredEventDirectPrefix,
            (_, _, true, true) => HeldKeyedDirectPrefix,
            (_, _, true, false) => KeyedDirectPrefix,
            (_, _, false, true) => HeldDirectPrefix,
            _ => DirectPrefix
        };
        var header = new StoredDirect(id, creation, escaped, due == 0, escapedKind)
            { EventWait = eventWait, ScheduleControl = scheduleControl, EventOccurrence = eventOccurrence };
        return (prefix + DurableKeyJson.Write(header), storedPayload, escapedKind is null ? kind : "input");
    }

    internal static bool FitsEnvelope(string header, string? payload, string? replyTo = null) =>
        Encoding.UTF8.GetByteCount(header) + (payload is null ? 0L : Encoding.UTF8.GetByteCount(payload)) +
        (replyTo is null ? 0 : Encoding.UTF8.GetByteCount(replyTo)) <= EfStore.RootContentBytes;

    internal static (string? Text, bool Escaped) EncodePayload(string? payload) =>
        RequiresEnvelope(payload) ? (DurableKeyJson.Write(payload!), true) : (payload, false);

    internal static string? DecodePayload(string? text, bool escaped) =>
        escaped ? DurableKeyJson.Read<string>(text!) : text;

    internal static bool FitsRetainedEventPayload(string? payload) =>
        FoundationMetadataLimits.FitsContent(payload, FoundationMetadataLimits.EventContentBytes) &&
        (!RequiresEnvelope(payload) ||
         FoundationMetadataLimits.FitsContent(DurableKeyJson.Write(payload!), FoundationMetadataLimits.EventContentBytes));

    private static bool RequiresEnvelope(string? value) =>
        value is not null && (value.Contains('\0') || DurableKeyJson.HasUnpairedSurrogate(value));
}
