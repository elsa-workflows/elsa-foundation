using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;

namespace Elsa.Weaver.Services;

/// <summary>Bounded transcript dependencies; current execution context and original targets are kept elsewhere.</summary>
/// <remarks>
/// A snapshot's identity is the digest of its canonical JSON. The bounds count distinct identities and their canonical
/// size, the same text and size the session records once per distinct snapshot, so a repeated snapshot never counts twice.
/// </remarks>
internal sealed class WeaverDisclosureHistory
{
    internal const int MaximumSnapshots = 64;
    internal const int MaximumBytes = 8 * 1024 * 1024;
    // Frozen snapshots are immutable, so an identity computed once holds for the instance's lifetime.
    private static readonly ConditionalWeakTable<WeaverDisclosureSnapshot, string> Identities = new();
    private readonly List<WeaverDisclosureSnapshot> _snapshots = [];
    private readonly HashSet<string> _identities = new(StringComparer.Ordinal);
    private long _bytes;

    internal IReadOnlyList<WeaverDisclosureSnapshot> Snapshots => Array.AsReadOnly(_snapshots.ToArray());

    internal WeaverError? Add(IReadOnlyCollection<WeaverContextAttachment> context, IReadOnlyList<WeaverToolResult> results)
    {
        if (context is null || results is null)
            return new("weaver.history.provenance_unavailable", "Complete disclosure history is missing its original context or outcomes.", 409);
        if (context.Count == 0 && results.Count == 0) return null;
        try
        {
            var snapshot = Freeze(new WeaverDisclosureSnapshot(context, results));
            var recorded = Record(snapshot);
            if (_identities.Contains(recorded.Digest)) return null;
            if (_snapshots.Count >= MaximumSnapshots)
                return LimitReached("snapshots", "64 distinct disclosure snapshots");
            // One snapshot larger than any session can record is refused on its own; a new session would not help, so it
            // names no session limit and nothing is counted against this session.
            if (recorded.Size > MaximumBytes)
                return new("weaver.context.disclosure_budget_exceeded",
                    "This context is larger than one conversation can record (8 MiB), so it was not disclosed. Narrow it and try again; this conversation is unchanged.", 409)
                { Limit = SnapshotTooLarge };
            if (_bytes + recorded.Size > MaximumBytes)
                // Only a session with less than the minimum room left is full; otherwise this one snapshot does not fit, a
                // smaller one still would, and nothing about the session is recorded.
                return MaximumBytes - _bytes < MinimumRoom
                    ? LimitReached("bytes", "nearly 8 MiB of disclosure, with less than 64 KiB left")
                    : new("weaver.context.disclosure_budget_exceeded",
                        $"This context does not fit in the room left in this conversation: {Mebibytes(_bytes)} of 8 MiB used. It was not disclosed; narrow it and try again. This conversation is unchanged.", 409)
                    { Limit = SnapshotDoesNotFit };
            _identities.Add(recorded.Digest);
            _snapshots.Add(snapshot);
            _bytes += recorded.Size;
            return null;
        }
        catch (Exception error) when (error is JsonException or NotSupportedException or ArgumentException
            or WeaverContributionException { InnerException: null })
        {
            return new("weaver.history.provenance_unavailable", "Complete immutable disclosure history could not be retained.", 409);
        }
    }

    /// <summary>The <see cref="WeaverError.Limit"/> of one snapshot larger than any session can record; not a session limit.</summary>
    internal const string SnapshotTooLarge = "snapshot-too-large";

    /// <summary>
    /// The <see cref="WeaverError.Limit"/> of a snapshot that does not fit in the room left in a session that is not full; not
    /// a session limit, so nothing is recorded and the session can continue with smaller context.
    /// </summary>
    internal const string SnapshotDoesNotFit = "snapshot-does-not-fit";

