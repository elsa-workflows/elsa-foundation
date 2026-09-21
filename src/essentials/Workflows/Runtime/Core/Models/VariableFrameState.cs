using System.Collections.ObjectModel;

namespace Elsa.Workflows.Runtime.Core.Models;

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
        VariableFrameStatus status = VariableFrameStatus.Active)
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
    }

    public string FrameId { get; }
    public string ScopeId { get; }
    public string ActivationId { get; }
    public string? ParentFrameId { get; }
    public VariableFrameKind Kind { get; }
    public IReadOnlyDictionary<string, ValueEnvelope> Values { get; }
    public long Revision { get; }
    public VariableFrameStatus Status { get; }

    public VariableFrameState Set(string variableKey, ValueEnvelope value, long expectedRevision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(variableKey);
        ArgumentNullException.ThrowIfNull(value);
        EnsureWritable(expectedRevision);
        if (!Values.ContainsKey(variableKey))
            throw new KeyNotFoundException($"Variable '{variableKey}' is not declared in frame '{FrameId}'.");

        var values = Values.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        values[variableKey] = value;
        return new VariableFrameState(FrameId, ScopeId, ActivationId, ParentFrameId, Kind, values, checked(Revision + 1), Status);
    }

    public VariableFrameState Close(long expectedRevision)
    {
        EnsureWritable(expectedRevision);
        return new VariableFrameState(FrameId, ScopeId, ActivationId, ParentFrameId, Kind, Values, checked(Revision + 1), VariableFrameStatus.Closed);
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
