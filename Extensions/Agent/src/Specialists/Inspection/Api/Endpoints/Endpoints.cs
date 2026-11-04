using Elsa.Agent.Specialists.Core.Contracts;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Workflows.Publishing.Api.Authorization;
using Elsa.Workflows.Publishing.Api.Services;
using NativeEndpoints;

namespace Elsa.Agent.Specialists.Inspection.Api.Endpoints;

[Get("/" + SpecialistInspectionApi.TestRunEvidenceRoute)]
[RequirePermission(WorkflowPublishingPermissions.Manage)]
internal sealed class GetTestRunEvidence(IActivityDraftTestRunService testRuns, ISpecialistInvocationInspector inspector)
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
        return new(run.TestRunId, run.DraftId, run.DraftRevision, run.ArtifactId, run.WorkflowExecutionId, run.Status, invocation);
    }
}
