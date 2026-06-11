namespace Elsa.Workflows.Runtime.Core.Models;

public sealed class WorkflowExecutionStartDispatchRequest
{
    public WorkflowExecutionStartDispatchRequest(
        string artifactId,
        string? workflowExecutionId = null,
        string? idempotencyKey = null,
        string requestedBy = "runtime-api",
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedBy);

        if (workflowExecutionId is not null && string.IsNullOrWhiteSpace(workflowExecutionId))
            throw new ArgumentException("Workflow execution ID cannot be blank when provided.", nameof(workflowExecutionId));

        if (idempotencyKey is not null && string.IsNullOrWhiteSpace(idempotencyKey))
            throw new ArgumentException("Idempotency key cannot be blank when provided.", nameof(idempotencyKey));

        ArtifactId = artifactId;
        WorkflowExecutionId = workflowExecutionId;
        IdempotencyKey = idempotencyKey;
        RequestedBy = requestedBy;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
    }

    public string ArtifactId { get; }
    public string? WorkflowExecutionId { get; }
    public string? IdempotencyKey { get; }
    public string RequestedBy { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
}

public sealed class WorkflowExecutionStartCommandPayload
{
    public WorkflowExecutionStartCommandPayload(
        WorkflowExecutableIdentity pinnedExecutable,
        string requestedArtifactId)
    {
        ArgumentNullException.ThrowIfNull(pinnedExecutable);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedArtifactId);

        PinnedExecutable = pinnedExecutable;
        RequestedArtifactId = requestedArtifactId;
    }

    public WorkflowExecutableIdentity PinnedExecutable { get; }
    public string RequestedArtifactId { get; }
}

public sealed class WorkflowExecutionStartDispatchResult
{
    public WorkflowExecutionStartDispatchResult(
        string workflowExecutionId,
        WorkflowExecutableIdentity pinnedExecutable,
        WorkflowExecutionCommandDispatchResult commandDispatch,
        WorkflowExecutionAgentDescriptor agent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentNullException.ThrowIfNull(pinnedExecutable);
        ArgumentNullException.ThrowIfNull(commandDispatch);
        ArgumentNullException.ThrowIfNull(agent);

        if (!string.Equals(workflowExecutionId, commandDispatch.WorkflowExecutionId, StringComparison.Ordinal))
            throw new ArgumentException("Start dispatch result workflow execution ID must match command dispatch result.", nameof(commandDispatch));

        if (!string.Equals(workflowExecutionId, agent.WorkflowExecutionId, StringComparison.Ordinal))
            throw new ArgumentException("Start dispatch result workflow execution ID must match agent descriptor.", nameof(agent));

        WorkflowExecutionId = workflowExecutionId;
        PinnedExecutable = pinnedExecutable;
        CommandDispatch = commandDispatch;
        Agent = agent;
    }

    public string WorkflowExecutionId { get; }
    public WorkflowExecutableIdentity PinnedExecutable { get; }
    public WorkflowExecutionCommandDispatchResult CommandDispatch { get; }
    public WorkflowExecutionAgentDescriptor Agent { get; }
}
