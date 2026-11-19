using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Executions;

/// <summary>Shared ordinary source selection; capture performs no policy admission or recipient effect.</summary>
public sealed class WorkflowExecutableStartSourceResolver(
    IWorkflowExecutableSourceReferenceStore sourceReferenceStore, TimeProvider timeProvider)
{
    /// <summary>Resolve ordinary eligibility; unavailable reads retain their cause and artifact/scope context.</summary>
    /// <exception cref="WorkflowExecutableReferenceRejectedException">No unique eligible reference matches.</exception>
    /// <exception cref="WorkflowExecutableSourceReadException">The authoritative reference read failed.</exception>
    public async ValueTask<WorkflowExecutableSourceProvenance?> ResolveAsync(string artifactId,
        WorkflowExecutableSourceSelection? selection, WorkflowExecutableProvenanceRequirement provenanceRequirement,
        WorkflowExecutableReferenceScope requiredScope, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyCollection<WorkflowExecutableSourceReference> references;
        try { references = await sourceReferenceStore.ListAllByArtifactAsync(artifactId, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (WorkflowExecutableSourceReadException) { throw; }
        catch (Exception exception) { throw new WorkflowExecutableSourceReadException(artifactId, requiredScope, exception); }
        return ResolveKnown(references, artifactId, selection, provenanceRequirement, requiredScope, timeProvider.GetUtcNow(), null);
    }

    private static WorkflowExecutableSourceProvenance? ResolveKnown(IReadOnlyCollection<WorkflowExecutableSourceReference> references,
        string artifactId, WorkflowExecutableSourceSelection? selection, WorkflowExecutableProvenanceRequirement provenanceRequirement,
        WorkflowExecutableReferenceScope requiredScope, DateTimeOffset now, PersistenceScope? accessScope)
    {
        if (references.Count == 0)
        {
            if (selection is null &&
                provenanceRequirement == WorkflowExecutableProvenanceRequirement.AllowReferenceLessLegacy)
                return null;

            throw new WorkflowExecutableReferenceRejectedException(
                artifactId,
                requiredScope,
                selection is null
                    ? WorkflowExecutableReferenceRejectionReason.NoLiveReference
                    : WorkflowExecutableReferenceRejectionReason.SelectionNotFound);
        }

        var scopedReferences = references.Where(reference => reference.Scope == requiredScope &&
            (accessScope is null || reference.TenantId is null || reference.TenantId == accessScope.Value)).ToArray();
        var selectedReferences = Select(scopedReferences, selection);
        var liveReferences = selectedReferences
            .Where(reference => reference.IsLive(now))
            .OrderBy(reference => reference.SourceReferenceId, StringComparer.Ordinal)
            .ToArray();

        if (liveReferences.Length == 1)
            return WorkflowExecutableSourceProvenance.From(liveReferences[0]);

        if (liveReferences.Length > 1)
            throw new WorkflowExecutableReferenceRejectedException(
                artifactId,
                requiredScope,
                WorkflowExecutableReferenceRejectionReason.Ambiguous);

        if (selection is not null && selectedReferences.Length == 0)
            throw new WorkflowExecutableReferenceRejectedException(
                artifactId,
                requiredScope,
                WorkflowExecutableReferenceRejectionReason.SelectionNotFound);

        // No live reference of the required scope. Distinguish the test-run-lapsed case (a non-retired reference
        // that is present but past its expiry) from the absent/retired/wrong-scope case, so an expired test run is
        // reported honestly rather than as an unpublished artifact.
        var expired = selectedReferences.Any(reference => reference.DeletedAt is null && reference.IsExpired(now));
        throw new WorkflowExecutableReferenceRejectedException(
            artifactId,
            requiredScope,
            expired ? WorkflowExecutableReferenceRejectionReason.Expired : WorkflowExecutableReferenceRejectionReason.NoLiveReference);
    }

    /// <summary>Capture a binding using ordinary published selection, without recipient effects.</summary>
    /// <exception cref="WorkflowExecutableReferenceRejectedException">No unique eligible publication matches.</exception>
    /// <exception cref="WorkflowExecutableSourceReadException">The authoritative reference read failed.</exception>
    public ValueTask<StimulusStartRecipient> CaptureAsync(WorkflowTriggerBinding binding,
        WorkflowExecutableIdentity executable, CancellationToken cancellationToken = default) => CaptureCoreAsync(binding, executable, null, cancellationToken);

    /// <summary>Capture ordinary publication selection within the actual owning persistence scope.</summary>
    /// <exception cref="WorkflowExecutableReferenceRejectedException">No unique live publication is eligible in this scope.</exception>
    /// <exception cref="WorkflowExecutableSourceReadException">The reference read failed; preserves its cause.</exception>
    /// <exception cref="ArgumentException">A binding, executable or scope argument is invalid.</exception>
    /// <exception cref="OperationCanceledException">Requested cancellation propagates.</exception>
    public ValueTask<StimulusStartRecipient> CaptureInScopeAsync(WorkflowTriggerBinding binding,
        WorkflowExecutableIdentity executable, PersistenceScope scope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return CaptureCoreAsync(binding, executable, scope, cancellationToken);
    }

    private async ValueTask<StimulusStartRecipient> CaptureCoreAsync(WorkflowTriggerBinding binding,
        WorkflowExecutableIdentity executable, PersistenceScope? accessScope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(binding); ArgumentNullException.ThrowIfNull(executable);
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyCollection<WorkflowExecutableSourceReference> references;
        try { references = await sourceReferenceStore.ListAllByArtifactAsync(binding.ArtifactId, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (WorkflowExecutableSourceReadException) { throw; }
        catch (Exception exception) { throw new WorkflowExecutableSourceReadException(binding.ArtifactId, WorkflowExecutableReferenceScope.Published, exception); }
        return CaptureKnown(binding, executable, references, accessScope, timeProvider.GetUtcNow());
    }

    private static StimulusStartRecipient CaptureKnown(WorkflowTriggerBinding binding, WorkflowExecutableIdentity executable,
        IReadOnlyCollection<WorkflowExecutableSourceReference> references, PersistenceScope? accessScope, DateTimeOffset now)
    {
        var selection = binding.ActivationId is null && binding.SlotId is null ? null :
            new WorkflowExecutableSourceSelection(activationId: binding.ActivationId, slotId: binding.SlotId);
        var source = ResolveKnown(references, binding.ArtifactId, selection, WorkflowExecutableProvenanceRequirement.AllowReferenceLessLegacy,
            WorkflowExecutableReferenceScope.Published, now, accessScope);
        return new(binding, executable, source);
    }

    /// <summary>Validate every supplied capture against the same ordinary current publication selector before retention acceptance.</summary>
    /// <exception cref="WorkflowExecutableReferenceRejectedException">A captured publication is no longer eligible.</exception>
    /// <exception cref="WorkflowExecutableSourceReadException">An authoritative reference read failed.</exception>
    /// <exception cref="InvalidDataException">A capture is absent or differs from ordinary selection.</exception>
    public ValueTask ValidateCapturedAsync(IReadOnlyList<StimulusStartRecipient> recipients,
        CancellationToken cancellationToken = default) => ValidateCapturedCoreAsync(recipients, null, cancellationToken);

    /// <summary>Validate captured publications within the actual source acceptance scope.</summary>
    /// <exception cref="WorkflowExecutableReferenceRejectedException">The captured publication is no longer eligible in this scope.</exception>
    /// <exception cref="WorkflowExecutableSourceReadException">The reference read failed; preserves its cause.</exception>
    /// <exception cref="InvalidDataException">A capture is absent or differs from the current eligible publication.</exception>
    /// <exception cref="OperationCanceledException">Requested cancellation propagates.</exception>
    public ValueTask ValidateCapturedInScopeAsync(IReadOnlyList<StimulusStartRecipient> recipients, PersistenceScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return ValidateCapturedCoreAsync(recipients, scope, cancellationToken);
    }

    private async ValueTask ValidateCapturedCoreAsync(IReadOnlyList<StimulusStartRecipient> recipients, PersistenceScope? scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recipients);
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var recipient in recipients)
        {
            if (!recipient.SourceProvenanceCaptured) throw new InvalidDataException("A retained cohort requires an actual ordinary source capture for every start.");
            var selected = await CaptureCoreAsync(recipient.Binding, recipient.Executable, scope, cancellationToken);
            if (selected.SourceProvenance != recipient.SourceProvenance)
                throw new InvalidDataException("The captured source differs from ordinary authoritative publication selection.");
        }
    }

    internal static void ValidateCapturedUnderReferenceGate(IReadOnlyList<StimulusStartRecipient> recipients,
        IReadOnlyCollection<WorkflowExecutableSourceReference> references, PersistenceScope scope, DateTimeOffset now)
    {
        foreach (var recipient in recipients)
        {
            if (!recipient.SourceProvenanceCaptured) throw new InvalidDataException("The selected publication is uncaptured.");
            var selected = CaptureKnown(recipient.Binding, recipient.Executable,
                references.Where(reference => reference.ArtifactId == recipient.Executable.ArtifactId).ToArray(), scope, now);
            if (selected.SourceProvenance != recipient.SourceProvenance)
                throw new InvalidDataException("The captured publication changed before atomic acceptance.");
        }
    }

    private static WorkflowExecutableSourceReference[] Select(
        WorkflowExecutableSourceReference[] references,
        WorkflowExecutableSourceSelection? selection)
    {
        if (selection is null)
            return references;
        if (selection.SourceReferenceId is { } sourceReferenceId)
            return references.Where(reference => StringComparer.Ordinal.Equals(reference.SourceReferenceId, sourceReferenceId)).ToArray();

        return references
            .Where(reference => selection.ActivationId is null || StringComparer.Ordinal.Equals(reference.ActivationId, selection.ActivationId))
            .Where(reference => selection.SlotId is null || StringComparer.Ordinal.Equals(reference.SlotId, selection.SlotId))
            .ToArray();
    }

}
