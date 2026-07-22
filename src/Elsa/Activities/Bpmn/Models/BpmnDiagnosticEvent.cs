using System.Text.Json.Serialization;

namespace Elsa.Activities.Bpmn.Models;

public sealed record BpmnDiagnosticEvent
{
    [JsonConstructor]
    public BpmnDiagnosticEvent(
        string diagnosticId,
        BpmnDiagnosticKind kind,
        string message,
        string? elementId = null,
        string? flowId = null,
        string? tokenId = null,
        IReadOnlyDictionary<string, string>? details = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(diagnosticId);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        DiagnosticId = diagnosticId;
        Kind = kind;
        Message = message;
        ElementId = elementId;
        FlowId = flowId;
        TokenId = tokenId;
        Details = details ?? new Dictionary<string, string>();
    }

    public string DiagnosticId { get; init; }
    public BpmnDiagnosticKind Kind { get; init; }
    public string Message { get; init; }
    public string? ElementId { get; init; }
    public string? FlowId { get; init; }
    public string? TokenId { get; init; }
    public IReadOnlyDictionary<string, string> Details { get; init; }
}

public enum BpmnDiagnosticKind
{
    TokenEmitted,
    Scheduled,
    Waiting,
    Joined,
    Consumed,
    Canceled,
    Terminated,
    BehaviorFailure,
    Completed,
    Faulted,

    /// <summary>A host completion carrying an attached compensation boundary registered a compensable (spec 124).</summary>
    CompensationRegistered,

    /// <summary>A compensate throw/end event triggered a compensation replay (spec 124).</summary>
    CompensationTriggered,

    /// <summary>A compensation handler ran to completion for one registered compensable (spec 124).</summary>
    Compensated,

    /// <summary>A cancel end event began (or completed) cancelling a transaction scope (spec 125).</summary>
    TransactionCancelled
}
