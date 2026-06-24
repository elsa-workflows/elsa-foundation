using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class WorkflowExecutionStartDispatcher : IWorkflowExecutionStartDispatcher
{
    private readonly IWorkflowExecutableStore _executableStore;
    private readonly IWorkflowExecutionAgentProvider _agentProvider;
    private readonly IRuntimeExecutionIdGenerator _idGenerator;
    private readonly TimeProvider _timeProvider;

    public WorkflowExecutionStartDispatcher(
        IWorkflowExecutableStore executableStore,
        IWorkflowExecutionAgentProvider agentProvider,
        IRuntimeExecutionIdGenerator idGenerator)
        : this(executableStore, agentProvider, idGenerator, TimeProvider.System)
    {
    }

    public WorkflowExecutionStartDispatcher(
        IWorkflowExecutableStore executableStore,
        IWorkflowExecutionAgentProvider agentProvider,
        IRuntimeExecutionIdGenerator idGenerator,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(executableStore);
        ArgumentNullException.ThrowIfNull(agentProvider);
        ArgumentNullException.ThrowIfNull(idGenerator);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _executableStore = executableStore;
        _agentProvider = agentProvider;
        _idGenerator = idGenerator;
        _timeProvider = timeProvider;
    }

    public async ValueTask<WorkflowExecutionStartDispatchResult> DispatchAsync(
        WorkflowExecutionStartDispatchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var executable = await _executableStore.FindAsync(request.ArtifactId, cancellationToken)
            ?? throw new WorkflowExecutableNotFoundException(request.ArtifactId);

        if (executable.Scope == WorkflowExecutableScope.TransientTestRun)
            throw new WorkflowExecutableNotFoundException(request.ArtifactId);

        return await DispatchCoreAsync(request, executable, cancellationToken);
    }

    public async ValueTask<WorkflowExecutionStartDispatchResult> DispatchTransientAsync(
        WorkflowExecutionStartDispatchRequest request,
        WorkflowExecutable executable,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(executable);

        if (executable.Scope != WorkflowExecutableScope.TransientTestRun)
            throw new ArgumentException("Transient dispatch requires a transient test-run executable.", nameof(executable));

        if (!string.Equals(request.ArtifactId, executable.Identity.ArtifactId, StringComparison.Ordinal))
            throw new ArgumentException("Transient dispatch request artifact ID must match the executable artifact ID.", nameof(request));

        await _executableStore.SaveAsync(executable, cancellationToken);

        return await DispatchCoreAsync(request, executable, cancellationToken);
    }

    private async ValueTask<WorkflowExecutionStartDispatchResult> DispatchCoreAsync(
        WorkflowExecutionStartDispatchRequest request,
        WorkflowExecutable executable,
        CancellationToken cancellationToken)
    {
        if (executable.ExpiresAt is { } expiresAt && expiresAt <= _timeProvider.GetUtcNow())
            throw new WorkflowExecutableNotFoundException(request.ArtifactId);

        var workflowExecutionId = request.WorkflowExecutionId ?? _idGenerator.NewWorkflowExecutionId();
        var now = _timeProvider.GetUtcNow();
        var metadata = CreateDispatchMetadata(request, executable.Identity);
        var payload = JsonSerializer.SerializeToElement(new WorkflowExecutionStartCommandPayload(
            pinnedExecutable: executable.Identity,
            requestedArtifactId: request.ArtifactId));

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
            idempotencyKey: request.IdempotencyKey ?? CreateDefaultIdempotencyKey(workflowExecutionId, executable.Identity.ArtifactId),
            deliveryMode: WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            enqueuedAt: now,
            metadata: metadata);

        var activationRequest = new WorkflowExecutionAgentActivationRequest(
            workflowExecutionId: workflowExecutionId,
            reason: WorkflowExecutionAgentActivationReason.Start,
            requestedAt: now,
            requestedBy: request.RequestedBy,
            requiredCapabilities: WorkflowExecutionAgentCapabilities.None,
            metadata: metadata);

        var agent = await _agentProvider.GetAgentAsync(activationRequest, cancellationToken);
        var dispatchResult = await agent.EnqueueAsync(envelope, cancellationToken);

        return new WorkflowExecutionStartDispatchResult(
            workflowExecutionId: workflowExecutionId,
            pinnedExecutable: executable.Identity,
            commandDispatch: dispatchResult,
            agent: agent.Descriptor);
    }

    private static IReadOnlyDictionary<string, string> CreateDispatchMetadata(
        WorkflowExecutionStartDispatchRequest request,
        WorkflowExecutableIdentity identity)
    {
        var metadata = request.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        metadata["runtime.dispatcher"] = nameof(WorkflowExecutionStartDispatcher);
        metadata["runtime.artifactId"] = identity.ArtifactId;
        metadata["runtime.artifactVersion"] = identity.ArtifactVersion;
        metadata["runtime.artifactHash"] = identity.ArtifactHash;
        return RuntimeModelMetadata.Snapshot(metadata);
    }

    private static string CreateDefaultIdempotencyKey(string workflowExecutionId, string artifactId) =>
        $"{workflowExecutionId}:start:{artifactId}";
}
