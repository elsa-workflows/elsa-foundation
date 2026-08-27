using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Publishing.Api.Authorization;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using NativeEndpoints;

namespace Elsa.Workflows.Publishing.Api.Endpoints.Workflows.SnapshotPreflight;

[Post("/publishing/workflows/preflight")]
[RequirePermission(WorkflowPublishingPermissions.Read)]
public sealed class Endpoint(
    PublicationSnapshotReviewService reviews,
    TimeProvider timeProvider,
    IWorkflowExecutableCompiler compiler,
    WorkflowPublicationPreflightReader preflightReader) : ApiEndpoint<PreflightWorkflowPublicationSnapshot, PublicationSnapshotPreflightView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "PreflightWorkflowPublicationSnapshotEndpoint";
        options.Accepts = ["application/json"];
        options.BodyMode = EndpointBodyMode.RequiredWithContentType;
    }

    public override async Task<PublicationSnapshotPreflightView> HandleAsync(PreflightWorkflowPublicationSnapshot request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DefinitionId);
        ArgumentNullException.ThrowIfNull(request.State);
        ArgumentNullException.ThrowIfNull(request.Layout);
        var candidateHash = reviews.ComputeCandidateHash(request.State, request.Layout);
        var snapshotId = $"snapshot:{candidateHash}";
        var now = timeProvider.GetUtcNow();
        var executable = await compiler.CompileAsync(
            new WorkflowExecutableCompileRequest(snapshotId, WorkflowExecutableReferenceScope.Published, now, null, null,
                "artifact-", new Dictionary<string, string> { ["slice"] = "workflow-publication-snapshot-preflight" })
            {
                Source = new WorkflowExecutableCompileSource(request.DefinitionId, snapshotId, "snapshot", request.State,
                    "WorkflowDefinitionSnapshot", snapshotId, SourceVersion: null)
            }, cancellationToken);
        var plan = await preflightReader.EvaluateAsync(
            executable, PublicationIntents.RequestIntent(request.Action, request.SlotName), request.ExpectedPublicationId,
            $"preflight:{candidateHash}", cancellationToken);
        var issued = await reviews.IssueAsync(candidateHash, plan,
            request.Action is { } action ? PublicationIntentContract.ToModel(action) : null,
            request.SlotName, request.ExpectedPublicationId, PublicationRequestTenant.Resolve(HttpContext.User), cancellationToken);
        var resolved = plan.ResolvedAction;
        return new PublicationSnapshotPreflightView(
            issued.PreflightToken, issued.CandidateHash, resolved.WorkflowDefinitionId, VersionId: null,
            resolved.SlotName, PublicationContract.ToView(resolved.Action), PublicationContract.ToView(resolved.PolicySource),
            resolved.PolicyRevision, plan.Result.CanActivate,
            plan.CandidateClaims.Select(PublicationTriggerClaimView.From).ToArray(),
            plan.Result.Changes.Select(PublicationTriggerChangeView.From).ToArray(),
            plan.Result.Conflicts.Select(PublicationTriggerConflictView.From).ToArray());
    }
}
