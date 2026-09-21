using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Core.Models;

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
        IReadOnlyDictionary<string, string>? metadata = null,
        WorkflowTestScope? testScope = null)
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
            dispatchNestingDepth: 0,
            testScope)
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
        int dispatchNestingDepth,
        WorkflowTestScope? testScope = null)
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
        if (testScope is not null)
        {
            if (runKind != WorkflowRunKind.TestRun)
                throw new ArgumentException("A workflow test scope requires TestRun run kind.", nameof(testScope));
            if (!StringComparer.Ordinal.Equals(tenantId, testScope.TenantId) || !Equals(partition, testScope.Partition))
                throw new ArgumentException("The workflow test scope must match the dispatch tenant and partition.", nameof(testScope));
        }

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
        TestScope = testScope;
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
    public WorkflowTestScope? TestScope { get; }

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
