using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Exceptions;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Provider replacement capability for an existing source outbox row's immutable recipient snapshot.</summary>
/// <remarks>
/// One scoped implementation per provider. Install atomically validates exact persisted intent/scope and current
/// owner/fence/unexpired persisted visibility, committed source scope and the authoritative publisher's tenant/partition/run/test-scope facts, then chooses absent-to-whole
/// snapshot under row revision CAS. Return the persisted winner, never an
/// uncommitted losing proposal. Every later row rewrite preserves that winner. A missing/legacy ambiguous source,
/// corrupt state, unavailable provider or lost authority must refuse before effects; no per-attempt fallback exists.
/// Read an existing snapshot through the same provider's exact outbox lookup. This contract creates no second ledger.
/// </remarks>
public interface IStimulusDeliveryStore
{
    /// <summary>Proves ordinary collection is still safe for the actual selected physical outbox/material/reference/execution boundary.</summary>
    /// <remarks>Called on every ordinary sweep and its guarded final reads. Protocol/cohort data refuses ordinary
    /// fallback, including when access or composition changed after the collector was resolved. No absent-capability
    /// or narrower-query fallback is permitted. This is not an old-writer or publisher-activation preflight.</remarks>
    /// <exception cref="NotSupportedException">The selected source boundary or capability is unsupported.</exception>
    /// <exception cref="StimulusDeliveryRetentionException">Protocol obligations or provider failure require retaining material; preserves cause.</exception>
    /// <exception cref="InvalidDataException">Visible source authority is corrupt.</exception>
    /// <exception cref="InvalidOperationException">The committed access boundary cannot be established.</exception>
    /// <exception cref="OperationCanceledException">Requested cancellation propagates.</exception>
    ValueTask EnsureOrdinaryCollectionAllowedAsync(IRuntimePostCommitOutboxStore outbox, IWorkflowExecutableStore executables,
        IWorkflowExecutableSourceReferenceStore references, IWorkflowExecutionStateStore executions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(outbox); ArgumentNullException.ThrowIfNull(executables);
        ArgumentNullException.ThrowIfNull(references); ArgumentNullException.ThrowIfNull(executions); cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromException(new NotSupportedException("Ordinary collection cannot prove the absence of stimulus protocol obligations."));
    }

    /// <summary>Whether retention coordinates these actual selected outbox, material, reference and physical execution stores.</summary>
    /// <remarks>Structural maintenance capability only; this is not publisher rollout or legacy-writer readiness.
    /// Callers must qualify their selected stores. Unsupported providers report false.</remarks>
    /// <exception cref="StimulusDeliveryRetentionException">A synchronous provider/access failure prevents qualification; preserves its cause.</exception>
    bool CanRetainExecutables(IRuntimePostCommitOutboxStore outbox, IWorkflowExecutableStore executables,
        IWorkflowExecutableSourceReferenceStore references, IWorkflowExecutionStateStore executions) => false;

    /// <summary>Reads owed cohort roots for the selected physical material boundary, rather than a delivery scope.</summary>
    /// <remarks>Native material is shared: maintenance includes every committed source scope sharing that material.
    /// EF material is scoped: maintenance requires the same actual context and scope. This is a maintenance read,
    /// not recipient selection. Legacy providers refuse rather than substitute a narrower scoped root set.</remarks>
    /// <exception cref="NotSupportedException">The selected physical boundary cannot be established.</exception>
    /// <exception cref="StimulusDeliveryRetentionException">Provider failure; retains actual scope/cause when known.</exception>
    /// <exception cref="InvalidDataException">Source or retirement authority is corrupt; retain material.</exception>
    /// <exception cref="InvalidOperationException">A committed access boundary cannot be established.</exception>
    /// <exception cref="OperationCanceledException">Requested cancellation propagates.</exception>
    ValueTask<IReadOnlyCollection<string>> ListPinnedExecutableArtifactIdsAsync(IWorkflowExecutableStore executables,
        IWorkflowExecutableSourceReferenceStore references, IWorkflowExecutionStateStore executions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executables); ArgumentNullException.ThrowIfNull(references); ArgumentNullException.ThrowIfNull(executions);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromException<IReadOnlyCollection<string>>(new NotSupportedException("Physical stimulus retention is unavailable."));
    }

    /// <exception cref="StimulusDeliveryBoundaryException">
    /// The exact persisted source, publisher scope or snapshot cannot be read/validated. Include its source ID and
    /// preserve the original cause. Temporary dependency failures, including unrequested provider cancellation,
    /// set IsUnavailable; malformed/conflicting/unsupported legacy state does not. No failed installation accepts effects.
    /// </exception>
    /// <exception cref="RuntimePostCommitOutboxStaleClaimException">
    /// Current persisted owner/fence or provider-clock visibility no longer admits this source claim. Preserve the source for its rightful owner.
    /// </exception>
    /// <exception cref="OperationCanceledException">The supplied cancellation token was requested; propagate it unchanged.</exception>
    /// <exception cref="ArgumentException">The caller supplied invalid source/candidate arguments.</exception>
    ValueTask<StimulusDeliverySnapshot> InstallAsync(PostCommitDeliveryContext source, StimulusDeliverySnapshot candidate,
        CancellationToken cancellationToken = default);

    /// <summary>List scoped cohort start pins until delivery and the deterministic target's sticky retirement tombstone.</summary>
    /// <remarks>Includes delivered sources after publisher deletion and uncreated targets. Absence never releases a pin.
    /// Providers must traverse every finite source page and retain on unreadable authority. Legacy providers refuse.</remarks>
    /// <exception cref="StimulusDeliveryRetentionException">Provider unavailability, including unrequested cancellation; preserve the cause and actual scope if known.</exception>
    /// <exception cref="InvalidDataException">Persisted source/cohort or retirement authority is corrupt; retain material.</exception>
    /// <exception cref="InvalidOperationException">Actual access or committed-boundary requirements cannot be established.</exception>
    /// <exception cref="NotSupportedException">The selected provider lacks root enumeration; no empty-set fallback.</exception>
    /// <exception cref="OperationCanceledException">Requested cancellation propagates.</exception>
    ValueTask<IReadOnlyCollection<string>> ListPinnedExecutableArtifactIdsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromException<IReadOnlyCollection<string>>(new NotSupportedException(
            "The selected provider cannot enumerate saved stimulus retention roots."));
    }

    /// <summary>Reread an exact physical source and captured binding under its current claim and publisher scope.</summary>
    /// <remarks>Read-only provenance validation; it does not admit a start or replace reference/retention checks.
    /// Legacy implementations refuse before effects rather than falling back to intent-only or live matching.</remarks>
    /// <exception cref="StimulusDeliveryBoundaryException">Source/cohort/scope/support is missing or contradictory. Preserve the cause and mark temporary dependency failure unavailable.</exception>
    /// <exception cref="RuntimePostCommitOutboxStaleClaimException">Current owner/fence or trusted provider-clock expiry no longer admits this claim.</exception>
    /// <exception cref="OperationCanceledException">Requested cancellation propagates.</exception>
    ValueTask<PostCommitDeliveryContext> ReadSavedStartSourceAsync(WorkflowExecutableStimulusDeliveryAuthority authority,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authority); cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromException<PostCommitDeliveryContext>(new StimulusDeliveryBoundaryException(authority.SourceOutboxItemId,
            new NotSupportedException("The selected provider cannot validate saved-start source provenance."), false));
    }
}
