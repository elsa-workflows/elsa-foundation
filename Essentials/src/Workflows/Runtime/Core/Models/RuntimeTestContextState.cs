using System.Collections.ObjectModel;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>One admitted lexical frame outside the selected owned work, without an activity execution.</summary>
public sealed record RuntimeTestFrameSeed(
    string ScopeId,
    VariableFrameKind Kind,
    IReadOnlyDictionary<string, ValueEnvelope> Values,
    bool IsClosedBoundary = false,
    IReadOnlyDictionary<string, ValueEnvelope>? Inputs = null,
    IReadOnlyDictionary<string, string>? VariableNames = null,
    bool Writable = true);

/// <summary>An explicit whole result of a producer outside the selected owned work; never execution history.</summary>
public sealed record RuntimeTestResultFixture(
    string ProducerNodeId,
    string ProducerScopeId,
    ValueEnvelope Result);

/// <summary>Immutable setup returned only by a trusted admitted-context source.</summary>
public sealed record RuntimeTestContextSeed(
    string ContextId,
    string ContextFingerprint,
    IReadOnlyList<RuntimeTestFrameSeed> Frames,
    IReadOnlyList<RuntimeTestResultFixture> Results,
    IReadOnlyDictionary<string, ValueEnvelope>? RootVariables = null);

/// <summary>Runtime-owned frame with explicit closure/input authority, separate from invocation rows.</summary>
public sealed record RuntimeTestContextFrame(
    VariableFrameState Frame,
    bool IsClosedBoundary,
    bool Writable,
    IReadOnlyDictionary<string, ValueEnvelope> Inputs,
    IReadOnlyDictionary<string, string> VariableNames);

/// <summary>Durable attempt-local frames and immutable fixtures, atomically stored in the execution header.</summary>
public sealed record RuntimeTestContextState
{
    public RuntimeTestContextState(string contextId, string contextFingerprint,
        IReadOnlyList<RuntimeTestContextFrame> frames, IReadOnlyList<RuntimeTestResultFixture> results,
        IReadOnlyList<string> ownedNodeIds, long revision = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contextId);
        ArgumentException.ThrowIfNullOrWhiteSpace(contextFingerprint);
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(ownedNodeIds);
        if (revision < 0) throw new ArgumentOutOfRangeException(nameof(revision));
        if (frames.Select(x => x.Frame.FrameId).Distinct(StringComparer.Ordinal).Count() != frames.Count)
            throw new ArgumentException("Test context frame identities must be unique.", nameof(frames));
        if (results.GroupBy(x => (x.ProducerNodeId, x.ProducerScopeId)).Any(x => x.Count() != 1))
            throw new ArgumentException("Test result fixture identities must be unique.", nameof(results));
        if (results.Any(x => ownedNodeIds.Contains(x.ProducerNodeId, StringComparer.Ordinal)))
            throw new ArgumentException("A fixture cannot replace results of selected owned work.", nameof(results));
        ContextId = contextId;
        ContextFingerprint = contextFingerprint;
        Frames = Array.AsReadOnly(frames.Select(x => x with
        {
            Inputs = Snapshot(x.Inputs),
            VariableNames = new ReadOnlyDictionary<string, string>(x.VariableNames.ToDictionary(y => y.Key, y => y.Value, StringComparer.Ordinal))
        }).ToArray());
        Results = Array.AsReadOnly(results.ToArray());
        OwnedNodeIds = Array.AsReadOnly(ownedNodeIds.ToArray());
        Revision = revision;
    }

    public string ContextId { get; }
    public string ContextFingerprint { get; }
    public IReadOnlyList<RuntimeTestContextFrame> Frames { get; }
    public IReadOnlyList<RuntimeTestResultFixture> Results { get; }
    public IReadOnlyList<string> OwnedNodeIds { get; }
    public long Revision { get; }

    public RuntimeTestContextState Set(string frameId, string variableKey, ValueEnvelope value, long expectedFrameRevision)
    {
        var owner = Frames.SingleOrDefault(x => x.Frame.FrameId == frameId)
            ?? throw new InvalidOperationException("The test frame is unavailable.");
        if (!owner.Writable || owner.Frame.Kind == VariableFrameKind.Iteration)
            throw new InvalidOperationException("The supplied test frame is read-only.");
        if (!owner.Frame.Values.TryGetValue(variableKey, out var declared) ||
            declared.Type.Alias != value.Type.Alias || declared.Type.CollectionKind != value.Type.CollectionKind ||
            declared.Type.SchemaVersion != value.Type.SchemaVersion ||
            declared.Type.Schema?.GetRawText() != value.Type.Schema?.GetRawText())
            throw new InvalidOperationException("The test frame write differs from its declared variable type.");
        if (value.Presence == ValuePresence.Absent || value.TransientResource is not null ||
            value.Policy.Lifecycle == DurableValueLifecycle.None ||
            !value.Policy.Satisfies(declared.Policy) || value.HoldsValueRequiringEncryption(declared.Policy))
            throw new InvalidOperationException("The test frame write cannot weaken its declared protection policy.");
        var updated = owner with { Frame = owner.Frame.Set(variableKey, value, expectedFrameRevision) };
        return new(ContextId, ContextFingerprint, Frames.Select(x => x.Frame.FrameId == frameId ? updated : x).ToArray(),
            Results, OwnedNodeIds, checked(Revision + 1));
    }

    public RuntimeTestContextState Close() => Frames.All(x => x.Frame.Status == VariableFrameStatus.Closed) ? this :
        new(ContextId, ContextFingerprint,
            Frames.Select(x => x with { Frame = x.Frame.Status == VariableFrameStatus.Active ? x.Frame.Close(x.Frame.Revision) : x.Frame }).ToArray(),
            Results, OwnedNodeIds, checked(Revision + 1));

    private static IReadOnlyDictionary<string, ValueEnvelope> Snapshot(IReadOnlyDictionary<string, ValueEnvelope> values) =>
        new ReadOnlyDictionary<string, ValueEnvelope>(values.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal));
}

/// <summary>Exact frame selected for a graph capture when its lexical parent was supplied as test context.</summary>
public sealed record RuntimeTestCaptureTarget(string ContextId, string FrameId, string ScopeId, long FrameRevision);

/// <summary>One typed write folded into the same checkpoint as the producing completion.</summary>
public sealed record RuntimeTestFrameWrite(RuntimeTestCaptureTarget Target, string VariableKey, ValueEnvelope Value);
