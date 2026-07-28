using Elsa.Workflows.Runtime.Core.Models.Alterations;

namespace Elsa.Workflows.Runtime.Api.Models.Alterations;

/// <summary>Payload-free submission receipt. The durable plan is the only execution contract.</summary>
public sealed record WorkflowAlterationPlanSubmissionView(
    string PlanId,
    string Status,
    string SubmissionDisposition,
    DateTimeOffset CreatedAt,
    WorkflowAlterationLinksView Links)
{
    public static WorkflowAlterationPlanSubmissionView From(WorkflowAlterationPlanState plan, bool isReplay) =>
        new(plan.PlanId, plan.Status.ToString(), isReplay ? "Replayed" : "Accepted", plan.CreatedAt, WorkflowAlterationLinksView.For(plan.PlanId));
}

public sealed record WorkflowAlterationPlanView(
    string PlanId,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SealedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? CancellationRequestedAt,
    WorkflowAlterationTargetSummaryView Target,
    IReadOnlyCollection<WorkflowAlterationDescriptorView> Alterations,
    WorkflowAlterationPlanCountsView Counts,
    WorkflowAlterationFailureView? Failure,
    WorkflowAlterationOperatorProvenanceView SubmittedBy,
    WorkflowAlterationLinksView Links);

public sealed record WorkflowAlterationPlanCancellationView(WorkflowAlterationPlanView Plan, bool IsTerminalNoOp);

public sealed record WorkflowAlterationTargetSummaryView(
    string Kind,
    int? ExplicitCount,
    IReadOnlyCollection<string> FilterNames)
{
    public static WorkflowAlterationTargetSummaryView From(WorkflowAlterationTargetSelector target) => target.Query is null
        ? new("Explicit", target.ExecutionIds.Count, [])
        : new("Query", null, CreateFilterNames(target.Query));

    private static IReadOnlyCollection<string> CreateFilterNames(WorkflowAlterationQuerySelector query)
    {
        var names = new List<string>();
        if (query.DefinitionId is not null) names.Add("definitionId");
        if (query.Status is not null) names.Add("status");
        if (query.RunKind is not null) names.Add("runKind");
        if (query.From is not null) names.Add("from");
        if (query.To is not null) names.Add("to");
        if (query.CorrelationId is not null) names.Add("correlationId");
        if (query.WorkflowExecutionId is not null) names.Add("workflowExecutionId");
        if (query.ArtifactId is not null) names.Add("artifactId");
        if (query.MatchAllAuthorized) names.Add("matchAllAuthorized");
        return names;
    }
}

public sealed record WorkflowAlterationDescriptorView(int Ordinal, string Kind, int SchemaVersion);

public sealed record WorkflowAlterationPlanCountsView(
    long CapturedSoFar,
    long TargetCount,
    long Pending,
    long Running,
    long Succeeded,
    long Failed,
    long Cancelled);

public sealed record WorkflowAlterationJobPageView(
    IReadOnlyCollection<WorkflowAlterationJobSummaryView> Items,
    string? NextCursor,
    bool HasNext,
    int Count,
    long TotalCount);

public sealed record WorkflowAlterationJobSummaryView(
    string JobId,
    string PlanId,
    string WorkflowExecutionId,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    WorkflowAlterationFailureView? Failure)
{
    public static WorkflowAlterationJobSummaryView From(WorkflowAlterationJobState job) =>
        new(job.JobId, job.PlanId, job.WorkflowExecutionId, job.Status.ToString(), job.CreatedAt, job.StartedAt, job.CompletedAt, WorkflowAlterationFailureView.From(job.SafeFailure));
}

public sealed record WorkflowAlterationJobView(
    string JobId,
    string PlanId,
    string WorkflowExecutionId,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    WorkflowAlterationFailureView? Failure,
    WorkflowAlterationOperatorProvenanceView SubmittedBy,
    IReadOnlyCollection<WorkflowAlterationOutcomeView> Outcomes)
{
    public static WorkflowAlterationJobView From(WorkflowAlterationJobState job, WorkflowAlterationOperatorProvenance submittedBy) =>
        new(job.JobId, job.PlanId, job.WorkflowExecutionId, job.Status.ToString(), job.CreatedAt, job.StartedAt, job.CompletedAt,
            WorkflowAlterationFailureView.From(job.SafeFailure), WorkflowAlterationOperatorProvenanceView.From(submittedBy),
            job.Outcomes.Select(WorkflowAlterationOutcomeView.From).ToArray());
}

public sealed record WorkflowAlterationOutcomeView(
    int Ordinal,
    string Kind,
    int SchemaVersion,
    string Status,
    string Code,
    string? Message,
    DateTimeOffset RecordedAt,
    IReadOnlyDictionary<string, string> StructuralMetadata)
{
    public static WorkflowAlterationOutcomeView From(WorkflowAlterationOutcome outcome) =>
        new(outcome.Ordinal, outcome.Kind, outcome.SchemaVersion, outcome.Status.ToString(), outcome.Code, outcome.Message, outcome.RecordedAt, outcome.StructuralMetadata);
}

public sealed record WorkflowAlterationFailureView(string Code, string? Message)
{
    public static WorkflowAlterationFailureView? From(WorkflowAlterationSafeFailure? failure) =>
        failure is null ? null : new(failure.Code, failure.Message);
}

/// <summary>Safe submitting-operator audit evidence; it contains no authentication token or session material.</summary>
public sealed record WorkflowAlterationOperatorProvenanceView(string Subject, string? CorrelationId, string? DisplayName)
{
    public static WorkflowAlterationOperatorProvenanceView From(WorkflowAlterationOperatorProvenance provenance) =>
        new(provenance.Subject, provenance.CorrelationId, provenance.DisplayName);
}

public sealed record WorkflowAlterationLinksView(string Self, string JobsPage, string Cancel)
{
    public static WorkflowAlterationLinksView For(string planId) => new(
        $"/runtime/workflows/alteration-plans/{Uri.EscapeDataString(planId)}",
        $"/runtime/workflows/alteration-plans/{Uri.EscapeDataString(planId)}/jobs/page",
        $"/runtime/workflows/alteration-plans/{Uri.EscapeDataString(planId)}/cancel");
}

/// <summary>Fixed problem shape that never serializes an exception, payload, stack, or protected state.</summary>
public sealed record WorkflowAlterationProblemView(string Code, string Message, int Status);
