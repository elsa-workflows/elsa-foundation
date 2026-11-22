using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Core.Models;

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
        int dispatchNestingDepth,
        WorkflowTestScope? testScope = null)
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
        if (testScope is not null)
        {
            if (runKind != WorkflowRunKind.TestRun)
                throw new ArgumentException("A workflow test scope requires TestRun run kind.", nameof(testScope));
            if (!StringComparer.Ordinal.Equals(tenantId, testScope.TenantId) || !Equals(partition, testScope.Partition))
                throw new ArgumentException("The workflow test scope must match the start payload tenant and partition.", nameof(testScope));
        }

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
        TestScope = testScope;
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
    public WorkflowTestScope? TestScope { get; }

    private static void ValidateOptional(string? value, string parameterName)
    {
        if (value is not null && string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Optional dispatch start context values cannot be blank.", parameterName);
    }
}
