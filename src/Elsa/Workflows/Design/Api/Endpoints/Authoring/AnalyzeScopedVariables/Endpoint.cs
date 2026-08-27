using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Projections;
using Elsa.Workflows.Design.Core.Services;
using NativeEndpoints;

namespace Elsa.Workflows.Design.Api.Endpoints.Authoring.AnalyzeScopedVariables;

[Post("scoped-variables/analyze")]
[RequirePermission(WorkflowDesignPermissions.Read)]
public sealed class Endpoint(ScopedVariableAuthoringContract authoring)
    : ApiEndpoint<AnalyzeScopedVariablesRequest, ScopedVariableAnalysisResponse>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "AuthoringAnalyzeScopedVariables";
        options.Accepts = ["application/json"];
    }

    public override Task<ScopedVariableAnalysisResponse> HandleAsync(AnalyzeScopedVariablesRequest request, CancellationToken cancellationToken)
    {
        // Reported through the shared translator, which maps ArgumentException to 400.
        if (request.State is null)
            throw new ArgumentException("A workflow definition state is required.");
        if (request.NodeId is not null && string.IsNullOrWhiteSpace(request.NodeId))
            throw new ArgumentException("The selected activity node id cannot be empty.");

        var state = request.State.ToState();
        return Task.FromResult(new ScopedVariableAnalysisResponse(
            authoring.GetVisibleVariables(state, request.NodeId), authoring.GetShadowingWarnings(state)));
    }
}