    /// <summary>
    /// A session reports the <c>bytes</c> session limit only when less than this much room is left, and is then treated as
    /// full (a small, bounded snapshot could still fit, but the session no longer has useful room). Otherwise a snapshot that
    /// does not fit is refused on its own (<see cref="SnapshotDoesNotFit"/>).
    /// </summary>
    internal const int MinimumRoom = 64 * 1024;

    private static string Mebibytes(long bytes) => (bytes / (1024d * 1024d)).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " MiB";

    /// <summary>Whether <paramref name="limit"/> names a documented session limit, which the session records.</summary>
    internal static bool IsSessionLimit(string? limit) => limit is "snapshots" or "bytes";

    /// <summary>
    /// The documented session limit outcome. Nothing is evicted, reset or replayed: every recorded outcome and the complete
    /// provenance stay, and the session stays readable. A continuation session carries over only a link to this one.
    /// </summary>
    private static WeaverError LimitReached(string limit, string bound)
        => new("weaver.context.disclosure_budget_exceeded",
            $"Session limit reached: this conversation has recorded {bound}. Continue in a new session; nothing recorded here was discarded.", 409)
        { Limit = limit };

    internal WeaverError? Add(IEnumerable<WeaverDisclosureSnapshot> snapshots)
    {
        foreach (var snapshot in snapshots)
        {
            if (snapshot is null) return new("weaver.history.provenance_unavailable", "A retained disclosure snapshot is missing.", 409);
            // A recorded snapshot already carries its identity; a repeat is skipped without being written out again.
            if (Identities.TryGetValue(snapshot, out var identity) && _identities.Contains(identity)) continue;
            if (Add(snapshot.Context, snapshot.ToolResults) is { } error) return error;
        }
        return null;
    }

    internal static IReadOnlyList<WeaverDisclosureSnapshot> Freeze(IEnumerable<WeaverDisclosureSnapshot> snapshots)
        => Array.AsReadOnly(snapshots.Select(Freeze).ToArray());

    private static WeaverDisclosureSnapshot Freeze(WeaverDisclosureSnapshot snapshot)
        => new(Array.AsReadOnly(snapshot.Context.Select(attachment => attachment with
        {
            Content = attachment.Content is null ? null : JsonSerializer.SerializeToElement(attachment.Content).Clone(),
            References = new ReadOnlyDictionary<string, string>(attachment.References.ToDictionary()),
            WeaverScopes = Array.AsReadOnly(attachment.WeaverScopes.ToArray())
        }).ToArray()), Array.AsReadOnly(snapshot.ToolResults.Select(result => result with
        {
            Payload = result.Payload is null ? null : WeaverInvocationIntegrity.Freeze(result.Payload),
            Receipt = result.Receipt is null ? null : result.Receipt with { Evidence = WeaverInvocationIntegrity.Freeze(result.Receipt.Evidence) }
        }).ToArray()));

    /// <summary>The identity of a frozen snapshot: the digest of its canonical JSON, computed once per instance.</summary>
    internal static string Identity(WeaverDisclosureSnapshot snapshot) => Identities.GetValue(snapshot, WeaverInvocationIntegrity.Digest);

    /// <summary>
    /// The identity of the snapshot that an admission of <paramref name="context"/> and <paramref name="results"/>
    /// discloses, or null when it cannot be computed; such an admission is then never shared within a pass.
    /// </summary>
    internal static string? TryIdentity(IReadOnlyCollection<WeaverContextAttachment> context, IReadOnlyList<WeaverToolResult> results)
    {
        try { return WeaverInvocationIntegrity.Digest(new WeaverDisclosureSnapshot(context, results)); }
        catch (Exception error) when (error is JsonException or NotSupportedException or ArgumentException or InvalidOperationException
            or WeaverContributionException) { return null; }
    }

    /// <summary>The record of a frozen snapshot: its canonical text and the digest over it, which becomes its identity.</summary>
    internal static WeaverDisclosureSnapshotRecord Record(WeaverDisclosureSnapshot snapshot)
    {
        var canonical = WeaverInvocationIntegrity.Canonical(snapshot);
        var digest = WeaverInvocationIntegrity.DigestCanonical(canonical);
        Identities.AddOrUpdate(snapshot, digest);
        return new(digest, canonical);
    }

