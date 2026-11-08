using Elsa.Agent.Specialists.Core.Contracts;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Workflows.Publishing.Api.Authorization;
using Elsa.Workflows.Publishing.Api.Services;
using NativeEndpoints;

namespace Elsa.Agent.Specialists.Inspection.Api.Endpoints;

[Get("/" + SpecialistInspectionApi.TestRunEvidenceRoute)]
[RequirePermission(WorkflowPublishingPermissions.Manage)]
internal sealed class GetTestRunEvidence(IActivityDraftTestRunService testRuns, ISpecialistInvocationInspector inspector, SpecialistEvidenceAccess access)
    : ApiEndpoint<SpecialistTestRunEvidenceRequest, SpecialistTestRunEvidenceView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "AgentSpecialistTestRunEvidence";
        options.Accepts = ["*/*", "application/json"];
        options.StrictTypedParsing = true;
    }

    // The generic read authorizes the run, then the evidence comes from its own recorded root invocation, never from a caller-named one.
    public override async Task<SpecialistTestRunEvidenceView> HandleAsync(SpecialistTestRunEvidenceRequest request, CancellationToken cancellationToken)
    {
        var run = await testRuns.GetAsync(request.TestRunId, cancellationToken);
        var invocation = run.OuterActivityExecutionId is { } root
            ? await inspector.InspectAsync(run.WorkflowExecutionId, root, cancellationToken)
            : null;
        var values = invocation is not null && (await access.AuthorizeAsync(run.WorkflowExecutionId, cancellationToken)).Values;
        return new(run.TestRunId, run.DraftId, run.DraftRevision, run.ArtifactId, run.WorkflowExecutionId, run.Status,
            SpecialistEvidenceAccess.Redact(invocation, values), values);
    }
}

[Get("/" + SpecialistInspectionApi.InvocationEvidenceRoute)]
[RequirePermission(SpecialistInspectionApi.RuntimeRead)]
internal sealed class GetInvocationEvidence(ISpecialistInvocationInspector inspector, SpecialistEvidenceAccess access)
    : ApiEndpoint<SpecialistInvocationEvidenceRequest, SpecialistInvocationEvidenceView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "AgentSpecialistInvocationEvidence";
        options.Accepts = ["*/*", "application/json"];
        options.StrictTypedParsing = true;
    }

    // As the runtime's own activity execution reads do, a run the caller may not see or inspect, and an occurrence that is
    // not a specialist, all read alike as not found, so the answer never confirms that a run exists.
    public override async Task<SpecialistInvocationEvidenceView> HandleAsync(SpecialistInvocationEvidenceRequest request, CancellationToken cancellationToken)
    {
        var grant = await access.AuthorizeAsync(request.WorkflowExecutionId, cancellationToken);
        if (!grant.Found || !grant.Structure) throw new Elsa.Primitives.Exceptions.EntityNotFoundException("No visible specialist invocation matches.");
        var invocation = await inspector.InspectAsync(request.WorkflowExecutionId, request.ActivityExecutionId, cancellationToken)
            ?? throw new Elsa.Primitives.Exceptions.EntityNotFoundException("No visible specialist invocation matches.");
        return new(SpecialistEvidenceAccess.Redact(invocation, grant.Values)!, grant.Values);
    }
}
