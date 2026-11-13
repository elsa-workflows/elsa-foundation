using System.Text.Json.Serialization;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// A variable's own version and value digest (spec 1221 FR-001, ADR 0092 decision 2). The version counts applied writes
/// that changed the value; the digest is taken over the canonical JSON form of the value once, when the write applies.
/// A variable written before versions existed reads as version 0 with no digest.
/// </summary>
public sealed record VariableVersion
{
    [JsonConstructor]
    public VariableVersion(long version, string? digest, VariableWriter? writer = null)
    {
        if (version < 0)
            throw new ArgumentOutOfRangeException(nameof(version), version, "A variable version cannot be negative.");
        if (digest is not null && string.IsNullOrWhiteSpace(digest))
            throw new ArgumentException("A variable digest cannot be blank when present.", nameof(digest));

        Version = version;
        Digest = digest;
        Writer = writer;
    }

    public long Version { get; }
    public string? Digest { get; }

    /// <summary>Who wrote this version, so a lost-update warning can name the writer whose value it overwrote.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public VariableWriter? Writer { get; }

    /// <summary>The version of a variable no versioned write has touched (legacy records and declarations alike).</summary>
    public static VariableVersion Initial { get; } = new(0, null);
}

/// <summary>Where a variable write came from (spec 1221 FR-024).</summary>
public enum VariableWriteSource
{
    Capture,
    Intrinsic,
    SessionChange,
    Alteration,
    BackgroundResult
}

/// <summary>The writer of a variable write: the node, invocation and attempt, and the kind of write (spec 1221 FR-024).</summary>
public sealed record VariableWriter
{
    [JsonConstructor]
    public VariableWriter(VariableWriteSource source, string? executableNodeId, string? invocationId, string? attemptId = null)
    {
        if (!Enum.IsDefined(source))
            throw new ArgumentOutOfRangeException(nameof(source), source, "Variable write source is not defined.");

        Source = source;
        ExecutableNodeId = executableNodeId;
        InvocationId = invocationId;
        AttemptId = attemptId;
    }

