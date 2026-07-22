using System.Text.Json.Serialization;

namespace Elsa.Activities.Bpmn.Models;

/// <summary>
/// One BPMN token. Tokens are minted when a start event fires or when a sequence flow is taken
/// (<see cref="FlowId"/> records the inbound flow), sit at <see cref="AtElementId"/>, and are consumed
/// when the element routes them onward, an end event absorbs them, or a terminate end event ends the
/// process.
/// </summary>
public sealed record BpmnToken
{
    [JsonConstructor]
    public BpmnToken(
        string tokenId,
        string atElementId,
        string? flowId = null,
        string? parentTokenId = null,
        BpmnTokenStatus status = BpmnTokenStatus.Active,
        string? producingActivityExecutionId = null,
        string? iterationKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenId);
        ArgumentException.ThrowIfNullOrWhiteSpace(atElementId);

        TokenId = tokenId;
        AtElementId = atElementId;
        FlowId = flowId;
        ParentTokenId = parentTokenId;
        Status = status;
        ProducingActivityExecutionId = producingActivityExecutionId;
        IterationKey = iterationKey;
    }

    public string TokenId { get; init; }
    public string AtElementId { get; init; }

    /// <summary>The sequence flow the token arrived on, or <c>null</c> for start-event tokens.</summary>
    public string? FlowId { get; init; }

    public string? ParentTokenId { get; init; }
    public BpmnTokenStatus Status { get; init; }

    /// <summary>The activity execution whose completion produced this token, when known.</summary>
    public string? ProducingActivityExecutionId { get; init; }

    /// <summary>
    /// The loop-iteration key (spec 122; the token analog of the Flowchart #382 loop-iteration scope). It is
    /// <c>null</c> for the implicit first pass ("iteration 0") and is minted fresh only when a token traverses
    /// a backward (loop-back) sequence flow (<see cref="Elsa.Activities.Bpmn.Internal.BpmnGraph.IsBackwardFlow"/>);
    /// every other minting site inherits its source/parent/group token's key. Join accounting groups arrivals
    /// by <c>(element, iteration key)</c> so a revisited join never conflates one iteration with the next.
    /// </summary>
    public string? IterationKey { get; init; }
}

public enum BpmnTokenStatus
{
    /// <summary>The token is at an element and must still be dispatched to the element's behavior.</summary>
    Active,

    /// <summary>The token is parked while the element's bound Elsa child activity runs.</summary>
    AwaitingChild,

    /// <summary>The token arrived at a joining gateway and waits for the join to fire.</summary>
    WaitingAtJoin,

    Consumed,
    Canceled
}
