using Elsa.Primitives.Models;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>The frame after one committed write, the commit rule's decision, and the value the write replaced.</summary>
public sealed record VariableFrameWrite(VariableFrameState Frame, VariableCommitDecision Decision, ValueEnvelope? OverwrittenValue);

/// <summary>
/// Immutable runtime storage for one concrete activation of an executable lexical scope.
/// </summary>
public sealed record VariableFrameState
{
    public VariableFrameState(
        string frameId,
        string scopeId,
        string activationId,
        string? parentFrameId,
        VariableFrameKind kind,
        IReadOnlyDictionary<string, ValueEnvelope> values,
        long revision = 0,
        VariableFrameStatus status = VariableFrameStatus.Active,
        IReadOnlyDictionary<string, VariableVersion>? versions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(frameId);
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activationId);
        ArgumentNullException.ThrowIfNull(values);
        if (parentFrameId is not null && string.IsNullOrWhiteSpace(parentFrameId))
            throw new ArgumentException("A parent frame ID cannot be blank when provided.", nameof(parentFrameId));
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Variable frame kind is not defined.");
        if (!Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status), status, "Variable frame status is not defined.");
        if (revision < 0)
            throw new ArgumentOutOfRangeException(nameof(revision), revision, "Variable frame revision cannot be negative.");

        var snapshot = values.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        if (snapshot.Keys.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Variable frame keys cannot be blank.", nameof(values));
        if (snapshot.Values.Any(value => value is null))
            throw new ArgumentException("Variable frame values cannot be null envelopes.", nameof(values));
        if (kind == VariableFrameKind.Root && parentFrameId is not null)
            throw new ArgumentException("A root variable frame cannot have a parent frame.", nameof(parentFrameId));
        if (kind != VariableFrameKind.Root && parentFrameId is null)
            throw new ArgumentException("A container or iteration variable frame requires a parent frame.", nameof(parentFrameId));

        FrameId = frameId;
        ScopeId = scopeId;
        ActivationId = activationId;
        ParentFrameId = parentFrameId;
        Kind = kind;
        Values = new ReadOnlyDictionary<string, ValueEnvelope>(snapshot);
        Revision = revision;
        Status = status;

        if (versions is { Count: > 0 })
        {
            var versionSnapshot = new Dictionary<string, VariableVersion>(StringComparer.Ordinal);
            foreach (var (key, version) in versions)
            {
                if (!snapshot.ContainsKey(key))
                    throw new ArgumentException($"Variable frame version key '{key}' is not declared in the frame.", nameof(versions));
                versionSnapshot[key] = version ?? throw new ArgumentException("Variable frame versions cannot be null.", nameof(versions));
            }

            Versions = new ReadOnlyDictionary<string, VariableVersion>(versionSnapshot);
        }
    }

    public string FrameId { get; }
    public string ScopeId { get; }
    public string ActivationId { get; }
    public string? ParentFrameId { get; }
    public VariableFrameKind Kind { get; }
    public IReadOnlyDictionary<string, ValueEnvelope> Values { get; }
    public long Revision { get; }
    public VariableFrameStatus Status { get; }

    /// <summary>
    /// Each variable's own version and value digest (spec 1221 FR-001), for the variables a versioned write has
    /// touched. A variable without an entry is at version 0 (a declaration's initial value, or a frame written before
    /// versions existed). Absent from the stored document while empty, so older frames read back unchanged. The frame
    /// <see cref="Revision"/> keeps ordering frame writes; it is not used to detect conflicts.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, VariableVersion>? Versions { get; }

    /// <summary>The variable's current version; version 0 with no digest when no versioned write touched it.</summary>
    public VariableVersion VersionOf(string variableKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(variableKey);
        return Versions is not null && Versions.TryGetValue(variableKey, out var version) ? version : VariableVersion.Initial;
    }

    /// <summary>
    /// The variable's current digest: the stored one, or, for a version-0 value that has none, the digest of the
    /// stored value. Only the commit rule's no-op check and a <c>Fault</c> node's read of a never-written variable
    /// need it; a default-mode read never does.
    /// </summary>
    public string CurrentDigestOf(string variableKey)
    {
        var version = VersionOf(variableKey);
        if (version.Digest is { } digest)
            return digest;
        return Values.TryGetValue(variableKey, out var value)
            ? VariableValueDigest.Compute(value)
            : throw new KeyNotFoundException($"Variable '{variableKey}' is not declared in frame '{FrameId}'.");
    }

    /// <summary>A blind write (no base): the commit rule applies it; last commit wins (D-8).</summary>
    public VariableFrameState Set(string variableKey, ValueEnvelope value, long expectedRevision) =>
        Commit(variableKey, value, expectedRevision, writeBase: null, LostUpdateMode.Default).Frame;

    /// <summary>
    /// Commits one write under the commit rule (spec 1221 FR-009): returns the frame after the write and the decision.
    /// A <c>Fault</c> lost update returns this frame unchanged; a write whose digest equals the current digest applies
    /// nothing and returns this frame unchanged too. <paramref name="writer"/> is recorded on the version it produces.
    /// </summary>
    public VariableFrameWrite Commit(
        string variableKey,
        ValueEnvelope value,
        long expectedRevision,
        VariableWriteBase? writeBase,
        LostUpdateMode mode,
        VariableWriter? writer = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(variableKey);
        ArgumentNullException.ThrowIfNull(value);
        EnsureWritable(expectedRevision);
        if (!Values.ContainsKey(variableKey))
            throw new KeyNotFoundException($"Variable '{variableKey}' is not declared in frame '{FrameId}'.");

        var stored = VersionOf(variableKey);
        var current = stored.Digest is null ? new VariableVersion(stored.Version, CurrentDigestOf(variableKey), stored.Writer) : stored;
        var decision = VariableCommitRule.Evaluate(writeBase, current, VariableValueDigest.Compute(value), mode);
        if (!decision.ChangesValue)
            return new VariableFrameWrite(this, decision with { Current = stored, Resulting = stored }, OverwrittenValue: null);

        var resulting = new VariableVersion(decision.Resulting.Version, decision.Resulting.Digest, writer);
        var values = Values.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        var overwritten = values[variableKey];
        values[variableKey] = value;
        var versions = Versions?.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal)
                       ?? new Dictionary<string, VariableVersion>(StringComparer.Ordinal);
        versions[variableKey] = resulting;
        var frame = new VariableFrameState(FrameId, ScopeId, ActivationId, ParentFrameId, Kind, values, checked(Revision + 1), Status, versions);
        return new VariableFrameWrite(frame, decision with { Resulting = resulting }, overwritten);
    }

    public VariableFrameState Close(long expectedRevision)
    {
        EnsureWritable(expectedRevision);
        return new VariableFrameState(FrameId, ScopeId, ActivationId, ParentFrameId, Kind, Values, checked(Revision + 1), VariableFrameStatus.Closed, Versions);
    }

    private void EnsureWritable(long expectedRevision)
    {
        if (Status != VariableFrameStatus.Active)
            throw new InvalidOperationException($"Variable frame '{FrameId}' is closed and cannot be changed.");
        if (expectedRevision != Revision)
            throw new VariableFrameRevisionException(FrameId, expectedRevision, Revision);
    }
}

public enum VariableFrameKind
{
    Root,
    Container,
    Iteration
}

public enum VariableFrameStatus
{
    Active,
    Closed
}

public sealed class VariableFrameRevisionException(string frameId, long expectedRevision, long actualRevision)
    : InvalidOperationException($"Variable frame '{frameId}' has revision {actualRevision}; write expected revision {expectedRevision}.")
{
    public string FrameId { get; } = frameId;
    public long ExpectedRevision { get; } = expectedRevision;
    public long ActualRevision { get; } = actualRevision;
}
