using System.Text.Json;

namespace Elsa.Agent.Specialists.Core.Models;

/// <summary>
/// Observable evidence of one native specialist invocation: the exact specialist version and model it was admitted with,
/// its native and journal state, model requests, usage and conversation. Protected request/result/session material,
/// instructions, fences, leases, connection incarnations and capability fingerprints are never part of it.
/// </summary>
public sealed record SpecialistInvocationEvidence(
    string WorkflowExecutionId,
    string ActivityExecutionId,
    SpecialistInvocationVersionEvidence Specialist,
    SpecialistModelReference Model,
    SpecialistTurnLimits Limits,
    string ActivityStatus,
    SpecialistInvocationFaultEvidence? Fault,
    SpecialistInvocationOutputEvidence? Output,
    SpecialistTurnEvidence? Turn);

/// <summary>The exact specialist version and executable artifact; a draft candidate carries its candidate version identity.</summary>
public sealed record SpecialistInvocationVersionEvidence(string DefinitionId, string DefinitionVersionId, string Version, string TemplateHash,
    string ArtifactId, string ArtifactHash, bool AllowContinuity);

/// <summary>
/// The native fault's code. Its message is present only for a specialist domain refusal, whose text this module authors;
/// other messages, stack traces and exception types stay out.
/// </summary>
public sealed record SpecialistInvocationFaultEvidence(string Code, string? Message);

/// <summary>The accepted business result, present only when the native completion holds it inline without an encryption requirement.</summary>
public sealed record SpecialistInvocationOutputEvidence(JsonElement Value);

/// <summary>The staged journal turn. Its status is the journal's own; accepted and applied are separate facts.</summary>
public sealed record SpecialistTurnEvidence(
    SpecialistTurnStatus Status,
    string? FailureCode,
    bool Applied,
    DateTimeOffset Deadline,
    int MaximumModelCalls,
    IReadOnlyList<SpecialistModelRequestEvidence> ModelRequests,
    SpecialistUsageEvidence? Usage,
    SpecialistConversationEvidence? Conversation,
    DateTimeOffset? ContentPurgedAt);

/// <summary>One reserved physical model request. A reservation without response evidence may or may not have reached the provider.</summary>
public sealed record SpecialistModelRequestEvidence(string Id, DateTimeOffset ReservedAt, int MaximumOutputTokens, string ProviderKey, string ConfiguredModelId);

/// <summary>Response evidence as the provider reported it; missing counts stay unknown, never zero.</summary>
public sealed record SpecialistUsageEvidence(DateTimeOffset ObservedAt, bool UsageAvailable, long? InputTokens, long? OutputTokens, long? TotalTokens, string? ObservedModelId);

/// <summary>
/// The conversation this turn was admitted against and its current journal state. A base revision of zero is a fresh
/// conversation. <see cref="CurrentRevision"/> is null when the conversation is no longer readable in this scope.
/// </summary>
public sealed record SpecialistConversationEvidence(string ConversationId, long BaseRevision, long? CurrentRevision, bool OutcomeUnknown, bool TurnActive);
