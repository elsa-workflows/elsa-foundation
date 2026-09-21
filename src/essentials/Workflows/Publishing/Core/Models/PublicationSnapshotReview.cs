namespace Elsa.Workflows.Publishing.Core.Models;

/// <summary>
/// A short-lived publication review authority. The record is immutable so a persistence provider can consume it
/// with one storage-level compare-and-delete operation across server replicas.
/// </summary>
public sealed record PublicationSnapshotReview(
    string PreflightToken,
    string CandidateHash,
    string DefinitionId,
    PublicationAction Action,
    string SlotName,
    PublicationPolicySource PolicySource,
    long? PolicyRevision,
    PublicationAction? RequestedAction,
    string? RequestedSlotName,
    string? RequestedExpectedPublicationId,
    long SlotRevision,
    string? ActivePublicationId,
    string? TenantId,
    DateTimeOffset ExpiresAt);
