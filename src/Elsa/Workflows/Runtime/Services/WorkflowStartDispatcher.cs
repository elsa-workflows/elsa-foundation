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
    private readonly IWorkflowExecutableStartPolicy _startPolicy;
    private readonly IWorkflowDispatchStore? _workflowDispatchStore;
    private readonly IWorkflowExecutionStateStore? _workflowExecutionStateStore;

    public WorkflowStartDispatcher(
        IWorkflowExecutableStore executableStore,
        IWorkflowExecutableSourceReferenceStore sourceReferenceStore,
        IWorkflowExecutionActorProvider agentProvider,
        IRuntimeExecutionIdGenerator idGenerator)
        : this(executableStore, sourceReferenceStore, agentProvider, idGenerator, TimeProvider.System, null, new AllowWorkflowExecutableStartPolicy())
    {
    }

    public WorkflowStartDispatcher(
        IWorkflowExecutableStore executableStore,
        IWorkflowExecutableSourceReferenceStore sourceReferenceStore,
        IWorkflowExecutionActorProvider agentProvider,
        IRuntimeExecutionIdGenerator idGenerator,
        TimeProvider timeProvider)
        : this(executableStore, sourceReferenceStore, agentProvider, idGenerator, timeProvider, null, new AllowWorkflowExecutableStartPolicy())
    {
    }

    public WorkflowStartDispatcher(
        IWorkflowExecutableStore executableStore,
        IWorkflowExecutableSourceReferenceStore sourceReferenceStore,
        IWorkflowExecutionActorProvider agentProvider,
        IRuntimeExecutionIdGenerator idGenerator,
        TimeProvider timeProvider,
        IWorkflowExecutionPartitionAccessor? partitionAccessor)
        : this(executableStore, sourceReferenceStore, agentProvider, idGenerator, timeProvider, partitionAccessor, new AllowWorkflowExecutableStartPolicy())
    {
    }

    public WorkflowStartDispatcher(
        IWorkflowExecutableStore executableStore,
        IWorkflowExecutableSourceReferenceStore sourceReferenceStore,
        IWorkflowExecutionActorProvider agentProvider,
        IRuntimeExecutionIdGenerator idGenerator,
        TimeProvider timeProvider,
        IWorkflowExecutionPartitionAccessor? partitionAccessor,
        IWorkflowExecutableStartPolicy startPolicy)
        : this(
            executableStore,
            sourceReferenceStore,
            agentProvider,
            idGenerator,
            timeProvider,
            partitionAccessor,
            startPolicy,
            null,
            null)
    {
    }

    public WorkflowStartDispatcher(
        IWorkflowExecutableStore executableStore,
        IWorkflowExecutableSourceReferenceStore sourceReferenceStore,
        IWorkflowExecutionActorProvider agentProvider,
        IRuntimeExecutionIdGenerator idGenerator,
        TimeProvider timeProvider,
        IWorkflowExecutionPartitionAccessor? partitionAccessor,
        IWorkflowExecutableStartPolicy startPolicy,
        IWorkflowDispatchStore? workflowDispatchStore,
        IWorkflowExecutionStateStore? workflowExecutionStateStore)
    {
        ArgumentNullException.ThrowIfNull(executableStore);
        ArgumentNullException.ThrowIfNull(sourceReferenceStore);
        ArgumentNullException.ThrowIfNull(agentProvider);
        ArgumentNullException.ThrowIfNull(idGenerator);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(startPolicy);

        _executableStore = executableStore;
        _sourceReferenceStore = sourceReferenceStore;
        _agentProvider = agentProvider;
        _idGenerator = idGenerator;
        _timeProvider = timeProvider;
        _partitionAccessor = partitionAccessor;
        _startPolicy = startPolicy;
        _workflowDispatchStore = workflowDispatchStore;
        _workflowExecutionStateStore = workflowExecutionStateStore;
    }

    public async ValueTask<WorkflowExecutionStartDispatchResult> DispatchAsync(
        WorkflowExecutionStartDispatchRequest request,
        WorkflowExecutableReferenceScope requiredScope = WorkflowExecutableReferenceScope.Published,
        WorkflowExecutionCommandDispatchOptions? dispatchOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.DispatchNestingDepth < 0)
            throw new ArgumentOutOfRangeException(nameof(request), "Dispatch nesting depth cannot be negative.");

        var executable = await _executableStore.FindAsync(request.ArtifactId, cancellationToken)
            ?? throw new WorkflowExecutableNotFoundException(request.ArtifactId);

        var resolved = request.StartAuthority?.Kind == WorkflowExecutableStartAuthorityKind.RetainedDependency
            ? await ResolveRetainedDependencyAsync(request, executable, cancellationToken)
            : await ResolvePinnedExecutableAsync(request, executable, requiredScope, cancellationToken);
        var partition = request.Partition ?? CurrentPartition();
        if (resolved.Dispatch is not null && _workflowExecutionStateStore is not null)
        {
            var workflowExecutionId = request.WorkflowExecutionId!;
            var existing = await _workflowExecutionStateStore.FindAsync(workflowExecutionId, cancellationToken);
            if (existing is not null)
            {
                return ExistingDispatchResult(
                    existing,
                    resolved.Dispatch,
                    resolved.Identity,
                    resolved.Source,
                    request.DispatchNestingDepth);
            }
        }

        await EnforceStartPolicyAsync(request, resolved.Identity, partition, cancellationToken);
        return await DispatchCoreAsync(
            request,
            resolved.Identity,
            resolved.Source,
            resolved.Dispatch,
            partition,
            requiredScope,
            dispatchOptions,
            cancellationToken);
    }

    private async ValueTask<ResolvedPinnedExecutable> ResolveRetainedDependencyAsync(
        WorkflowExecutionStartDispatchRequest request,
        WorkflowExecutable child,
        CancellationToken cancellationToken)
    {
        var authority = request.StartAuthority!.RetainedDependency!;
        var parent = await _executableStore.FindAsync(authority.ParentArtifactId, cancellationToken);
        if (parent is null)
        {
            throw AuthorityRejected(
                "retained-dependency-parent-not-found",
                "The retained parent executable is unavailable.");
        }

        if (!StringComparer.Ordinal.Equals(parent.Identity.ArtifactHash, authority.ParentArtifactHash))
        {
            throw AuthorityRejected(
                "retained-dependency-parent-hash-mismatch",
                "The retained parent executable does not match the authorized immutable identity.");
        }

        var exactEdge = parent.Dependencies.Any(dependency =>
            StringComparer.Ordinal.Equals(dependency.ArtifactId, child.Identity.ArtifactId) &&
            StringComparer.Ordinal.Equals(dependency.ArtifactHash, child.Identity.ArtifactHash) &&
            dependency.DispatchNodeIds.Contains(authority.DispatchNodeId, StringComparer.Ordinal));
        if (!exactEdge)
        {
            throw AuthorityRejected(
                "retained-dependency-edge-mismatch",
                "The retained parent does not authorize this exact child executable and dispatch node.");
        }

        if (_workflowDispatchStore is null)
            return new(child.Identity, null, null);
        if (request.ParentWorkflowExecutionId is null || request.WorkflowExecutionId is null || request.Partition is null)
        {
            throw AuthorityRejected(
                "retained-dependency-dispatch-context-missing",
                "The retained dependency start is missing committed dispatch context.");
        }

        var committedDispatches = await _workflowDispatchStore.ListAsync(request.ParentWorkflowExecutionId, cancellationToken);
        var identityCandidates = committedDispatches.Where(dispatch =>
            StringComparer.Ordinal.Equals(dispatch.ChildWorkflowExecutionId, request.WorkflowExecutionId) &&
            WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(dispatch.ChildExecutable, child.Identity))
            .ToArray();
        var committedDispatch = identityCandidates.SingleOrDefault(dispatch =>
            StringComparer.Ordinal.Equals(dispatch.CorrelationId, request.CorrelationId) &&
            StringComparer.Ordinal.Equals(dispatch.TenantId, request.TenantId) &&
            Equals(dispatch.Partition, request.Partition) &&
            dispatch.RunKind == request.RunKind &&
            dispatch.DispatchNestingDepth == request.DispatchNestingDepth &&
            WorkflowTestScope.ContextEquals(dispatch.TestScope, request.TestScope) &&
            AuthorityEquals(request.Authority, dispatch.Authority));
        if (committedDispatch is null)
        {
            var mismatch = identityCandidates.Length == 1
                ? $" Context mismatches: {DescribeDispatchContextMismatch(identityCandidates[0], request)}."
                : $" Matching identity candidates: {identityCandidates.Length}.";
            throw AuthorityRejected(
                "retained-dependency-dispatch-not-found",
                "No committed dispatch authorizes this retained dependency start." + mismatch);
        }

        // Retained-dependency authority is the immutable provenance for this start. Historical source
        // provenance remains on the dispatch record for inspection, but must not be copied into the start
        // command because the canonical payload deliberately makes those authority modes mutually exclusive.
        return new(child.Identity, null, committedDispatch);
    }

    private static string DescribeDispatchContextMismatch(
        WorkflowDispatchRecord dispatch,
        WorkflowExecutionStartDispatchRequest request)
    {
        var mismatches = new List<string>();
        if (!StringComparer.Ordinal.Equals(dispatch.CorrelationId, request.CorrelationId)) mismatches.Add(nameof(request.CorrelationId));
        if (!StringComparer.Ordinal.Equals(dispatch.TenantId, request.TenantId)) mismatches.Add(nameof(request.TenantId));
        if (!Equals(dispatch.Partition, request.Partition)) mismatches.Add(nameof(request.Partition));
        if (dispatch.RunKind != request.RunKind) mismatches.Add(nameof(request.RunKind));
        if (dispatch.DispatchNestingDepth != request.DispatchNestingDepth) mismatches.Add(nameof(request.DispatchNestingDepth));
        if (!WorkflowTestScope.ContextEquals(dispatch.TestScope, request.TestScope)) mismatches.Add(nameof(request.TestScope));
        if (!AuthorityEquals(request.Authority, dispatch.Authority)) mismatches.Add(nameof(request.Authority));
        return mismatches.Count == 0 ? "none (multiple candidates)" : string.Join(", ", mismatches);
    }

    private async ValueTask EnforceStartPolicyAsync(
        WorkflowExecutionStartDispatchRequest request,
        WorkflowExecutableIdentity executable,
        WorkflowExecutionPartition partition,
        CancellationToken cancellationToken)
    {
        var context = new WorkflowExecutableStartPolicyContext(
            executable,
            request.StartAuthority?.Kind ?? WorkflowExecutableStartAuthorityKind.LiveReference,
            request.RequestedBy,
            request.Authority,
            request.RunKind,
            request.TenantId,
            partition,
            request.DispatchNestingDepth);
        var decision = await _startPolicy.EvaluateAsync(context, cancellationToken)
            ?? throw new InvalidOperationException($"{nameof(IWorkflowExecutableStartPolicy)} returned no decision.");
        if (!decision.IsAllowed)
        {
            throw new WorkflowExecutableStartRejectedException(
                WorkflowExecutableStartRejectionCategory.Policy,
                decision.ReasonCode!,
                decision.Message!);
        }
    }

    private static WorkflowExecutableStartRejectedException AuthorityRejected(string reasonCode, string message) =>
        new(WorkflowExecutableStartRejectionCategory.Authority, reasonCode, message);

    // Resolves the artifact's Source References and returns content identity plus independently pinned source
    // attribution. This is deliberately fail-closed: the content artifact's identity can reflect whichever source
    // first produced deduplicated content, so it is never authoritative publication provenance on its own.
    private async ValueTask<ResolvedPinnedExecutable> ResolvePinnedExecutableAsync(
        WorkflowExecutionStartDispatchRequest request,
        WorkflowExecutable executable,
        WorkflowExecutableReferenceScope requiredScope,
        CancellationToken cancellationToken)
    {
        var references = await _sourceReferenceStore.ListAllByArtifactAsync(request.ArtifactId, cancellationToken);
        if (references.Count == 0)
        {
            if (request.SourceSelection is null &&
                request.ProvenanceRequirement == WorkflowExecutableProvenanceRequirement.AllowReferenceLessLegacy)
                return new(executable.Identity, null, null);

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
                WorkflowExecutableSourceProvenance.From(liveReferences[0]),
                null);

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
        WorkflowDispatchRecord? retainedDispatch,
        WorkflowExecutionPartition partition,
        WorkflowExecutableReferenceScope requiredScope,
        WorkflowExecutionCommandDispatchOptions? dispatchOptions,
        CancellationToken cancellationToken)
    {
        var workflowExecutionId = request.WorkflowExecutionId ?? _idGenerator.NewWorkflowExecutionId();
        var requestedAt = _timeProvider.GetUtcNow();
        var enqueuedAt = retainedDispatch?.CreatedAt ?? requestedAt;
        var metadata = CreateDispatchMetadata(request, pinnedIdentity, pinnedSource, retainedDispatch, requiredScope);
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
            authority: request.Authority,
            startAuthority: request.StartAuthority,
            dispatchNestingDepth: request.DispatchNestingDepth,
            testScope: request.TestScope));

        var command = new WorkflowExecutionCommand(
            CommandId: retainedDispatch is null
                ? _idGenerator.NewWorkflowExecutionCommandId()
                : $"{retainedDispatch.DispatchId}:command:start",
            WorkflowExecutionId: workflowExecutionId,
            Kind: WorkflowExecutionCommandKind.Start,
            EnqueuedAt: enqueuedAt,
            Payload: payload.Clone(),
            Metadata: metadata);

        var envelope = new WorkflowExecutionCommandEnvelope(
            envelopeId: retainedDispatch is null
                ? _idGenerator.NewWorkflowExecutionCommandEnvelopeId()
                : $"{retainedDispatch.DispatchId}:envelope:start",
            workflowExecutionId: workflowExecutionId,
            command: command,
            idempotencyKey: request.IdempotencyKey ?? CreateDefaultIdempotencyKey(workflowExecutionId, pinnedIdentity.ArtifactId),
            deliveryMode: WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            enqueuedAt: enqueuedAt,
            metadata: metadata,
            partition: partition);

        var activationRequest = new WorkflowExecutionActorActivationRequest(
            workflowExecutionId: workflowExecutionId,
            reason: WorkflowExecutionActorActivationReason.Start,
            requestedAt: requestedAt,
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
        WorkflowExecutableSourceProvenance? source,
        WorkflowDispatchRecord? retainedDispatch,
        WorkflowExecutableReferenceScope requiredScope)
    {
        var metadata = request.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        // Diagnostic breadcrumb only — never read back or matched, so it is safe for this value to track the type name.
        metadata["runtime.dispatcher"] = nameof(WorkflowStartDispatcher);
        metadata["runtime.artifactId"] = identity.ArtifactId;
        metadata["runtime.artifactVersion"] = source?.ArtifactVersion ?? identity.ArtifactVersion;
        metadata["runtime.artifactHash"] = identity.ArtifactHash;
        metadata[RuntimeMetadataKeys.WorkflowExecutionOrigin] = requiredScope.ToString();
        if (source?.SourceReferenceId is { } sourceReferenceId)
            metadata[RuntimeMetadataKeys.SourceReferenceId] = sourceReferenceId;
        if (retainedDispatch is not null)
            metadata[RuntimeMetadataKeys.WorkflowDispatchId] = retainedDispatch.DispatchId;
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
        WorkflowExecutableSourceProvenance? Source,
        WorkflowDispatchRecord? Dispatch);

    private static WorkflowExecutionStartDispatchResult ExistingDispatchResult(
        WorkflowExecutionState existing,
        WorkflowDispatchRecord dispatch,
        WorkflowExecutableIdentity pinnedIdentity,
        WorkflowExecutableSourceProvenance? pinnedSource,
        int dispatchNestingDepth)
    {
        var exact = WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(existing.PinnedExecutable, pinnedIdentity) &&
            Equals(existing.PinnedSource, pinnedSource) &&
            StringComparer.Ordinal.Equals(existing.ParentWorkflowExecutionId, dispatch.ParentWorkflowExecutionId) &&
            StringComparer.Ordinal.Equals(existing.CorrelationId, dispatch.CorrelationId) &&
            StringComparer.Ordinal.Equals(existing.TenantId, dispatch.TenantId) &&
            Equals(existing.Partition, dispatch.Partition) &&
            existing.RunKind == dispatch.RunKind &&
            existing.DispatchNestingDepth == dispatchNestingDepth &&
            WorkflowTestScope.ContextEquals(existing.TestScope, dispatch.TestScope) &&
            AuthorityEquals(existing.Authority, dispatch.Authority);
        if (!exact)
            throw new InvalidOperationException($"Workflow execution '{existing.WorkflowExecutionId}' already exists with conflicting dispatch identity or context.");

        var envelopeId = $"{dispatch.DispatchId}:envelope:start";
        return new WorkflowExecutionStartDispatchResult(
            existing.WorkflowExecutionId,
            pinnedIdentity,
            new WorkflowExecutionCommandDispatchResult(
                envelopeId,
                existing.WorkflowExecutionId,
                WorkflowExecutionCommandDispatchStatus.Duplicate,
                existing.UpdatedAt ?? existing.StartedAt ?? existing.CreatedAt),
            new WorkflowExecutionActorDescriptor(
                existing.WorkflowExecutionId,
                $"existing:{existing.WorkflowExecutionId}",
                "durable-state",
                WorkflowExecutionActorStatus.Passivated,
                WorkflowExecutionActorCapabilities.None,
                existing.StartedAt ?? existing.CreatedAt),
            pinnedSource);
    }

    private static bool AuthorityEquals(
        WorkflowExecutionAuthoritySnapshot? existing,
        WorkflowExecutionAuthoritySnapshot expected) =>
        existing is not null &&
        StringComparer.Ordinal.Equals(existing.SystemIdentity, expected.SystemIdentity) &&
        StringComparer.Ordinal.Equals(existing.RootInitiator, expected.RootInitiator) &&
        existing.Metadata.Count == expected.Metadata.Count &&
        existing.Metadata.All(item => expected.Metadata.TryGetValue(item.Key, out var value) && StringComparer.Ordinal.Equals(item.Value, value));
}
