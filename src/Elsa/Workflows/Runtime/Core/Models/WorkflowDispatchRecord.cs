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

    /// <summary>Creates a lifecycle successor while preserving every immutable dispatch field.</summary>
    public WorkflowDispatchRecord TransitionTo(WorkflowDispatchStatus status, DateTimeOffset updatedAt) =>
        WorkflowDispatchLifecycle.Transition(this, status, updatedAt);

    /// <summary>Creates a final delivery-failure transition with fixed safe diagnostic classification.</summary>
    public WorkflowDispatchRecord TransitionToDispatchFailed(DateTimeOffset updatedAt) =>
        WorkflowDispatchLifecycle.TransitionToDispatchFailed(this, updatedAt);

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

/// <summary>Bounded provider-neutral dispatch query. Tenant scope comes from the active store context.</summary>
public sealed class WorkflowDispatchQuery
{
    public const int MaximumTake = 100;

    public WorkflowDispatchQuery(
        string? parentWorkflowExecutionId = null,
        string? childWorkflowExecutionId = null,
        WorkflowDispatchStatus? status = null,
        int take = MaximumTake,
        DateTimeOffset? afterCreatedAt = null,
        string? afterDispatchId = null)
    {
        ValidateOptional(parentWorkflowExecutionId, nameof(parentWorkflowExecutionId));
        ValidateOptional(childWorkflowExecutionId, nameof(childWorkflowExecutionId));
        if (parentWorkflowExecutionId is null && childWorkflowExecutionId is null && status is null)
            throw new ArgumentException("A workflow dispatch query requires at least one operational filter.");
        if (take is <= 0 or > MaximumTake)
            throw new ArgumentOutOfRangeException(nameof(take), $"Workflow dispatch query take must be between 1 and {MaximumTake}.");
        if ((afterCreatedAt is null) != (afterDispatchId is null))
            throw new ArgumentException("Workflow dispatch query continuation requires both creation time and dispatch ID.", nameof(afterDispatchId));
        ValidateOptional(afterDispatchId, nameof(afterDispatchId));

        ParentWorkflowExecutionId = parentWorkflowExecutionId;
        ChildWorkflowExecutionId = childWorkflowExecutionId;
        Status = status;
        Take = take;
        AfterCreatedAt = afterCreatedAt;
        AfterDispatchId = afterDispatchId;
    }

    public string? ParentWorkflowExecutionId { get; }
    public string? ChildWorkflowExecutionId { get; }
    public WorkflowDispatchStatus? Status { get; }
    public int Take { get; }
    public DateTimeOffset? AfterCreatedAt { get; }
    public string? AfterDispatchId { get; }

    private static void ValidateOptional(string? value, string parameterName)
    {
        if (value is not null && string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Workflow dispatch query filters cannot be blank.", parameterName);
    }
}

/// <summary>Shared identity and monotonic-lifecycle rules used by every dispatch store.</summary>
public static class WorkflowDispatchLifecycle
{
    public const string DiagnosticCodeMetadataKey = "runtime.diagnostic.code";
    public const string DiagnosticCategoryMetadataKey = "runtime.diagnostic.category";
    public const string ChildStartDeliveryFailedCode = "child-start-delivery-failed";
    public const string DeliveryCategory = "delivery";

    public static WorkflowDispatchRecord Transition(
        WorkflowDispatchRecord record,
        WorkflowDispatchStatus status,
        DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(record);

        var candidate = new WorkflowDispatchRecord(
            record.DispatchId,
            record.ParentWorkflowExecutionId,
            record.ParentActivityExecutionId,
            record.ChildWorkflowExecutionId,
            record.ChildExecutable,
            record.ChildSource,
            record.Mode,
            status,
            record.CorrelationId,
            record.TenantId,
            record.Partition,
            record.RunKind,
            record.Authority,
            record.InputDescriptors,
            record.CreatedAt,
            updatedAt,
            record.Metadata);
        ValidateTransition(record, candidate);
        return candidate;
    }

