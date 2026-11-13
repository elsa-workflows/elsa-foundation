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

/// <summary>Immutable setup data. Execution obtains it from a trusted admitted-context source; read-only inspection grants no authority.</summary>
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

    private static IReadOnlyDictionary<string, ValueEnvelope> Snapshot(IReadOnlyDictionary<string, ValueEnvelope> values) =>
        new ReadOnlyDictionary<string, ValueEnvelope>(values.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal));
}
