using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>A lifecycle transition of an activity invocation (spec 1221 FR-024).</summary>
public enum ActivityLifecycleTransitionKind
{
    Scheduled,
    Started,
    Suspended,
    Resumed,
    Completed,
    Faulted,
    Cancelled,
    Superseded
}

/// <summary>
/// One lifecycle transition and the instance commit sequence that committed it, with the branch and path it ran on and,
/// for a background step whose inputs were refreshed, the input evaluation it ran on.
/// </summary>
public sealed record ActivityLifecycleStamp
{
    [JsonConstructor]
    public ActivityLifecycleStamp(
        ActivityLifecycleTransitionKind kind,
        long commitSequence,
        string? branchId = null,
        string? executionPathId = null,
        int? evaluation = null)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Lifecycle transition kind is not defined.");
        ArgumentOutOfRangeException.ThrowIfNegative(commitSequence);
        Kind = kind;
        CommitSequence = commitSequence;
        BranchId = branchId;
        ExecutionPathId = executionPathId;
        Evaluation = evaluation;
    }

    public ActivityLifecycleTransitionKind Kind { get; }
    public long CommitSequence { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BranchId { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExecutionPathId { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Evaluation { get; }
}

/// <summary>
/// The instance commit sequence (spec 1221 FR-024): the execution lease's fencing token, which only grows per instance,
/// in the high 32 bits, and the commit's ordinal within that lease in the low 32 bits. Numbers grow with commit order
/// and are not dense.
/// </summary>
public static class RuntimeCommitSequence
{
    /// <summary>Composes the sequence of the <paramref name="ordinal"/>-th commit under the lease with <paramref name="fencingToken"/>.</summary>
    public static long Compose(long fencingToken, long ordinal)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fencingToken);
        ArgumentOutOfRangeException.ThrowIfNegative(ordinal);
        if (fencingToken > int.MaxValue || ordinal > uint.MaxValue)
            throw new OverflowException("The commit sequence exceeds its encoding.");
        return (fencingToken << 32) | ordinal;
    }

    /// <summary>The fencing token part of <paramref name="sequence"/>.</summary>
    public static long FencingTokenOf(long sequence) => sequence >> 32;

    /// <summary>The ordinal within the lease of <paramref name="sequence"/>.</summary>
    public static long OrdinalOf(long sequence) => sequence & uint.MaxValue;
}
