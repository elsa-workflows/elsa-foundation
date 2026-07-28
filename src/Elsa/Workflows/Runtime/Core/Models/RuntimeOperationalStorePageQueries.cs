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

/// <summary>One finite, deterministic page of recurring schedules for one publication.</summary>
public sealed record RecurringTriggerSchedulePublicationPageQuery : RuntimeStorePageRequest
{
    public RecurringTriggerSchedulePublicationPageQuery(
        string publicationId,
        int limit = DefaultLimit,
        string? continuationToken = null)
        : base(limit, continuationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicationId);
        PublicationId = publicationId;
    }

    public string PublicationId { get; }
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