    public static WorkflowDispatchRecord TransitionToDispatchFailed(
        WorkflowDispatchRecord record,
        DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(record);
        var metadata = record.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        metadata[DiagnosticCodeMetadataKey] = ChildStartDeliveryFailedCode;
        metadata[DiagnosticCategoryMetadataKey] = DeliveryCategory;
        var candidate = new WorkflowDispatchRecord(
            record.DispatchId,
            record.ParentWorkflowExecutionId,
            record.ParentActivityExecutionId,
            record.ChildWorkflowExecutionId,
            record.ChildExecutable,
            record.ChildSource,
            record.Mode,
            WorkflowDispatchStatus.DispatchFailed,
            record.CorrelationId,
            record.TenantId,
            record.Partition,
            record.RunKind,
            record.Authority,
            record.InputDescriptors,
            record.CreatedAt,
            updatedAt,
            metadata);
        ValidateTransition(record, candidate);
        return candidate;
    }

    public static void ValidateNew(WorkflowDispatchRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.Status != WorkflowDispatchStatus.Pending)
            throw new InvalidOperationException($"New workflow dispatch '{record.DispatchId}' must start in Pending status.");
        ValidateDiagnostics(record);
    }

    public static void ValidateTransition(WorkflowDispatchRecord existing, WorkflowDispatchRecord candidate)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(candidate);

        if (RecordsEqual(existing, candidate))
            return;
        if (!ImmutableFieldsEqual(existing, candidate))
            throw new InvalidOperationException($"Workflow dispatch '{candidate.DispatchId}' already exists with conflicting immutable identity or context.");
        if (candidate.UpdatedAt < existing.UpdatedAt)
            throw new InvalidOperationException($"Workflow dispatch '{candidate.DispatchId}' cannot move its update timestamp backwards.");
        ValidateDiagnostics(candidate);

