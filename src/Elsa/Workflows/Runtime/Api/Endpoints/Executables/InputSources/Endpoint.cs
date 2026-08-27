using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Runtime.Api.Services;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using NativeEndpoints;

namespace Elsa.Workflows.Runtime.Api.Endpoints.Executables.InputSources;

/// <remarks>
/// Input-source metadata is Publishing-owned data surfaced through a Runtime route, so this is the
/// one Runtime endpoint gated on Publishing's read permission rather than the Runtime one.
/// </remarks>
[Get("runtime/workflows/executables/{artifactId}/source-references/{sourceReferenceId}/input-sources")]
[RequirePermission(WorkflowRuntimePermissions.WorkflowPublishingRead)]
[RuntimeProblems("handling runtime request", NotFoundArms = true)]
public sealed class Endpoint(IWorkflowExecutableInspector inspector) : ApiEndpoint<GetWorkflowExecutableInputSources, WorkflowExecutableInputSourcesView>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "GetExecutableInputSources";

    public override Task<WorkflowExecutableInputSourcesView> HandleAsync(GetWorkflowExecutableInputSources request, CancellationToken cancellationToken) =>
        inspector.GetInputSourcesAsync(request, cancellationToken);
}
