using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executables;

namespace Elsa.Workflows.Runtime.Services.Executions;

public sealed class WorkflowStartDispatcher : IWorkflowStartDispatcher
{
    private readonly IWorkflowExecutableStore _executableStore;
    private readonly WorkflowExecutableStartSourceResolver _startSourceResolver;
    private readonly IWorkflowExecutionActorProvider _agentProvider;
    private readonly IRuntimeExecutionIdGenerator _idGenerator;
    private readonly TimeProvider _timeProvider;
    private readonly IWorkflowExecutionPartitionAccessor? _partitionAccessor;
    private readonly IWorkflowExecutableStartPolicy _startPolicy;
    private readonly IWorkflowDispatchStore? _workflowDispatchStore;
    private readonly IWorkflowExecutionStateStore? _workflowExecutionStateStore;
    private readonly IRuntimePersistenceModeResolver? _persistenceModeResolver;

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
        IWorkflowExecutionStateStore? workflowExecutionStateStore,
        IRuntimePersistenceModeResolver? persistenceModeResolver = null)
    {
        ArgumentNullException.ThrowIfNull(executableStore);
        ArgumentNullException.ThrowIfNull(sourceReferenceStore);
        ArgumentNullException.ThrowIfNull(agentProvider);
        ArgumentNullException.ThrowIfNull(idGenerator);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(startPolicy);

        _executableStore = executableStore;
        _startSourceResolver = new(sourceReferenceStore, timeProvider);
        _agentProvider = agentProvider;
        _idGenerator = idGenerator;
        _timeProvider = timeProvider;
        _partitionAccessor = partitionAccessor;
        _startPolicy = startPolicy;
        _workflowDispatchStore = workflowDispatchStore;
        _workflowExecutionStateStore = workflowExecutionStateStore;
        _persistenceModeResolver = persistenceModeResolver;
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
        if (request.StartAuthority?.Kind == WorkflowExecutableStartAuthorityKind.StimulusDelivery)
            throw AuthorityRejected("stimulus-delivery-source-unavailable", "Saved stimulus authority requires verified provider admission.");

        // A keyed start that already ran is answered before anything else is resolved: the existing execution is the
        // answer even if the workflow has since been unpublished, so a redelivery converges instead of failing (#2195).
        var keyed = KeyedWorkflowStartIdentity.TryGet(request);
        if (keyed is not null && await FindKeyedStartAsync(request, keyed, cancellationToken) is { } duplicate)
            return duplicate;

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
            keyed,
            partition,
            requiredScope,
            _persistenceModeResolver?.Resolve(executable),
            dispatchOptions,
            cancellationToken);
    }

    private async ValueTask<WorkflowExecutionStartDispatchResult?> FindKeyedStartAsync(
        WorkflowExecutionStartDispatchRequest request,
        KeyedWorkflowStartIdentity keyed,
        CancellationToken cancellationToken)
    {
        // Without durable execution state a duplicate cannot be recognized, and re-running a start into an execution id
        // that already exists is worse than the double start keying prevents. Refuse it loudly.
        if (_workflowExecutionStateStore is null)
        {
            throw new InvalidOperationException(
                $"Keyed workflow start '{keyed.StartKey}' requires an {nameof(IWorkflowExecutionStateStore)} to recognize a duplicate delivery.");
        }

        var existing = await _workflowExecutionStateStore.FindAsync(keyed.WorkflowExecutionId, cancellationToken);
        if (existing is null)
            return null;
        // An occurrence start (#2198) names its occurrence whichever publication of the trigger fires it, so an execution a
        // replaced publication's artifact started for it is the duplicate, not a conflict.
        if (keyed.IsArtifactScoped && !StringComparer.Ordinal.Equals(existing.PinnedExecutable.ArtifactId, request.ArtifactId))
        {
            throw new InvalidOperationException(
                $"Workflow execution '{existing.WorkflowExecutionId}' already exists for artifact '{existing.PinnedExecutable.ArtifactId}', not for keyed start '{keyed.StartKey}' of artifact '{request.ArtifactId}'.");
        }

        return DuplicateStartResult(existing, keyed.EnvelopeId, existing.PinnedExecutable, existing.PinnedSource);
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

    private async ValueTask<ResolvedPinnedExecutable> ResolvePinnedExecutableAsync(
        WorkflowExecutionStartDispatchRequest request, WorkflowExecutable executable,
        WorkflowExecutableReferenceScope requiredScope, CancellationToken cancellationToken) =>
        new(executable.Identity, await _startSourceResolver.ResolveAsync(request.ArtifactId, request.SourceSelection,
            request.ProvenanceRequirement, requiredScope, cancellationToken), null);

    private async ValueTask<WorkflowExecutionStartDispatchResult> DispatchCoreAsync(
        WorkflowExecutionStartDispatchRequest request,
        WorkflowExecutableIdentity pinnedIdentity,
        WorkflowExecutableSourceProvenance? pinnedSource,
        WorkflowDispatchRecord? retainedDispatch,
        KeyedWorkflowStartIdentity? keyed,
        WorkflowExecutionPartition partition,
        WorkflowExecutableReferenceScope requiredScope,
        PersistenceMode? persistenceMode,
        WorkflowExecutionCommandDispatchOptions? dispatchOptions,
        CancellationToken cancellationToken)
    {
        var workflowExecutionId = request.WorkflowExecutionId ?? _idGenerator.NewWorkflowExecutionId();
        var requestedAt = _timeProvider.GetUtcNow();
        var enqueuedAt = retainedDispatch?.CreatedAt ?? requestedAt;
        var metadata = CreateDispatchMetadata(request, pinnedIdentity, pinnedSource, retainedDispatch, requiredScope, persistenceMode);
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
            testScope: request.TestScope,
            triggerMetadata: request.TriggerMetadata));

        // A retained dispatch and a keyed start both name their command and envelope deterministically, so a repeated
        // delivery enqueues the same start work item and the queue keeps the first.
        var command = new WorkflowExecutionCommand(
            CommandId: retainedDispatch is not null
                ? $"{retainedDispatch.DispatchId}:command:start"
                : keyed?.CommandId ?? _idGenerator.NewWorkflowExecutionCommandId(),
            WorkflowExecutionId: workflowExecutionId,
            Kind: WorkflowExecutionCommandKind.Start,
            EnqueuedAt: enqueuedAt,
            Payload: payload.Clone(),
            Metadata: metadata);

        var envelope = new WorkflowExecutionCommandEnvelope(
            envelopeId: retainedDispatch is not null
                ? $"{retainedDispatch.DispatchId}:envelope:start"
                : keyed?.EnvelopeId ?? _idGenerator.NewWorkflowExecutionCommandEnvelopeId(),
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
        WorkflowExecutableReferenceScope requiredScope,
        PersistenceMode? persistenceMode)
    {
        var metadata = request.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        // The run's persistence mode, decided here once: the workflow's choice over the host default. The scheduler queue
        // keeps it on the start until the run's first commit, so a drain that runs a never-committed run again finds the
        // same mode. A caller cannot supply it.
        metadata.Remove(RuntimeMetadataKeys.PersistenceMode);
        if (persistenceMode is { } mode)
            metadata[RuntimeMetadataKeys.PersistenceMode] = mode.ToString();
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

        return DuplicateStartResult(existing, $"{dispatch.DispatchId}:envelope:start", pinnedIdentity, pinnedSource);
    }

    private static WorkflowExecutionStartDispatchResult DuplicateStartResult(
        WorkflowExecutionState existing,
        string envelopeId,
        WorkflowExecutableIdentity pinnedIdentity,
        WorkflowExecutableSourceProvenance? pinnedSource) =>
        new(
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

    private static bool AuthorityEquals(
        WorkflowExecutionAuthoritySnapshot? existing,
        WorkflowExecutionAuthoritySnapshot expected) =>
        existing is not null &&
        StringComparer.Ordinal.Equals(existing.SystemIdentity, expected.SystemIdentity) &&
        StringComparer.Ordinal.Equals(existing.RootInitiator, expected.RootInitiator) &&
        existing.Metadata.Count == expected.Metadata.Count &&
        existing.Metadata.All(item => expected.Metadata.TryGetValue(item.Key, out var value) && StringComparer.Ordinal.Equals(item.Value, value));
}
