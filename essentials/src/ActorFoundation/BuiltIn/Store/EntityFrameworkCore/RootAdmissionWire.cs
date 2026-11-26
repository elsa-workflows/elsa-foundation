using System.Text;

namespace Kernel.EntityFramework;

internal static class RootAdmissionWire
{
    internal const string CreatorPrefix = "!kernel-root/1:";
    internal const string DirectPrefix = "!kernel-direct/1:";
    internal const string HeldDirectPrefix = "!kernel-held-direct/1:";

    internal sealed record StoredRoot(string Id, StartSpec Spec, bool EscapedInput, bool ExplicitZeroDue = false);
    internal sealed record StoredDirect(string Operation, long Creation, bool EscapedPayload, bool ExplicitZeroDue, string? EscapedKind = null);

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
        long? due, bool held)
    {
        var escaped = RequiresEnvelope(payload);
        var escapedKind = kind.Contains('\0') ? kind : null;
        var prefix = held ? HeldDirectPrefix : DirectPrefix;
        return (prefix + DurableKeyJson.Write(new StoredDirect(id, creation, escaped, due == 0, escapedKind)),
            escaped ? DurableKeyJson.Write(payload!) : payload, escapedKind is null ? kind : "input");
    }

    internal static bool FitsEnvelope(string header, string? payload, string? replyTo = null) =>
        Encoding.UTF8.GetByteCount(header) + (payload is null ? 0L : Encoding.UTF8.GetByteCount(payload)) +
        (replyTo is null ? 0 : Encoding.UTF8.GetByteCount(replyTo)) <= EfStore.RootContentBytes;

    private static bool RequiresEnvelope(string? value) =>
        value is not null && (value.Contains('\0') || DurableKeyJson.HasUnpairedSurrogate(value));
}
