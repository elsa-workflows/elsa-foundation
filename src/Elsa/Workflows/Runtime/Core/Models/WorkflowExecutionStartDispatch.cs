using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Core.Models;

public sealed class WorkflowExecutionStartDispatchRequest
{
    public WorkflowExecutionStartDispatchRequest(
        string artifactId,
        string requestedBy,
        string? workflowExecutionId = null,
        string? idempotencyKey = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        IReadOnlyDictionary<string, object?>? variables = null,
        IReadOnlyDictionary<string, object?>? inputs = null)
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
        Variables = SnapshotValues(variables);
        Inputs = SnapshotValues(inputs);
    }

    public string ArtifactId { get; }
    public string? WorkflowExecutionId { get; }
    public string? IdempotencyKey { get; }
    public string RequestedBy { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }

    /// <summary>
    /// Initial workflow variable values keyed by variable name, seeded as durable runtime state at start so
    /// later activities resolve <c>variables.*</c> input expressions. Empty when none are supplied.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Variables { get; }

    /// <summary>
    /// Workflow input values keyed by input name, seeded as durable runtime state at start so later activities
    /// resolve <c>input.*</c> input expressions. Empty when none are supplied.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Inputs { get; }

    private static IReadOnlyDictionary<string, object?> SnapshotValues(IReadOnlyDictionary<string, object?>? values) =>
        (values ?? new Dictionary<string, object?>()).ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
}

public sealed class WorkflowExecutionStartCommandPayload
{
    [JsonConstructor]
    public WorkflowExecutionStartCommandPayload(
        WorkflowExecutableIdentity pinnedExecutable,
        string requestedArtifactId,
        IReadOnlyDictionary<string, JsonElement>? variables = null,
        IReadOnlyDictionary<string, JsonElement>? inputs = null)
    {
        ArgumentNullException.ThrowIfNull(pinnedExecutable);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedArtifactId);

        PinnedExecutable = pinnedExecutable;
        RequestedArtifactId = requestedArtifactId;
        Variables = SnapshotElements(variables);
        Inputs = SnapshotElements(inputs);
    }

    public WorkflowExecutableIdentity PinnedExecutable { get; }
    public string RequestedArtifactId { get; }

    /// <summary>
    /// Initial workflow variable values (name → JSON-encoded value) carried from start dispatch so the
    /// workflow-started checkpoint can seed them as durable runtime state.
    /// </summary>
    public IReadOnlyDictionary<string, JsonElement> Variables { get; }

    /// <summary>
    /// Workflow input values (name → JSON-encoded value) carried from start dispatch so the workflow-started
    /// checkpoint can seed them as durable runtime state.
    /// </summary>
    public IReadOnlyDictionary<string, JsonElement> Inputs { get; }

    public static IReadOnlyDictionary<string, JsonElement> ToJsonValues(IReadOnlyDictionary<string, object?> values) =>
        values.ToDictionary(
            item => item.Key,
            item => item.Value is JsonElement json ? json.Clone() : JsonSerializer.SerializeToElement(item.Value, item.Value?.GetType() ?? typeof(object)),
            StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, JsonElement> SnapshotElements(IReadOnlyDictionary<string, JsonElement>? values) =>
        (values ?? new Dictionary<string, JsonElement>()).ToDictionary(item => item.Key, item => item.Value.Clone(), StringComparer.Ordinal);
}

public sealed class WorkflowExecutionStartDispatchResult
{
    public WorkflowExecutionStartDispatchResult(
        string workflowExecutionId,
        WorkflowExecutableIdentity pinnedExecutable,
        WorkflowExecutionCommandDispatchResult commandDispatch,
        WorkflowExecutionActorDescriptor agent)
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
    public WorkflowExecutionActorDescriptor Agent { get; }
}