        var validTransition = existing.Status switch
        {
            WorkflowDispatchStatus.Pending => candidate.Status is
                WorkflowDispatchStatus.Started or
                WorkflowDispatchStatus.Completed or
                WorkflowDispatchStatus.Faulted or
                WorkflowDispatchStatus.Cancelled or
                WorkflowDispatchStatus.DispatchFailed,
            WorkflowDispatchStatus.Started => candidate.Status is
                WorkflowDispatchStatus.Completed or
                WorkflowDispatchStatus.Faulted or
                WorkflowDispatchStatus.Cancelled or
                WorkflowDispatchStatus.DispatchFailed,
            _ => false
        };
        if (!validTransition)
            throw new InvalidOperationException($"Workflow dispatch '{candidate.DispatchId}' cannot transition from '{existing.Status}' to '{candidate.Status}'.");
    }

    /// <summary>Ensures Pending creation is parent-owned and checkpoint lifecycle projection is child-owned.</summary>
    public static void ValidateCheckpointOwnership(string workflowExecutionId, WorkflowDispatchRecord record)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentNullException.ThrowIfNull(record);

        var ownerExecutionId = record.Status == WorkflowDispatchStatus.Pending
            ? record.ParentWorkflowExecutionId
            : record.ChildWorkflowExecutionId;
        if (!StringComparer.Ordinal.Equals(workflowExecutionId, ownerExecutionId))
        {
            var ownerKind = record.Status == WorkflowDispatchStatus.Pending ? "parent" : "child";
            throw new InvalidOperationException(
                $"Workflow dispatch '{record.DispatchId}' status '{record.Status}' must be committed by its {ownerKind} workflow execution.");
        }
    }

    public static bool ImmutableFieldsEqual(WorkflowDispatchRecord left, WorkflowDispatchRecord right) =>
        StringComparer.Ordinal.Equals(left.DispatchId, right.DispatchId) &&
        StringComparer.Ordinal.Equals(left.ParentWorkflowExecutionId, right.ParentWorkflowExecutionId) &&
        StringComparer.Ordinal.Equals(left.ParentActivityExecutionId, right.ParentActivityExecutionId) &&
        StringComparer.Ordinal.Equals(left.ChildWorkflowExecutionId, right.ChildWorkflowExecutionId) &&
        Equals(left.ChildExecutable, right.ChildExecutable) &&
        Equals(left.ChildSource, right.ChildSource) &&
        left.Mode == right.Mode &&
        StringComparer.Ordinal.Equals(left.CorrelationId, right.CorrelationId) &&
        StringComparer.Ordinal.Equals(left.TenantId, right.TenantId) &&
        Equals(left.Partition, right.Partition) &&
        left.RunKind == right.RunKind &&
        AuthorityEquals(left.Authority, right.Authority) &&
        left.InputDescriptors.SequenceEqual(right.InputDescriptors) &&
        left.CreatedAt == right.CreatedAt &&
        ImmutableMetadataEquals(left.Metadata, right.Metadata);

    public static bool RecordsEqual(WorkflowDispatchRecord left, WorkflowDispatchRecord right) =>
        ImmutableFieldsEqual(left, right) &&
        left.Status == right.Status &&
        left.UpdatedAt == right.UpdatedAt &&
        MetadataEquals(left.Metadata, right.Metadata);

    public static string? ReadSafeDiagnosticCode(WorkflowDispatchRecord record) =>
        record.Metadata.TryGetValue(DiagnosticCodeMetadataKey, out var value) &&
        StringComparer.Ordinal.Equals(value, ChildStartDeliveryFailedCode)
            ? value
            : null;

    public static string? ReadSafeDiagnosticCategory(WorkflowDispatchRecord record) =>
        record.Metadata.TryGetValue(DiagnosticCategoryMetadataKey, out var value) &&
        StringComparer.Ordinal.Equals(value, DeliveryCategory)
            ? value
            : null;

    private static bool AuthorityEquals(WorkflowExecutionAuthoritySnapshot left, WorkflowExecutionAuthoritySnapshot right) =>
        StringComparer.Ordinal.Equals(left.SystemIdentity, right.SystemIdentity) &&
        StringComparer.Ordinal.Equals(left.RootInitiator, right.RootInitiator) &&
        MetadataEquals(left.Metadata, right.Metadata);

    private static bool MetadataEquals(IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right) =>
        left.Count == right.Count &&
        left.All(entry => right.TryGetValue(entry.Key, out var value) && StringComparer.Ordinal.Equals(entry.Value, value));

    private static bool ImmutableMetadataEquals(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right)
    {
        var leftImmutable = left.Where(item => !IsDiagnosticKey(item.Key)).ToArray();
        var rightImmutable = right.Where(item => !IsDiagnosticKey(item.Key)).ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        return leftImmutable.Length == rightImmutable.Count &&
               leftImmutable.All(item => rightImmutable.TryGetValue(item.Key, out var value) && StringComparer.Ordinal.Equals(item.Value, value));
    }

    private static bool IsDiagnosticKey(string key) =>
        StringComparer.Ordinal.Equals(key, DiagnosticCodeMetadataKey) ||
        StringComparer.Ordinal.Equals(key, DiagnosticCategoryMetadataKey);

    private static void ValidateDiagnostics(WorkflowDispatchRecord record)
    {
        var code = record.Metadata.GetValueOrDefault(DiagnosticCodeMetadataKey);
        var category = record.Metadata.GetValueOrDefault(DiagnosticCategoryMetadataKey);
        if (code is null && category is null)
            return;
        if (record.Status != WorkflowDispatchStatus.DispatchFailed ||
            !StringComparer.Ordinal.Equals(code, ChildStartDeliveryFailedCode) ||
            !StringComparer.Ordinal.Equals(category, DeliveryCategory))
        {
            throw new InvalidOperationException($"Workflow dispatch '{record.DispatchId}' carries an unsupported diagnostic classification.");
        }
    }
}
