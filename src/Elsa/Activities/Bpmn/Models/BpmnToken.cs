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
        string? producingActivityExecutionId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenId);
        ArgumentException.ThrowIfNullOrWhiteSpace(atElementId);

        TokenId = tokenId;
        AtElementId = atElementId;
        FlowId = flowId;
        ParentTokenId = parentTokenId;
        Status = status;
        ProducingActivityExecutionId = producingActivityExecutionId;
    }

    public string TokenId { get; init; }
    public string AtElementId { get; init; }

    /// <summary>The sequence flow the token arrived on, or <c>null</c> for start-event tokens.</summary>
    public string? FlowId { get; init; }

    public string? ParentTokenId { get; init; }
    public BpmnTokenStatus Status { get; init; }

    /// <summary>The activity execution whose completion produced this token, when known.</summary>
    public string? ProducingActivityExecutionId { get; init; }
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
