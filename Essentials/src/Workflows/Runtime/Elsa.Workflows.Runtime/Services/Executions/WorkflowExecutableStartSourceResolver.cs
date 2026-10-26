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

        var now = timeProvider.GetUtcNow();
        var scopedReferences = references.Where(reference => reference.Scope == requiredScope).ToArray();
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
    public async ValueTask<StimulusStartRecipient> CaptureAsync(WorkflowTriggerBinding binding,
        WorkflowExecutableIdentity executable, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding); ArgumentNullException.ThrowIfNull(executable);
        var selection = binding.ActivationId is null && binding.SlotId is null ? null :
            new WorkflowExecutableSourceSelection(activationId: binding.ActivationId, slotId: binding.SlotId);
        var source = await ResolveAsync(binding.ArtifactId, selection,
            WorkflowExecutableProvenanceRequirement.AllowReferenceLessLegacy, WorkflowExecutableReferenceScope.Published,
            cancellationToken);
        return new(binding, executable, source);
    }

    /// <summary>Validate every supplied capture against the same ordinary current publication selector before retention acceptance.</summary>
    /// <exception cref="WorkflowExecutableReferenceRejectedException">A captured publication is no longer eligible.</exception>
    /// <exception cref="WorkflowExecutableSourceReadException">An authoritative reference read failed.</exception>
    /// <exception cref="InvalidDataException">A capture is absent or differs from ordinary selection.</exception>
    public async ValueTask ValidateCapturedAsync(IReadOnlyList<StimulusStartRecipient> recipients,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recipients);
        foreach (var recipient in recipients)
        {
            if (!recipient.SourceProvenanceCaptured)
                throw new InvalidDataException("A retained cohort requires an actual ordinary source capture for every start.");
            var selected = await CaptureAsync(recipient.Binding, recipient.Executable, cancellationToken);
            if (selected.SourceProvenance != recipient.SourceProvenance)
                throw new InvalidDataException("The captured source differs from ordinary authoritative publication selection.");
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
