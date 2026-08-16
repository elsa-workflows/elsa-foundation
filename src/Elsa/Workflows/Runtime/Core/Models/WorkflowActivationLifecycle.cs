namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Deterministic 1:1 map from an activation id to the id of the source reference the coordinator mints for it.
/// </summary>
/// <remarks>
/// <para>
/// The coordinator has to answer two questions from the slot alone: "what artifact is the active activation
/// serving?" (the same-artifact idempotency rule) and "which reference must I retire now that this activation is
/// replaced?". The slot deliberately carries no <c>ArtifactId</c> and the source-reference store has no
/// by-activation query, so the link is carried in the reference <i>id</i> instead of in a new store method — the
/// coordinator owns reference identity and can therefore resolve any activation's reference with a plain
/// <c>FindAsync</c>.
/// </para>
/// <para>
/// A useful side effect: a reconcile pass that re-submits the same activation id mints the same reference id, so
/// the save is an idempotent overwrite rather than a duplicate row.
/// </para>
/// </remarks>
public static class WorkflowActivationReferenceIdentity
{
    public static string Create(string activationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activationId);
        return $"activation-ref:{activationId}";
    }
}

/// <summary>How an activation request was resolved.</summary>
public enum WorkflowActivationOutcome
{
    /// <summary>The candidate is now the live activation for its slot.</summary>
    Activated,

    /// <summary>
    /// The slot already serves this exact artifact, so nothing was written. FR-B-006's idempotent no-op:
    /// it applies to a request from <b>any</b> source, not just the owning one.
    /// </summary>
    AlreadyActive,

    /// <summary>
    /// The authority refused the transition — a stale revision or a non-owning source. Nothing was activated
    /// and the candidate's own writes were rolled back.
    /// </summary>
    Conflict,

    /// <summary>A step of the sequence failed; compensation ran. See <c>Diagnostic</c>.</summary>
    Failed
}

/// <summary>Which step of the activation sequence a <see cref="WorkflowActivationOutcome.Failed"/> outcome broke on.</summary>
/// <remarks>
/// <para>
/// The coordinator runs a fixed, ordered sequence, and a caller has to be able to tell its own audience <i>what</i>
/// did not happen — "the serving projections could not be prepared" and "the slot flipped but its route tables did
/// not follow" are different operational incidents with different remediations. Collapsing every failure into one
/// free-text diagnostic destroys that distinction and forces callers to parse prose.
/// </para>
/// <para>
/// This is a <b>discriminator, not a diagnostic</b>: it names the step, never the cause. The cause stays in
/// <see cref="WorkflowActivationResult.Diagnostic"/>.
/// </para>
/// </remarks>
public enum WorkflowActivationStep
{
    /// <summary>No step failed — the request succeeded, was a no-op, or was refused before the sequence began.</summary>
    None,

    /// <summary>Minting and saving the candidate's live source reference.</summary>
    SourceReferenceMint,

    /// <summary>Preparing both serving projections in non-serving state.</summary>
    ProjectionPreparation,

    /// <summary>The slot compare-and-swap on the authority threw. A <i>refusal</i> is a <see cref="WorkflowActivationOutcome.Conflict"/>, not this.</summary>
    SlotTransition,

    /// <summary>Making both prepared projections serve.</summary>
    ProjectionActivation,

    /// <summary>Notifying <c>IWorkflowTriggerIndexObserver</c>s of the new serving surface.</summary>
    TriggerObserverNotification,

    /// <summary>Retiring the predecessor activation's source reference.</summary>
    PredecessorReferenceRetirement
}

/// <summary>One request to make an artifact the live activation of a definition's slot.</summary>
/// <remarks>
/// The single entry point for every activating path (publish pipeline, artifact reconciler). Callers supply the
/// artifact and the provenance-bearing reference they want minted; the coordinator owns everything else —
/// leases, projections, the slot transition, observer notification and compensation. Callers MUST NOT implement
/// any part of that sequence themselves (FR-B-006).
/// </remarks>
/// <param name="Executable">
/// The artifact to activate. Its <see cref="WorkflowExecutable.Identity"/> supplies the definition id and the
/// artifact id, and the trigger projections are recomputed from it.
/// </param>
/// <param name="Reference">
/// The live source reference to mint, carrying the caller's provenance (source kind/id, layout sidecars, tenant,
/// authored inputs). The coordinator stamps its own <c>SourceReferenceId</c>, activation id and slot id onto it
/// so activation and reference stay resolvable from each other; everything else is preserved verbatim.
/// </param>
/// <param name="SlotName">The named activation lane of the definition.</param>
/// <param name="ActivationId">
/// The caller-minted, opaque activation id. Prefixes such as <c>import:{sourceId}:…</c> are for log readability
/// only and are never parsed — ownership comes from <paramref name="Source"/>.
/// </param>
/// <param name="Source">The explicit ownership descriptor for this activation.</param>
/// <param name="ExpectedRevision">The slot revision the caller read; the CAS guard for concurrent writers.</param>
public sealed record WorkflowActivationCommand(
    WorkflowExecutable Executable,
    WorkflowExecutableSourceReference Reference,
    string SlotName,
    string ActivationId,
    WorkflowActivationSource Source,
    long ExpectedRevision);

/// <summary>The outcome of one complete activation lifecycle.</summary>
/// <param name="Succeeded">True for <see cref="WorkflowActivationOutcome.Activated"/> and <see cref="WorkflowActivationOutcome.AlreadyActive"/>.</param>
/// <param name="Outcome">Which of the four resolutions applied.</param>
/// <param name="Slot">The slot as it stands after the request settled (including after compensation).</param>
/// <param name="Reference">The stored live reference, or the already-active one for a no-op.</param>
/// <param name="ReplacedActivationId">The predecessor the transition displaced, whose reference was retired.</param>
/// <param name="Conflict">Set when the authority refused the transition.</param>
/// <param name="Diagnostic">
/// The rejection reason, or the failure plus any compensation that did not converge. Never null when
/// <paramref name="Succeeded"/> is false.
/// </param>
/// <param name="FailedStep">
/// Which step broke, for <see cref="WorkflowActivationOutcome.Failed"/> only. Callers map it onto their own
/// failure vocabulary; without it every failure looks alike and step-specific incident codes degrade to a
/// single generic one.
/// </param>
/// <param name="CompensationDiagnostic">
/// Set when compensation itself did not converge, i.e. the engine may be left in a state neither the candidate
/// nor its predecessor fully owns. Distinct from <paramref name="Diagnostic"/> on purpose: "the activation
/// failed and was cleanly undone" and "the activation failed and the undo did not finish" are different
/// severities, and only the second warrants an operator page.
/// </param>
public sealed record WorkflowActivationResult(
    bool Succeeded,
    WorkflowActivationOutcome Outcome,
    WorkflowActivationSlot Slot,
    WorkflowExecutableSourceReference? Reference = null,
    string? ReplacedActivationId = null,
    WorkflowActivationConflict Conflict = WorkflowActivationConflict.None,
    string? Diagnostic = null,
    WorkflowActivationStep FailedStep = WorkflowActivationStep.None,
    string? CompensationDiagnostic = null);
