using Elsa.Workflows.Runtime.Api.Contracts.Alterations;
using Elsa.Workflows.Runtime.Api.Models.Alterations;
using Elsa.Workflows.Runtime.Api.Requests.Alterations;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Services.Alterations;

// The namespace is pinned: the receipt-locked capture tool (tools/compatibility/RuntimeFastEndpointsCapture)
// references WorkflowAlterationResourceNotFoundException by this exact name and must keep compiling unmodified.
namespace Elsa.Workflows.Runtime.Api.Handlers.Alterations;

public sealed class WorkflowAlterationPlanApiService(
    WorkflowAlterationPlanService planService,
    IWorkflowAlterationStore store,
    IWorkflowAlterationAdmissionGate admissionGate,
    IWorkflowAlterationRequestContext requestContext) : IWorkflowAlterationPlanApiService
{
    internal const int MaximumIdempotencyKeyLength = 256;

    public async Task<WorkflowAlterationPlanSubmissionView> SubmitAsync(SubmitWorkflowAlterationPlan request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.IdempotencyKey);
        if (request.IdempotencyKey.Length > MaximumIdempotencyKeyLength)
            throw new ArgumentOutOfRangeException(nameof(request.IdempotencyKey), "The Idempotency-Key header must not exceed 256 characters.");
        var submission = new WorkflowAlterationSubmission(
            requestContext.AuthorityScope,
            ToTarget(request.Target),
            ToEnvelopes(request.Alterations));
        var admission = await admissionGate.EvaluateAsync(cancellationToken);
        if (!admission.IsAccepted)
            throw new WorkflowAlterationAdmissionRejectedException(admission.RetryAfter);

        var result = await planService.SubmitAsync(submission, requestContext.Operator, request.IdempotencyKey, cancellationToken);
        return WorkflowAlterationPlanSubmissionView.From(result.Plan, result.IsReplay);
    }

    public async Task<WorkflowAlterationPlanView> GetPlanAsync(GetWorkflowAlterationPlan request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PlanId);
        var plan = await store.FindPlanAsync(request.PlanId, cancellationToken);
        if (plan is null || !requestContext.CanAccess(plan))
            throw new WorkflowAlterationResourceNotFoundException();
        return await WorkflowAlterationProjection.ToPlanAsync(store, plan, cancellationToken);
    }

    public async Task<WorkflowAlterationJobPageView> PageJobsAsync(PageWorkflowAlterationJobs request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PlanId);
        var plan = await store.FindPlanAsync(request.PlanId, cancellationToken);
        if (plan is null || !requestContext.CanAccess(plan))
            throw new WorkflowAlterationResourceNotFoundException();
        if (request.Take is <= 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(request.Take), "The alteration jobs page size must be between 1 and 100.");
        var take = request.Take ?? 25;
        var page = await store.PageJobsAsync(plan.PlanId, take, request.Cursor, cancellationToken);
        var totalCount = plan.SealedAt is null ? plan.CapturedSoFar : plan.TargetCount;
        return new WorkflowAlterationJobPageView(page.Items.Select(WorkflowAlterationJobSummaryView.From).ToArray(), page.NextCursor, page.HasNext, page.Items.Count, totalCount);
    }

    public async Task<WorkflowAlterationJobView> GetJobAsync(GetWorkflowAlterationJob request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PlanId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.JobId);
        var plan = await store.FindPlanAsync(request.PlanId, cancellationToken);
        if (plan is null || !requestContext.CanAccess(plan))
            throw new WorkflowAlterationResourceNotFoundException();
        var job = await store.FindJobAsync(request.JobId, cancellationToken);
        if (job is null || !StringComparer.Ordinal.Equals(job.PlanId, plan.PlanId))
            throw new WorkflowAlterationResourceNotFoundException();
        return WorkflowAlterationJobView.From(job, plan.SubmittedBy);
    }

    public async Task<WorkflowAlterationPlanCancellationView> CancelAsync(CancelWorkflowAlterationPlan request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PlanId);
        var existing = await store.FindPlanAsync(request.PlanId, cancellationToken);
        if (existing is null || !requestContext.CanAccess(existing))
            throw new WorkflowAlterationResourceNotFoundException();
        var terminalNoOp = IsTerminal(existing.Status);
        var plan = await planService.CancelAsync(existing.PlanId, cancellationToken);
        return new WorkflowAlterationPlanCancellationView(await WorkflowAlterationProjection.ToPlanAsync(store, plan, cancellationToken), terminalNoOp);
    }

    private static bool IsTerminal(WorkflowAlterationPlanStatus status) => status is
        WorkflowAlterationPlanStatus.Completed or WorkflowAlterationPlanStatus.CompletedWithFailures or
        WorkflowAlterationPlanStatus.Failed or WorkflowAlterationPlanStatus.Cancelled;

    internal static WorkflowAlterationTargetSelector ToTarget(WorkflowAlterationTargetRequest? request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var hasIds = request.ExecutionIds is { Count: > 0 };
        var hasQuery = request.Query is not null;
        if (hasIds == hasQuery)
            throw new ArgumentException("Exactly one target selector is required.", nameof(request));
        return hasIds
            ? WorkflowAlterationTargetSelector.ForExecutionIds(request.ExecutionIds!)
            : WorkflowAlterationTargetSelector.ForQuery(ToQuery(request.Query!));
    }

    internal static IReadOnlyCollection<WorkflowAlterationEnvelope> ToEnvelopes(IReadOnlyCollection<WorkflowAlterationEnvelopeRequest>? requests)
    {
        if (requests is null || requests.Count == 0)
            throw new ArgumentException("At least one alteration is required.", nameof(requests));
        return requests.Select(request => new WorkflowAlterationEnvelope(
            request.Kind ?? throw new ArgumentException("An alteration kind is required.", nameof(requests)),
            request.SchemaVersion,
            request.Payload)).ToArray();
    }

    internal static WorkflowAlterationQuerySelector ToQuery(WorkflowAlterationQueryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new WorkflowAlterationQuerySelector(
            request.DefinitionId,
            ParseEnum<WorkflowExecutionStatus>(request.Status, "status"),
            ParseEnum<WorkflowRunKind>(request.RunKind, "runKind"),
            request.From,
            request.To,
            request.CorrelationId,
            request.WorkflowExecutionId,
            request.ArtifactId,
            request.MatchAllAuthorized);
    }

    private static TEnum? ParseEnum<TEnum>(string? text, string name) where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        if (!Enum.TryParse<TEnum>(text, true, out var value) || !Enum.IsDefined(value))
            throw new ArgumentOutOfRangeException(name, $"'{text}' is not a supported {name}.");
        return value;
    }
}

