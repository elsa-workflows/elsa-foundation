using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Core.Models;

public sealed record WorkflowDispatchInputDescriptor
{
    public WorkflowDispatchInputDescriptor(string name, string valueType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(valueType);

        Name = name;
        ValueType = valueType;
    }

    public string Name { get; }
    public string ValueType { get; }
}

public sealed class WorkflowDispatchRecord
{
    public WorkflowDispatchRecord(
        string dispatchId,
        string parentWorkflowExecutionId,
        string parentActivityExecutionId,
        string childWorkflowExecutionId,
        WorkflowExecutableIdentity childExecutable,
        WorkflowExecutableSourceProvenance childSource,
        WorkflowDispatchMode mode,
        WorkflowDispatchStatus status,
        string? correlationId,
        string? tenantId,
        WorkflowExecutionPartition partition,
        WorkflowRunKind runKind,
        WorkflowExecutionAuthoritySnapshot authority,
        IReadOnlyCollection<WorkflowDispatchInputDescriptor>? inputDescriptors,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        IReadOnlyDictionary<string, string>? metadata = null)
        : this(
            dispatchId,
            parentWorkflowExecutionId,
            parentActivityExecutionId,
            childWorkflowExecutionId,
            childExecutable,
            childSource,
            mode,
            status,
            correlationId,
            tenantId,
            partition,
            runKind,
            authority,
            inputDescriptors,
            createdAt,
            updatedAt,
            metadata,
            dispatchNestingDepth: 0)
    {
    }

