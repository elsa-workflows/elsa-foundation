using Elsa.Agent.Core.Contracts;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Workflows.Publishing.Api.Authorization;
using Elsa.Workflows.Publishing.Api.Services;
using MinimalEndpoints;

namespace Elsa.Agent.Inspection.Api.Endpoints;

[Get("/" + AgentInspectionApi.TestRunEvidenceRoute)]
[RequirePermission(WorkflowPublishingPermissions.Manage)]
internal sealed class GetTestRunEvidence(IActivityDraftTestRunService testRuns, IAgentInvocationInspector inspector, AgentEvidenceAccess access)
    : ApiEndpoint<AgentTestRunEvidenceRequest, AgentTestRunEvidenceView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "AgentTestRunEvidence";
        options.Accepts = ["*/*", "application/json"];
        options.StrictTypedParsing = true;
    }

    // The generic read authorizes the run, then the evidence comes from its own recorded root invocation, never from a caller-named one.
    public override async Task<AgentTestRunEvidenceView> HandleAsync(AgentTestRunEvidenceRequest request, CancellationToken cancellationToken)
    {
        var run = await testRuns.GetAsync(request.TestRunId, cancellationToken);
        var invocation = run.OuterActivityExecutionId is { } root
            ? await inspector.InspectAsync(run.WorkflowExecutionId, root, cancellationToken)
            : null;
        var values = invocation is not null && (await access.AuthorizeAsync(run.WorkflowExecutionId, cancellationToken)).Values;
        return new(run.TestRunId, run.DraftId, run.DraftRevision, run.ArtifactId, run.WorkflowExecutionId, run.Status,
            AgentEvidenceAccess.Redact(invocation, values), values);
    }
}

[Get("/" + AgentInspectionApi.InvocationEvidenceRoute)]
[RequirePermission(AgentInspectionApi.RuntimeRead)]
internal sealed class GetInvocationEvidence(IAgentInvocationInspector inspector, AgentEvidenceAccess access)
    : ApiEndpoint<AgentInvocationEvidenceRequest, AgentInvocationEvidenceView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "AgentInvocationEvidence";
        options.Accepts = ["*/*", "application/json"];
        options.StrictTypedParsing = true;
    }

    // As the runtime's own activity execution reads do, a run the caller may not see or inspect, and an occurrence that is
    // not an agent, all read alike as not found, so the answer never confirms that a run exists.
    public override async Task<AgentInvocationEvidenceView> HandleAsync(AgentInvocationEvidenceRequest request, CancellationToken cancellationToken)
    {
        var grant = await access.AuthorizeAsync(request.WorkflowExecutionId, cancellationToken);
        if (!grant.Found || !grant.Structure) throw new Elsa.Primitives.Exceptions.EntityNotFoundException("No visible agent invocation matches.");
        var invocation = await inspector.InspectAsync(request.WorkflowExecutionId, request.ActivityExecutionId, cancellationToken)
            ?? throw new Elsa.Primitives.Exceptions.EntityNotFoundException("No visible agent invocation matches.");
        return new(AgentEvidenceAccess.Redact(invocation, grant.Values)!, grant.Values);
    }
}
