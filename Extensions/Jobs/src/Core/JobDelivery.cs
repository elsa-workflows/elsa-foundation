namespace Elsa.Jobs.Core;

/// <summary>Exact result/model input permission request; repeating it resolves the same grant, never a new physical job.</summary>
public sealed record JobContinuationRequest(
    JobScope Scope,
    string JobId,
    string ResultId,
    string ContinuationId,
    string ModelInputId);

/// <summary>Durable workflow-scope pause cutoff bound to the exact result and model address.</summary>
public sealed record JobContinuationPermission(
    string ContinuationId,
    string JobId,
    string ResultId,
    string ModelInputId,
    JobAssociation Association,
    DateTimeOffset GrantedAt);

/// <summary>Permission confirmation; granted replay remains the same already-started continuation while paused.</summary>
public sealed record JobContinuationResult(
    JobCommandStatus Status,
    JobContinuationPermission? Permission);

/// <summary>Proof supplied by the trusted owning model adapter; result digest and model receipt identity must match.</summary>
public sealed record JobDeliveryProof(
    JobDeliveryProofKind Kind,
    string ModelReceiptId,
    string ResultSha256);

/// <summary>Retires owed responsibility only after exact consumption or proven parent terminality.</summary>
public sealed record JobAcknowledgeDeliveryRequest(
    JobScope Scope,
    string JobId,
    string ContinuationId,
    JobDeliveryProof Proof);

/// <summary>Recoverable result responsibility; an absent grant is held separately from the retained terminal result.</summary>
public sealed record JobDeliverySnapshot(
    string JobId,
    string ResultId,
    JobAssociation Association,
    JobContinuationPermission? Permission);

/// <summary>Exact model-owned evidence, never transport queue acknowledgement.</summary>
public enum JobDeliveryProofKind
{
    Consumed = 0,
    AlreadyConsumed = 1,
    ParentProvenTerminal = 2
}
