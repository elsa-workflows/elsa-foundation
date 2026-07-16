using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class WorkflowStartDispatcher : IWorkflowStartDispatcher
{
    private readonly IWorkflowExecutableStore _executableStore;
    private readonly IWorkflowExecutableSourceReferenceStore _sourceReferenceStore;
    private readonly IWorkflowExecutionActorProvider _agentProvider;
    private readonly IRuntimeExecutionIdGenerator _idGenerator;
    private readonly TimeProvider _timeProvider;
    private readonly IWorkflowExecutionPartitionAccessor? _partitionAccessor;

    public WorkflowStartDispatcher(
        IWorkflowExecutableStore executableStore,
        IWorkflowExecutableSourceReferenceStore sourceReferenceStore,
        IWorkflowExecutionActorProvider agentProvider,
        IRuntimeExecutionIdGenerator idGenerator)
        : this(executableStore, sourceReferenceStore, agentProvider, idGenerator, TimeProvider.System, null)
    {
    }

    public WorkflowStartDispatcher(
        IWorkflowExecutableStore executableStore,
        IWorkflowExecutableSourceReferenceStore sourceReferenceStore,
        IWorkflowExecutionActorProvider agentProvider,
        IRuntimeExecutionIdGenerator idGenerator,
        TimeProvider timeProvider)
        : this(executableStore, sourceReferenceStore, agentProvider, idGenerator, timeProvider, null)
    {
    }

    public WorkflowStartDispatcher(
        IWorkflowExecutableStore executableStore,
        IWorkflowExecutableSourceReferenceStore sourceReferenceStore,
        IWorkflowExecutionActorProvider agentProvider,
        IRuntimeExecutionIdGenerator idGenerator,
        TimeProvider timeProvider,
        IWorkflowExecutionPartitionAccessor? partitionAccessor)
    {
        ArgumentNullException.ThrowIfNull(executableStore);
        ArgumentNullException.ThrowIfNull(sourceReferenceStore);
        ArgumentNullException.ThrowIfNull(agentProvider);
        ArgumentNullException.ThrowIfNull(idGenerator);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _executableStore = executableStore;
        _sourceReferenceStore = sourceReferenceStore;
        _agentProvider = agentProvider;
        _idGenerator = idGenerator;
        _timeProvider = timeProvider;
        _partitionAccessor = partitionAccessor;
    }

    public async ValueTask<WorkflowExecutionStartDispatchResult> DispatchAsync(
        WorkflowExecutionStartDispatchRequest request,
        WorkflowExecutableReferenceScope requiredScope = WorkflowExecutableReferenceScope.Published,
        WorkflowExecutionCommandDispatchOptions? dispatchOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var executable = await _executableStore.FindAsync(request.ArtifactId, cancellationToken)
            ?? throw new WorkflowExecutableNotFoundException(request.ArtifactId);

        // Reference gate (ADR 0040): scope and expiry are reference facts, so dispatch resolves the Source
        // References for this artifact and gates on them. A published dispatch requires a live Published
        // reference; a test-run dispatch requires a live TestRun reference and enforces its ExpiresAt. The
        // rejection reason distinguishes "no live reference" from "reference expired".
        var resolved = await ResolvePinnedExecutableAsync(request, executable, requiredScope, cancellationToken);

        return await DispatchCoreAsync(request, resolved.Identity, resolved.Source, dispatchOptions, cancellationToken);
    }

    // Resolves the artifact's Source References and returns content identity plus independently pinned source
    // attribution. This is deliberately fail-closed: the content artifact's identity can reflect whichever source
    // first produced deduplicated content, so it is never authoritative publication provenance on its own.
    private async ValueTask<ResolvedPinnedExecutable> ResolvePinnedExecutableAsync(
        WorkflowExecutionStartDispatchRequest request,
        WorkflowExecutable executable,
        WorkflowExecutableReferenceScope requiredScope,
        CancellationToken cancellationToken)
    {
        var references = await _sourceReferenceStore.ListByArtifactAsync(request.ArtifactId, cancellationToken);
        if (references.Count == 0)
        {
            if (request.SourceSelection is null &&
                request.ProvenanceRequirement == WorkflowExecutableProvenanceRequirement.AllowReferenceLessLegacy)
                return new(executable.Identity, null);

            throw new WorkflowExecutableReferenceRejectedException(
                request.ArtifactId,
                requiredScope,
                request.SourceSelection is null
                    ? WorkflowExecutableReferenceRejectionReason.NoLiveReference
                    : WorkflowExecutableReferenceRejectionReason.SelectionNotFound);
        }

        var now = _timeProvider.GetUtcNow();
        var scopedReferences = references.Where(reference => reference.Scope == requiredScope).ToArray();
        var selectedReferences = Select(scopedReferences, request.SourceSelection);
        var liveReferences = selectedReferences
            .Where(reference => reference.IsLive(now))
            .OrderBy(reference => reference.SourceReferenceId, StringComparer.Ordinal)
            .ToArray();

        if (liveReferences.Length == 1)
            return new(
                executable.Identity,
                WorkflowExecutableSourceProvenance.From(liveReferences[0]));

        if (liveReferences.Length > 1)
            throw new WorkflowExecutableReferenceRejectedException(
                request.ArtifactId,
                requiredScope,
                WorkflowExecutableReferenceRejectionReason.Ambiguous);

        if (request.SourceSelection is not null && selectedReferences.Length == 0)
            throw new WorkflowExecutableReferenceRejectedException(
                request.ArtifactId,
                requiredScope,
                WorkflowExecutableReferenceRejectionReason.SelectionNotFound);

        // No live reference of the required scope. Distinguish the test-run-lapsed case (a non-retired reference
        // that is present but past its expiry) from the absent/retired/wrong-scope case, so an expired test run is
        // reported honestly rather than as an unpublished artifact.
        var expired = selectedReferences.Any(reference => reference.DeletedAt is null && reference.IsExpired(now));
        throw new WorkflowExecutableReferenceRejectedException(
            request.ArtifactId,
            requiredScope,
            expired ? WorkflowExecutableReferenceRejectionReason.Expired : WorkflowExecutableReferenceRejectionReason.NoLiveReference);
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
            .Where(reference => selection.PublicationId is null || StringComparer.Ordinal.Equals(reference.PublicationId, selection.PublicationId))
            .Where(reference => selection.SlotId is null || StringComparer.Ordinal.Equals(reference.SlotId, selection.SlotId))
            .ToArray();
    }

    private async ValueTask<WorkflowExecutionStartDispatchResult> DispatchCoreAsync(
        WorkflowExecutionStartDispatchRequest request,
        WorkflowExecutableIdentity pinnedIdentity,
        WorkflowExecutableSourceProvenance? pinnedSource,
        WorkflowExecutionCommandDispatchOptions? dispatchOptions,
        CancellationToken cancellationToken)
    {
        var workflowExecutionId = request.WorkflowExecutionId ?? _idGenerator.NewWorkflowExecutionId();
        var now = _timeProvider.GetUtcNow();
        var partition = request.Partition ?? CurrentPartition();
        var metadata = CreateDispatchMetadata(request, pinnedIdentity, pinnedSource);
        var payload = JsonSerializer.SerializeToElement(new WorkflowExecutionStartCommandPayload(
            pinnedExecutable: pinnedIdentity,
            requestedArtifactId: request.ArtifactId,
            variables: WorkflowExecutionStartCommandPayload.ToJsonValues(request.Variables),
            inputs: WorkflowExecutionStartCommandPayload.ToJsonValues(request.Inputs),
            stimulusInput: request.StimulusInput,
            triggerNodeId: request.TriggerNodeId,
            runKind: request.RunKind,
            pinnedSource: pinnedSource,
            parentWorkflowExecutionId: request.ParentWorkflowExecutionId,
            correlationId: request.CorrelationId,
            tenantId: request.TenantId,
            partition: partition,
            authority: request.Authority));

        var command = new WorkflowExecutionCommand(
            CommandId: _idGenerator.NewWorkflowExecutionCommandId(),
            WorkflowExecutionId: workflowExecutionId,
            Kind: WorkflowExecutionCommandKind.Start,
            EnqueuedAt: now,
            Payload: payload.Clone(),
            Metadata: metadata);

        var envelope = new WorkflowExecutionCommandEnvelope(
            envelopeId: _idGenerator.NewWorkflowExecutionCommandEnvelopeId(),
            workflowExecutionId: workflowExecutionId,
            command: command,
            idempotencyKey: request.IdempotencyKey ?? CreateDefaultIdempotencyKey(workflowExecutionId, pinnedIdentity.ArtifactId),
            deliveryMode: WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            enqueuedAt: now,
            metadata: metadata,
            partition: partition);

        var activationRequest = new WorkflowExecutionActorActivationRequest(
            workflowExecutionId: workflowExecutionId,
            reason: WorkflowExecutionActorActivationReason.Start,
            requestedAt: now,
            requestedBy: request.RequestedBy,
            requiredCapabilities: WorkflowExecutionActorCapabilities.None,
            metadata: metadata,
            partition: partition);

        var agent = await _agentProvider.GetAgentAsync(activationRequest, cancellationToken);
        var dispatchResult = await agent.EnqueueAsync(envelope, dispatchOptions ?? WorkflowExecutionCommandDispatchOptions.Default, cancellationToken);

        return new WorkflowExecutionStartDispatchResult(
            workflowExecutionId: workflowExecutionId,
            pinnedExecutable: pinnedIdentity,
            commandDispatch: dispatchResult,
            agent: agent.Descriptor,
            pinnedSource: pinnedSource);
    }

    private static IReadOnlyDictionary<string, string> CreateDispatchMetadata(
        WorkflowExecutionStartDispatchRequest request,
        WorkflowExecutableIdentity identity,
        WorkflowExecutableSourceProvenance? source)
    {
        var metadata = request.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        // Diagnostic breadcrumb only — never read back or matched, so it is safe for this value to track the type name.
        metadata["runtime.dispatcher"] = nameof(WorkflowStartDispatcher);
        metadata["runtime.artifactId"] = identity.ArtifactId;
        metadata["runtime.artifactVersion"] = source?.ArtifactVersion ?? identity.ArtifactVersion;
        metadata["runtime.artifactHash"] = identity.ArtifactHash;
        if (source?.SourceReferenceId is { } sourceReferenceId)
            metadata[RuntimeMetadataKeys.SourceReferenceId] = sourceReferenceId;
        return RuntimeModelMetadata.Snapshot(metadata);
    }

    private static string CreateDefaultIdempotencyKey(string workflowExecutionId, string artifactId) =>
        $"{workflowExecutionId}:start:{artifactId}";

    private WorkflowExecutionPartition CurrentPartition()
    {
        if (_partitionAccessor is null)
            return new WorkflowExecutionPartition(WorkflowExecutionPartition.DefaultValue);

        return _partitionAccessor.Current;
    }

    private sealed record ResolvedPinnedExecutable(
        WorkflowExecutableIdentity Identity,
        WorkflowExecutableSourceProvenance? Source);
}