    [JsonConstructor]
    public WorkflowDispatchRecord(
        string dispatchId,
        string parentWorkflowExecutionId,
        string parentActivityExecutionId,
        string childWorkflowExecutionId,
        WorkflowExecutableIdentity childExecutable,
        WorkflowExecutableSourceProvenance childSource,
        WorkflowDispatchMode mode,
        WorkflowDispatchStatus status,
        string? correlationId,
        string? tenantId,
        WorkflowExecutionPartition partition,
        WorkflowRunKind runKind,
        WorkflowExecutionAuthoritySnapshot authority,
        IReadOnlyCollection<WorkflowDispatchInputDescriptor>? inputDescriptors,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        IReadOnlyDictionary<string, string>? metadata,
        int dispatchNestingDepth)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dispatchId);
        ArgumentException.ThrowIfNullOrWhiteSpace(parentWorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(parentActivityExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(childWorkflowExecutionId);
        ArgumentNullException.ThrowIfNull(childExecutable);
        ArgumentNullException.ThrowIfNull(childSource);
        ArgumentNullException.ThrowIfNull(partition);
        ArgumentNullException.ThrowIfNull(authority);
        ValidateOptional(correlationId, nameof(correlationId));
        ValidateOptional(tenantId, nameof(tenantId));
        if (dispatchNestingDepth < 0)
            throw new ArgumentOutOfRangeException(nameof(dispatchNestingDepth), dispatchNestingDepth, "Dispatch nesting depth cannot be negative.");
        var identity = new WorkflowDispatchIdentity(parentWorkflowExecutionId, parentActivityExecutionId);
        if (!StringComparer.Ordinal.Equals(dispatchId, identity.DispatchId))
            throw new ArgumentException("DispatchId must match the deterministic parent/activity identity.", nameof(dispatchId));
        if (!StringComparer.Ordinal.Equals(childWorkflowExecutionId, identity.ChildWorkflowExecutionId))
            throw new ArgumentException("ChildWorkflowExecutionId must match the deterministic parent/activity identity.", nameof(childWorkflowExecutionId));

        if (updatedAt < createdAt)
            throw new ArgumentOutOfRangeException(nameof(updatedAt), "UpdatedAt cannot precede CreatedAt.");

        var descriptors = (inputDescriptors ?? [])
            .OrderBy(descriptor => descriptor?.Name, StringComparer.Ordinal)
            .ThenBy(descriptor => descriptor?.ValueType, StringComparer.Ordinal)
            .ToArray();
        if (descriptors.Any(descriptor => descriptor is null))
            throw new ArgumentException("Input descriptors cannot contain null values.", nameof(inputDescriptors));
        if (descriptors.Select(descriptor => descriptor.Name).Distinct(StringComparer.Ordinal).Count() != descriptors.Length)
            throw new ArgumentException("Input descriptor names must be unique.", nameof(inputDescriptors));

        DispatchId = dispatchId;
        ParentWorkflowExecutionId = parentWorkflowExecutionId;
        ParentActivityExecutionId = parentActivityExecutionId;
        ChildWorkflowExecutionId = childWorkflowExecutionId;
        ChildExecutable = childExecutable;
        ChildSource = childSource;
        Mode = mode;
        Status = status;
        CorrelationId = correlationId;
        TenantId = tenantId;
        Partition = partition;
        RunKind = runKind;
        Authority = authority;
        InputDescriptors = descriptors;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
        DispatchNestingDepth = dispatchNestingDepth;
    }

    public string DispatchId { get; }
    public string ParentWorkflowExecutionId { get; }
    public string ParentActivityExecutionId { get; }
    public string ChildWorkflowExecutionId { get; }
    public WorkflowExecutableIdentity ChildExecutable { get; }
    public WorkflowExecutableSourceProvenance ChildSource { get; }
    public WorkflowDispatchMode Mode { get; }
    public WorkflowDispatchStatus Status { get; }
    public string? CorrelationId { get; }
    public string? TenantId { get; }
    public WorkflowExecutionPartition Partition { get; }
    public WorkflowRunKind RunKind { get; }
    public WorkflowExecutionAuthoritySnapshot Authority { get; }
    public IReadOnlyCollection<WorkflowDispatchInputDescriptor> InputDescriptors { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
    public int DispatchNestingDepth { get; }

    private static void ValidateOptional(string? value, string parameterName)
    {
        if (value is not null && string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Optional dispatch context values cannot be blank.", parameterName);
    }
}

public sealed class WorkflowDispatchCheckpointRequest
{
    public WorkflowDispatchCheckpointRequest(WorkflowDispatchRecord record, RuntimePostCommitIntent startIntent)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(startIntent);
        if (!StringComparer.Ordinal.Equals(record.ParentWorkflowExecutionId, startIntent.WorkflowExecutionId))
            throw new ArgumentException("The start intent workflow execution ID must match the dispatch parent execution.", nameof(startIntent));
        if (!StringComparer.Ordinal.Equals(record.ParentActivityExecutionId, startIntent.ActivityExecutionId))
            throw new ArgumentException("The start intent activity execution ID must match the dispatch parent activity execution.", nameof(startIntent));
        var identity = new WorkflowDispatchIdentity(record.ParentWorkflowExecutionId, record.ParentActivityExecutionId);
        if (!StringComparer.Ordinal.Equals(record.DispatchId, identity.DispatchId) ||
            !StringComparer.Ordinal.Equals(record.ChildWorkflowExecutionId, identity.ChildWorkflowExecutionId))
        {
            throw new ArgumentException("The dispatch record does not match its deterministic parent/activity identity.", nameof(record));
        }
        if (!StringComparer.Ordinal.Equals(startIntent.IntentId, identity.StartIntentId) ||
            !StringComparer.Ordinal.Equals(startIntent.IdempotencyKey, identity.StartIdempotencyKey))
        {
            throw new ArgumentException("The start intent does not match the dispatch record's deterministic identity.", nameof(startIntent));
        }

        Record = record;
        StartIntent = startIntent;
    }

    public WorkflowDispatchRecord Record { get; }
    public RuntimePostCommitIntent StartIntent { get; }
}

public sealed class WorkflowDispatchStartPayload
{
    public WorkflowDispatchStartPayload(
        string dispatchId,
        string parentWorkflowExecutionId,
        string parentActivityExecutionId,
        string childWorkflowExecutionId,
        WorkflowExecutableIdentity childExecutable,
        WorkflowExecutableSourceProvenance childSource,
        IReadOnlyDictionary<string, JsonElement>? inputs,
        string? correlationId,
        string? tenantId,
        WorkflowExecutionPartition partition,
        WorkflowRunKind runKind,
        WorkflowExecutionAuthoritySnapshot authority)
        : this(
            dispatchId,
            parentWorkflowExecutionId,
            parentActivityExecutionId,
            childWorkflowExecutionId,
            childExecutable,
            childSource,
            inputs,
            correlationId,
            tenantId,
            partition,
            runKind,
            authority,
            parentExecutable: null,
            dispatchNodeId: null)
    {
    }

    public WorkflowDispatchStartPayload(
        string dispatchId,
        string parentWorkflowExecutionId,
        string parentActivityExecutionId,
        string childWorkflowExecutionId,
        WorkflowExecutableIdentity childExecutable,
        WorkflowExecutableSourceProvenance? childSource,
        IReadOnlyDictionary<string, JsonElement>? inputs,
        string? correlationId,
        string? tenantId,
        WorkflowExecutionPartition partition,
        WorkflowRunKind runKind,
        WorkflowExecutionAuthoritySnapshot authority,
        WorkflowExecutableIdentity? parentExecutable,
        string? dispatchNodeId)
        : this(
            dispatchId,
            parentWorkflowExecutionId,
            parentActivityExecutionId,
            childWorkflowExecutionId,
            childExecutable,
            childSource,
            inputs,
            correlationId,
            tenantId,
            partition,
            runKind,
            authority,
            parentExecutable,
            dispatchNodeId,
            dispatchNestingDepth: 0)
    {
    }

    [JsonConstructor]
    public WorkflowDispatchStartPayload(
        string dispatchId,
        string parentWorkflowExecutionId,
        string parentActivityExecutionId,
        string childWorkflowExecutionId,
        WorkflowExecutableIdentity childExecutable,
        WorkflowExecutableSourceProvenance? childSource,
        IReadOnlyDictionary<string, JsonElement>? inputs,
        string? correlationId,
        string? tenantId,
        WorkflowExecutionPartition partition,
        WorkflowRunKind runKind,
        WorkflowExecutionAuthoritySnapshot authority,
        WorkflowExecutableIdentity? parentExecutable,
        string? dispatchNodeId,
        int dispatchNestingDepth)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dispatchId);
        ArgumentException.ThrowIfNullOrWhiteSpace(parentWorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(parentActivityExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(childWorkflowExecutionId);
        ArgumentNullException.ThrowIfNull(childExecutable);
        ArgumentNullException.ThrowIfNull(partition);
        ArgumentNullException.ThrowIfNull(authority);
        ValidateOptional(correlationId, nameof(correlationId));
        ValidateOptional(tenantId, nameof(tenantId));
        ValidateOptional(dispatchNodeId, nameof(dispatchNodeId));
        if (dispatchNestingDepth < 0)
            throw new ArgumentOutOfRangeException(nameof(dispatchNestingDepth), dispatchNestingDepth, "Dispatch nesting depth cannot be negative.");
        if ((parentExecutable is null) != (dispatchNodeId is null))
            throw new ArgumentException("Retained child starts require both parent executable identity and dispatch node ID.", nameof(parentExecutable));
        if (parentExecutable is null && childSource is null)
            throw new ArgumentException("Legacy child starts require historical child source provenance.", nameof(childSource));

        DispatchId = dispatchId;
        ParentWorkflowExecutionId = parentWorkflowExecutionId;
        ParentActivityExecutionId = parentActivityExecutionId;
        ChildWorkflowExecutionId = childWorkflowExecutionId;
        ChildExecutable = childExecutable;
        ChildSource = childSource;
        Inputs = (inputs ?? new Dictionary<string, JsonElement>())
            .ToDictionary(item => item.Key, item => item.Value.Clone(), StringComparer.Ordinal);
        CorrelationId = correlationId;
        TenantId = tenantId;
        Partition = partition;
        RunKind = runKind;
        Authority = authority;
        ParentExecutable = parentExecutable;
        DispatchNodeId = dispatchNodeId;
        DispatchNestingDepth = dispatchNestingDepth;
    }

    public string DispatchId { get; }
    public string ParentWorkflowExecutionId { get; }
    public string ParentActivityExecutionId { get; }
    public string ChildWorkflowExecutionId { get; }
    public WorkflowExecutableIdentity ChildExecutable { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WorkflowExecutableSourceProvenance? ChildSource { get; }
    public IReadOnlyDictionary<string, JsonElement> Inputs { get; }
    public string? CorrelationId { get; }
    public string? TenantId { get; }
    public WorkflowExecutionPartition Partition { get; }
    public WorkflowRunKind RunKind { get; }
    public WorkflowExecutionAuthoritySnapshot Authority { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WorkflowExecutableIdentity? ParentExecutable { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DispatchNodeId { get; }
    public int DispatchNestingDepth { get; }

    private static void ValidateOptional(string? value, string parameterName)
    {
        if (value is not null && string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Optional dispatch start context values cannot be blank.", parameterName);
    }
}

public enum WorkflowDispatchMode
{
    FireAndForget = 0,
    WaitForCompletion = 1
}

public enum WorkflowDispatchStatus
{
    Pending = 0,
    Started = 1,
    Completed = 2,
    Faulted = 3,
    Cancelled = 4,
    DispatchFailed = 5
}
