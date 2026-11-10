using System.Text.Json;

namespace Elsa.Agent.Core.Models;

/// <summary>
/// Observable evidence of one native agent invocation: the exact agent version and model it was admitted with,
/// its native and journal state, model requests, usage and conversation. Protected request/result/session material,
/// instructions, fences, leases, connection incarnations and capability fingerprints are never part of it.
/// </summary>
public sealed record AgentInvocationEvidence(
    string WorkflowExecutionId,
    string ActivityExecutionId,
    AgentInvocationVersionEvidence Agent,
    AgentModelReference Model,
    AgentTurnLimits Limits,
    string ActivityStatus,
    AgentInvocationFaultEvidence? Fault,
    AgentInvocationOutputEvidence? Output,
    AgentTurnEvidence? Turn);

/// <summary>The exact agent version and executable artifact; a draft candidate carries its candidate version identity.</summary>
public sealed record AgentInvocationVersionEvidence(string DefinitionId, string DefinitionVersionId, string Version, string TemplateHash,
    string ArtifactId, string ArtifactHash, bool AllowContinuity);

/// <summary>
/// The native fault's code. Its message is present only for an agent domain refusal, whose text this module authors;
/// other messages, stack traces and exception types stay out.
/// </summary>
public sealed record AgentInvocationFaultEvidence(string Code, string? Message);

/// <summary>
/// The native completion's result as the host's payload capture policy admits it. <see cref="CaptureMode"/> is that
/// policy's decision (<c>None</c>, <c>MetadataOnly</c>, <c>DiagnosticSnapshot</c> or <c>Payload</c>). <see cref="Value"/>
/// is present only for a full payload capture of an inline value that no policy marks sensitive or encryption-required.
/// </summary>
public sealed record AgentInvocationOutputEvidence(string CaptureMode, JsonElement? Value);

/// <summary>The staged journal turn. Its status is the journal's own; accepted and applied are separate facts.</summary>
public sealed record AgentTurnEvidence(
    AgentTurnStatus Status,
    string? FailureCode,
    AgentTurnApplicationStatus Application,
    DateTimeOffset Deadline,
    int MaximumModelCalls,
    IReadOnlyList<AgentModelRequestEvidence> ModelRequests,
    AgentUsageEvidence? Usage,
    AgentConversationEvidence? Conversation,
    DateTimeOffset? ContentPurgedAt);

/// <summary>Whether this invocation's process took up the turn's accepted result.</summary>
public enum AgentTurnApplicationStatus
{
    /// <summary>The turn has no accepted result.</summary>
    NotAccepted,
    /// <summary>The result is accepted and the native boundary has not settled yet.</summary>
    Pending,
    /// <summary>The native boundary completed with the accepted result; its settlement record is not written yet.</summary>
    SettlementPending,
    /// <summary>The settlement for this invocation's own delivery is recorded.</summary>
    Applied,
    /// <summary>The native boundary faulted, was cancelled or was superseded, so the accepted history was not applied here.</summary>
    NotApplied
}

/// <summary>One reserved physical model request. A reservation without response evidence may or may not have reached the provider.</summary>
public sealed record AgentModelRequestEvidence(string Id, DateTimeOffset ReservedAt, int MaximumOutputTokens, string ProviderKey, string ConfiguredModelId);

/// <summary>Response evidence as the provider reported it; missing counts stay unknown, never zero.</summary>
public sealed record AgentUsageEvidence(DateTimeOffset ObservedAt, bool UsageAvailable, long? InputTokens, long? OutputTokens, long? TotalTokens, string? ObservedModelId);

/// <summary>
/// The conversation this turn was admitted against and its current journal state. A base revision of zero is a fresh
/// conversation. <see cref="CurrentRevision"/> is null when the conversation is no longer readable in this scope.
/// </summary>
public sealed record AgentConversationEvidence(string ConversationId, long BaseRevision, long? CurrentRevision, bool OutcomeUnknown, bool TurnActive);
