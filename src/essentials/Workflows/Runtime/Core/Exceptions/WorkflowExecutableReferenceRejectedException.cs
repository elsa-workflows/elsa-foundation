using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Exceptions;

/// <summary>
/// Thrown when a dispatch is refused by the Source Reference gate (ADR 0040): the artifact exists, but no reference
/// lets it be dispatched under the requested <see cref="WorkflowExecutableReferenceScope"/>. The <see cref="Reason"/>
/// distinguishes "the artifact has no live reference of the required scope" from "the only matching reference has
/// expired", so callers (and Studio) can tell an un-published/retired artifact apart from a lapsed test run.
/// </summary>
public sealed class WorkflowExecutableReferenceRejectedException : Exception
{
    public WorkflowExecutableReferenceRejectedException(
        string artifactId,
        WorkflowExecutableReferenceScope requiredScope,
        WorkflowExecutableReferenceRejectionReason reason)
        : base(BuildMessage(artifactId, requiredScope, reason))
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);

        ArtifactId = artifactId;
        RequiredScope = requiredScope;
        Reason = reason;
    }

    public string ArtifactId { get; }
    public WorkflowExecutableReferenceScope RequiredScope { get; }
    public WorkflowExecutableReferenceRejectionReason Reason { get; }

    private static string BuildMessage(
        string artifactId,
        WorkflowExecutableReferenceScope requiredScope,
        WorkflowExecutableReferenceRejectionReason reason) =>
        reason switch
        {
            WorkflowExecutableReferenceRejectionReason.Expired =>
                $"Workflow executable artifact '{artifactId}' has no live {requiredScope} reference: the matching reference has expired.",
            WorkflowExecutableReferenceRejectionReason.SelectionNotFound =>
                $"Workflow executable artifact '{artifactId}' has no live {requiredScope} reference matching the requested source selection.",
            WorkflowExecutableReferenceRejectionReason.Ambiguous =>
                $"Workflow executable artifact '{artifactId}' has multiple live {requiredScope} references; the source selection must identify exactly one.",
            _ =>
                $"Workflow executable artifact '{artifactId}' has no live {requiredScope} reference."
        };
}

/// <summary>Why the Source Reference gate refused a dispatch (ADR 0040).</summary>
public enum WorkflowExecutableReferenceRejectionReason
{
    /// <summary>The artifact carries references, but none is a live (non-retired, non-expired) reference of the required scope.</summary>
    NoLiveReference,

    /// <summary>A reference of the required scope exists but has passed its expiry — the distinguishing test-run-lapsed signal.</summary>
    Expired,

    /// <summary>The caller's selector did not resolve to a reference for this artifact, scope and liveness.</summary>
    SelectionNotFound,

    /// <summary>More than one live reference matched, so provenance cannot be pinned unambiguously.</summary>
    Ambiguous
}