    /// <summary>
    /// Rebuilds a recorded snapshot. Its text must still digest to its recorded digest, and the snapshot read from it must
    /// digest to the same value, so every fingerprint over it is unchanged; null when either check fails or the text is
    /// unreadable.
    /// </summary>
    internal static WeaverDisclosureSnapshot? Rebuild(WeaverDisclosureSnapshotRecord record)
    {
        try
        {
            if (WeaverInvocationIntegrity.DigestCanonical(record.Canonical) != record.Digest
                || JsonSerializer.Deserialize<WeaverDisclosureSnapshot>(record.Canonical) is not { Context: not null, ToolResults: not null } read)
                return null;
            var snapshot = Freeze(read);
            if (WeaverInvocationIntegrity.Digest(snapshot) != record.Digest) return null;
            Identities.AddOrUpdate(snapshot, record.Digest);
            return snapshot;
        }
        catch (Exception error) when (error is JsonException or NotSupportedException or ArgumentException or InvalidOperationException
            or WeaverContributionException) { return null; }
    }

    internal static string MessageFingerprint(string content, IReadOnlyCollection<WeaverContextAttachment> context,
        IReadOnlyList<WeaverToolResult> results, IReadOnlyList<WeaverDisclosureSnapshot> history)
        => WeaverInvocationIntegrity.Digest(new { Content = content, Context = context, ToolResults = results, DisclosureHistory = history });

    internal static string PauseFingerprint(WeaverPausedTurn state)
        => WeaverInvocationIntegrity.Digest(state with { DisclosureHistoryFingerprint = null });

    /// <summary>Whether an assistant message's recorded delivery fingerprint still covers its content, context, outcomes and history.</summary>
    internal static bool AssistantHistoryMatches(WeaverMessage message)
    {
        try
        {
            return message.DisclosureHistory is not null && message.HasDisclosureProvenance
                && message.DeliveryFingerprint == MessageFingerprint(message.Content, message.Context, message.ToolResults, message.DisclosureHistory);
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or NotSupportedException) { return false; }
    }

    /// <summary>Whether a paused turn's recorded fingerprint still covers its complete state and history.</summary>
    internal static bool PauseHistoryMatches(WeaverPausedTurn state)
    {
        try { return state.DisclosureHistory is not null && state.DisclosureHistoryFingerprint == PauseFingerprint(state); }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or NotSupportedException) { return false; }
    }
}

/// <summary>
/// One authorization pass (FR-015): the owner admissions of one admission sequence, each remembered by the identity of
/// the snapshot it discloses and the flags it was asked with. Within the pass each disclosure source is asked to admit a
/// distinct snapshot at most once. A pass ends at the next write, provider call or tool execution; the awaits of owner
/// admission and tool discovery belong to it. A pass is created for one sequence and passed along explicitly: it is never
/// stored on a field and never reused after a write, so admission is never shared across passes, checkpoints or turns.
/// </summary>
internal sealed class WeaverAdmissionPass
{
    private readonly Dictionary<(string Snapshot, bool Continuation, bool Historical), WeaverResult<WeaverContextContribution>> _admitted = [];

    /// <summary>The admission this pass already obtained for the snapshot and flags, if any.</summary>
    internal WeaverResult<WeaverContextContribution>? Find(string? snapshot, bool continuation, bool historical)
        => snapshot is not null && _admitted.TryGetValue((snapshot, continuation, historical), out var admitted) ? admitted : null;

    /// <summary>Remembers an admission for the rest of this pass and returns it.</summary>
    internal WeaverResult<WeaverContextContribution> Remember(string? snapshot, bool continuation, bool historical, WeaverResult<WeaverContextContribution> admitted)
    {
        if (snapshot is not null) _admitted[(snapshot, continuation, historical)] = admitted;
        return admitted;
    }
}
