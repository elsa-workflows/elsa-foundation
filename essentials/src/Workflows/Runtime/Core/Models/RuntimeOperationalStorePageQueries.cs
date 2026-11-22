namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>One finite, deterministic page of liveness records for one workflow execution.</summary>
public sealed record ExecutionLivenessStatePageQuery : RuntimeStorePageRequest
{
    public ExecutionLivenessStatePageQuery(
        string workflowExecutionId,
        int limit = DefaultLimit,
        string? continuationToken = null)
        : base(limit, continuationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        WorkflowExecutionId = workflowExecutionId;
    }

    public string WorkflowExecutionId { get; }
}

/// <summary>One finite, deterministic page of durable timers for one workflow execution.</summary>
public sealed record DurableTimerPageQuery : RuntimeStorePageRequest
{
    public DurableTimerPageQuery(
        string workflowExecutionId,
        int limit = DefaultLimit,
        string? continuationToken = null)
        : base(limit, continuationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        WorkflowExecutionId = workflowExecutionId;
    }

    public string WorkflowExecutionId { get; }
}

/// <summary>One finite, deterministic page of recurring schedules for one activation.</summary>
public sealed record RecurringTriggerScheduleActivationPageQuery : RuntimeStorePageRequest
{
    public RecurringTriggerScheduleActivationPageQuery(
        string activationId,
        int limit = DefaultLimit,
        string? continuationToken = null)
        : base(limit, continuationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activationId);
        ActivationId = activationId;
    }

    public string ActivationId { get; }
}

/// <summary>One finite, deterministic page of recurring schedules for one artifact.</summary>
public sealed record RecurringTriggerScheduleArtifactPageQuery : RuntimeStorePageRequest
{
    public RecurringTriggerScheduleArtifactPageQuery(
        string artifactId,
        int limit = DefaultLimit,
        string? continuationToken = null)
        : base(limit, continuationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        ArtifactId = artifactId;
    }

    public string ArtifactId { get; }
}