/// <summary>The alteration-plan operations the runtime endpoints dispatch to.</summary>
public interface IWorkflowAlterationPlanApiService
{
    Task<WorkflowAlterationPlanSubmissionView> SubmitAsync(SubmitWorkflowAlterationPlan request, CancellationToken cancellationToken);
    Task<WorkflowAlterationPlanView> GetPlanAsync(GetWorkflowAlterationPlan request, CancellationToken cancellationToken);
    Task<WorkflowAlterationJobPageView> PageJobsAsync(PageWorkflowAlterationJobs request, CancellationToken cancellationToken);
    Task<WorkflowAlterationJobView> GetJobAsync(GetWorkflowAlterationJob request, CancellationToken cancellationToken);
    Task<WorkflowAlterationPlanCancellationView> CancelAsync(CancelWorkflowAlterationPlan request, CancellationToken cancellationToken);
}

public sealed class WorkflowAlterationAdmissionRejectedException(TimeSpan? retryAfter)
    : InvalidOperationException("Runtime alteration admission is currently at capacity.")
{
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

/// <summary>Uniform missing-or-inaccessible signal deliberately mapped to a 404 without resource details.</summary>
public sealed class WorkflowAlterationResourceNotFoundException : Exception;

internal static class WorkflowAlterationProjection
{
    public static async Task<WorkflowAlterationPlanView> ToPlanAsync(
        IWorkflowAlterationStore store,
        WorkflowAlterationPlanState plan,
        CancellationToken cancellationToken)
    {
        var counts = await CountJobsAsync(store, plan.PlanId, cancellationToken);
        return new WorkflowAlterationPlanView(
            plan.PlanId,
            plan.Status.ToString(),
            plan.CreatedAt,
            plan.SealedAt,
            plan.StartedAt,
            plan.CompletedAt,
            plan.CancellationRequestedAt,
            WorkflowAlterationTargetSummaryView.From(plan.Target),
            Alterations(plan),
            counts,
            WorkflowAlterationFailureView.From(plan.SafeFailure),
            WorkflowAlterationOperatorProvenanceView.From(plan.SubmittedBy),
            WorkflowAlterationLinksView.For(plan.PlanId));
    }

    private static IReadOnlyCollection<WorkflowAlterationDescriptorView> Alterations(WorkflowAlterationPlanState plan) =>
        plan.AlterationDescriptors
            .Select((alteration, ordinal) => new WorkflowAlterationDescriptorView(
                ordinal,
                alteration.Kind,
                alteration.SchemaVersion))
            .ToArray();

    private static async Task<WorkflowAlterationPlanCountsView> CountJobsAsync(IWorkflowAlterationStore store, string planId, CancellationToken cancellationToken)
    {
        var summary = await store.GetJobCountsAsync(planId, cancellationToken);
        var plan = await store.FindPlanAsync(planId, cancellationToken) ?? throw new InvalidOperationException("The alteration plan disappeared during projection.");
        return new WorkflowAlterationPlanCountsView(
            plan.CapturedSoFar,
            plan.TargetCount,
            summary.Pending,
            summary.Running,
            summary.Succeeded,
            summary.Failed,
            summary.Cancelled);
    }
}
