using System.Text.Json.Serialization;

namespace Elsa.Activities.Bpmn.Models;

/// <summary>
/// The live state of one multi-instance loop (spec 121). A coordinator token
/// (<see cref="TokenId"/>) stays <c>AwaitingChild</c> at the host <see cref="ElementId"/> while its instance
/// sub-tokens run the bound child; this record tracks how many instances remain. It is additive engine state
/// (schema stays version 1) whose only mutation home is <c>BpmnStateMutator</c>; <see cref="LoopId"/> and the
/// instance token ids derive from <c>BpmnExecutionState.Sequence</c>. The record is dropped when the loop
/// completes or its coordinator is cancelled.
/// </summary>
public sealed record BpmnLoopState
{
    [JsonConstructor]
    public BpmnLoopState(
        string loopId,
        string tokenId,
        string elementId,
        bool isSequential,
        int totalCount,
        int nextIndex,
        int completedCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(loopId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenId);
        ArgumentException.ThrowIfNullOrWhiteSpace(elementId);

        LoopId = loopId;
        TokenId = tokenId;
        ElementId = elementId;
        IsSequential = isSequential;
        TotalCount = totalCount;
        NextIndex = nextIndex;
        CompletedCount = completedCount;
    }

    /// <summary>The loop record id (a pure function of <c>Sequence</c>).</summary>
    public string LoopId { get; init; }

    /// <summary>The coordinator token id: it stays <c>AwaitingChild</c> at <see cref="ElementId"/> while instances run.</summary>
    public string TokenId { get; init; }

    /// <summary>The multi-instance host element id.</summary>
    public string ElementId { get; init; }

    /// <summary><c>true</c> = one instance at a time; <c>false</c> = all instances scheduled up front.</summary>
    public bool IsSequential { get; init; }

    /// <summary>The total number of instances the loop runs.</summary>
    public int TotalCount { get; init; }

    /// <summary>The next zero-based instance index to schedule (sequential mode advances this per completion).</summary>
    public int NextIndex { get; init; }

    /// <summary>How many instances have completed; the loop finishes when this reaches <see cref="TotalCount"/>.</summary>
    public int CompletedCount { get; init; }
}
