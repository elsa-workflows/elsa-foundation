using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Continuation state for one workflow execution pinned to an exact executable artifact snapshot.
/// </summary>
public sealed record WorkflowExecutionState(
    string WorkflowExecutionId,
    WorkflowExecutableIdentity PinnedExecutable,
    WorkflowExecutionStatus Status,
    string? SubStatus,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? CompletedAt,
    string? CorrelationId,
    string? ParentWorkflowExecutionId,
    string? TenantId,
    IReadOnlyDictionary<string, string> SystemMetadata)
{
    private int _dispatchNestingDepth;

    /// <summary>
    /// The durable classification pinned when this execution starts. States written before run-kind tracking
    /// deserialize to <see cref="WorkflowRunKind.Unknown"/> and remain distinguishable as legacy history.
    /// </summary>
    public WorkflowRunKind RunKind { get; init; } = WorkflowRunKind.Unknown;

    /// <summary>
    /// Immutable source attribution selected when this execution started. Null only for legacy state.
    /// </summary>
    public WorkflowExecutableSourceProvenance? PinnedSource { get; init; }

    /// <summary>Partition selected when the execution was dispatched. Null only for legacy state.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WorkflowExecutionPartition? Partition { get; init; }

    /// <summary>Immutable runtime-owned authority and root-initiator attribution. Null only for legacy state.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WorkflowExecutionAuthoritySnapshot? Authority { get; init; }

    /// <summary>
    /// Number of cross-workflow dispatch edges from the root execution. Root and legacy states default to zero.
    /// </summary>
    public int DispatchNestingDepth
    {
        get => _dispatchNestingDepth;
        init
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), value, "Dispatch nesting depth cannot be negative.");
            _dispatchNestingDepth = value;
        }
    }

    /// <summary>Authoritative finite test-run scope. Null for non-test and legacy execution state.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WorkflowTestScope? TestScope { get; init; }
}

public enum WorkflowRunKind
{
    Unknown = 0,
    TestRun = 1,
    PublishedRun = 2,
    BackgroundWeaverRun = 3
}

public enum WorkflowExecutionStatus
{
    Pending,
    Running,
    Suspended,
    Completed,
    Faulted,
    Cancelled
}

public static class WorkflowExecutionStatusExtensions
{
    /// <summary>
    /// Returns <see langword="true"/> when the workflow execution has reached a terminal status
    /// (<see cref="WorkflowExecutionStatus.Completed"/>, <see cref="WorkflowExecutionStatus.Faulted"/>,
    /// or <see cref="WorkflowExecutionStatus.Cancelled"/>) after which no further scheduler work may run.
    /// </summary>
    public static bool IsTerminal(this WorkflowExecutionStatus status) =>
        status is WorkflowExecutionStatus.Completed
            or WorkflowExecutionStatus.Faulted
            or WorkflowExecutionStatus.Cancelled;
}