    public VariableWriteSource Source { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExecutableNodeId { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InvocationId { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AttemptId { get; }
}

/// <summary>
/// One write-history record (spec 1221 FR-024–FR-027, ADR 0092 decision 13): an applied write or a lost update, with its
/// mandatory metadata always, and the operational data recovery or repair needs. It is committed in the same checkpoint
/// as the write it records, on the writer's own durable record, so every intermediate write is kept, including a
/// <c>0 → 1 → 0</c> inside one buffered segment, and no extra row is written (Open question B). Full historical values
/// for inspection are not captured here; the value a lost-update warning overwrote is, as operational data.
/// </summary>
public sealed record VariableWriteRecord
{
    [JsonConstructor]
    public VariableWriteRecord(
        string scopeId,
        string variableKey,
        VariableWriter writer,
        VariableCommitOutcome outcome,
        long? baseVersion,
        long currentVersion,
        long newVersion,
        long commitOrder,
        DateTimeOffset occurredAt,
        VariableWriter? overwrittenWriter = null,
        ValueEnvelope? overwrittenValue = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(variableKey);
        ArgumentNullException.ThrowIfNull(writer);
        if (!Enum.IsDefined(outcome))
            throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Variable commit outcome is not defined.");

        ScopeId = scopeId;
        VariableKey = variableKey;
        Writer = writer;
        Outcome = outcome;
        BaseVersion = baseVersion;
        CurrentVersion = currentVersion;
        NewVersion = newVersion;
        CommitOrder = commitOrder;
        OccurredAt = occurredAt;
        OverwrittenWriter = overwrittenWriter;
        OverwrittenValue = overwrittenValue;
    }

    public string ScopeId { get; }
    public string VariableKey { get; }
    public VariableWriter Writer { get; }

    /// <summary>The commit rule's outcome; a lost update is <see cref="VariableCommitOutcome.LostUpdateWarning"/> or <see cref="VariableCommitOutcome.LostUpdateFault"/>.</summary>
    public VariableCommitOutcome Outcome { get; }

    /// <summary>The version the write was built on; null for a blind write.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? BaseVersion { get; }

    /// <summary>The variable's version when the write committed.</summary>
    public long CurrentVersion { get; }

    /// <summary>The version after the write (equal to the current version when it applied nothing or faulted).</summary>
    public long NewVersion { get; }

    /// <summary>The instance commit order: the revision of the variable's frame after this write.</summary>
    public long CommitOrder { get; }

    public DateTimeOffset OccurredAt { get; }

    /// <summary>For a lost update: the writer of the version the write overwrote (or would have overwritten).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public VariableWriter? OverwrittenWriter { get; }

    /// <summary>Operational data for a lost-update warning: the value the write overwrote, kept so it can be restored.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ValueEnvelope? OverwrittenValue { get; }

    /// <summary>
    /// Operational data for a <c>Fault</c> lost update: the value the refused write would have written, the rejected
    /// completion's capture, kept for accept-recorded-completion.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ValueEnvelope? RejectedValue { get; init; }

    [JsonIgnore]
    public bool IsLostUpdate => Outcome is VariableCommitOutcome.LostUpdateWarning or VariableCommitOutcome.LostUpdateFault;

    /// <summary>Builds the record for one committed write.</summary>
    public static VariableWriteRecord From(VariableFrameState before, VariableFrameWrite write, string variableKey, VariableWriter writer, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(write);
        var decision = write.Decision;
        var lostUpdate = decision.IsLostUpdate;
        return new VariableWriteRecord(
            before.ScopeId,
            variableKey,
            writer,
            decision.Outcome,
            decision.Base?.Version,
            decision.Current.Version,
            decision.Resulting.Version,
            write.Frame.Revision,
            occurredAt,
            lostUpdate ? decision.Current.Writer : null,
            lostUpdate && decision.ChangesValue ? write.OverwrittenValue : null);
    }
}

/// <summary>Resolves a node's pinned lost-update mode (spec 1221 FR-012).</summary>
public static class LostUpdateModes
{
    /// <summary>
    /// The node's mode as the publish compiler pinned it in <see cref="RuntimeMetadataKeys.LostUpdateMode"/> from the
    /// activity type's declaration or the node's own setting. Absent means <see cref="LostUpdateMode.Default"/>.
    /// </summary>
    public static LostUpdateMode Of(ExecutableNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.Metadata.TryGetValue(RuntimeMetadataKeys.LostUpdateMode, out var value) &&
               StringComparer.Ordinal.Equals(value, RuntimeMetadataKeys.LostUpdateModeFault)
            ? LostUpdateMode.Fault
            : LostUpdateMode.Default;
    }
}

/// <summary>
/// One actual, instrumented read of a variable (spec 1221 FR-002–FR-004): which variable, the version seen and, only
/// for a node whose lost-update mode is <see cref="LostUpdateMode.Fault"/>, the stored digest seen.
/// </summary>
public sealed record VariableReadSetEntry
{
    [JsonConstructor]
    public VariableReadSetEntry(string scopeId, string variableKey, long version, string? digest = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(variableKey);
        if (version < 0)
            throw new ArgumentOutOfRangeException(nameof(version), version, "A read version cannot be negative.");

        ScopeId = scopeId;
        VariableKey = variableKey;
        Version = version;
        Digest = digest;
    }

    public string ScopeId { get; }
    public string VariableKey { get; }
    public long Version { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Digest { get; }

    /// <summary>The base this read gives a write to the same variable.</summary>
    public VariableWriteBase ToBase() => new(Version, Digest);
}

/// <summary>The version (and, under <see cref="LostUpdateMode.Fault"/>, the digest) a write was built on.</summary>
public readonly record struct VariableWriteBase(long Version, string? Digest);

/// <summary>
/// The input read set of an invocation (spec 1221 FR-002): the reads its input materialization made, committed with the
/// input snapshot. Its entries are the invocation's bases for a later capture and are immutable for the invocation.
/// An invocation writes each captured variable once, at its completion; advancing its own base after an earlier write
/// of its own (FR-007) arrives with the variable session's writes.
/// </summary>
public sealed record VariableInputReadSet
{
    [JsonConstructor]
    public VariableInputReadSet(IReadOnlyList<VariableReadSetEntry> entries, LostUpdateMode mode = LostUpdateMode.Default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Lost-update mode is not defined.");

        // One entry per variable: the oldest read wins (spec assumption: bind to the oldest in-use dependency).
        var distinct = new List<VariableReadSetEntry>();
        foreach (var entry in entries)
        {
            ArgumentNullException.ThrowIfNull(entry);
            if (!distinct.Any(existing => existing.ScopeId == entry.ScopeId && existing.VariableKey == entry.VariableKey))
                distinct.Add(entry);
        }

        Entries = distinct.AsReadOnly();
        Mode = mode;
    }

    public IReadOnlyList<VariableReadSetEntry> Entries { get; }
    public LostUpdateMode Mode { get; }

    public static VariableInputReadSet Empty { get; } = new([]);

    /// <summary>The base for a write to the variable, or <see langword="null"/> for a blind write (no read).</summary>
    public VariableWriteBase? BaseFor(string scopeId, string variableKey)
    {
        foreach (var entry in Entries)
        {
            if (StringComparer.Ordinal.Equals(entry.ScopeId, scopeId) && StringComparer.Ordinal.Equals(entry.VariableKey, variableKey))
                return entry.ToBase();
        }

        return null;
    }
}
