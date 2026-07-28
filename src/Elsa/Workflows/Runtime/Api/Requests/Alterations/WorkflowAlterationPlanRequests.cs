using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Models.Alterations;

namespace Elsa.Workflows.Runtime.Api.Requests.Alterations;

/// <summary>HTTP submission body. The idempotency key is bound only by the endpoint from the request header.</summary>
public sealed record SubmitWorkflowAlterationPlan(
    WorkflowAlterationTargetRequest? Target,
    IReadOnlyCollection<WorkflowAlterationEnvelopeRequest>? Alterations)
    : IRequest<WorkflowAlterationPlanSubmissionView>
{
    [JsonIgnore]
    public string? IdempotencyKey { get; init; }
}

public sealed record GetWorkflowAlterationPlan(string PlanId) : IRequest<WorkflowAlterationPlanView>;

public sealed record PageWorkflowAlterationJobs(string PlanId, int? Take, string? Cursor)
    : IRequest<WorkflowAlterationJobPageView>;

public sealed record GetWorkflowAlterationJob(string PlanId, string JobId) : IRequest<WorkflowAlterationJobView>;

public sealed record CancelWorkflowAlterationPlan(string PlanId) : IRequest<WorkflowAlterationPlanCancellationView>;

public sealed record WorkflowAlterationTargetRequest(
    IReadOnlyCollection<string>? ExecutionIds,
    WorkflowAlterationQueryRequest? Query);

public sealed record WorkflowAlterationQueryRequest(
    string? DefinitionId = null,
    string? Status = null,
    string? RunKind = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    string? CorrelationId = null,
    string? WorkflowExecutionId = null,
    string? ArtifactId = null,
    bool MatchAllAuthorized = false);

public sealed record WorkflowAlterationEnvelopeRequest(string? Kind, int SchemaVersion, JsonElement Payload);
