using Elsa.Api.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Publishing.Api.Authorization;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using NativeEndpoints;

namespace Elsa.Workflows.Publishing.Api.Endpoints.Workflows.Preflight;

[Post("/publishing/workflows/{versionId:regex(^(?!drafts$).+$)}/preflight")]
[RequirePermission(WorkflowPublishingPermissions.Read)]
public sealed class Endpoint(
    TimeProvider timeProvider,
    IWorkflowExecutableCompiler compiler,
    WorkflowPublicationPreflightReader preflightReader) : ApiEndpoint<PreflightWorkflowPublication, PublicationPreflightView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "PreflightWorkflowPublicationEndpoint";
        options.Accepts = ["application/json"];
        options.BodyMode = EndpointBodyMode.RequiredWithContentType;
        options.Convention(builder => builder.WithMetadata(
            new WorkflowPublicationProblemEndpointMetadata(expressionValidation: false)));
    }

    public override async Task<PublicationPreflightView> HandleAsync(PreflightWorkflowPublication request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var executable = await compiler.CompileAsync(
            new WorkflowExecutableCompileRequest(
                request.VersionId,
                WorkflowExecutableReferenceScope.Published,
                now,
                now,
                ExpiresAt: null,
                "artifact-",
                new Dictionary<string, string> { ["slice"] = "workflow-execution-vertical-slice" }),
            cancellationToken);
        var plan = await preflightReader.EvaluateAsync(
            executable,
            PublicationIntents.RequestIntent(request.Action, request.SlotName),
            request.ExpectedPublicationId,
            $"preflight:{request.VersionId}",
            cancellationToken);
        var resolved = plan.ResolvedAction;
        return new PublicationPreflightView(
            resolved.WorkflowDefinitionId,
            resolved.WorkflowDefinitionVersionId,
            resolved.SlotName,
            PublicationContract.ToView(resolved.Action),
            PublicationContract.ToView(resolved.PolicySource),
            resolved.PolicyRevision,
            plan.Result.CanActivate,
            plan.Result.Changes.Select(PublicationTriggerChangeView.From).ToArray(),
            plan.Result.Conflicts.Select(PublicationTriggerConflictView.From).ToArray());
    }
}
