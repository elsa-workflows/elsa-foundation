using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Workflows.Design.Api.Constants;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Projections;
using Elsa.Workflows.Design.Core.Services;

namespace Elsa.Workflows.Design.Api.Endpoints.Authoring;

internal sealed class AnalyzeScopedVariables(ScopedVariableAuthoringContract authoring)
    : ElsaEndpoint<AnalyzeScopedVariablesRequest, ScopedVariableAnalysisResponse>
{
    public override void Configure()
    {
        Post(RouteConstants.ScopedVariableAnalysis);
        ConfigurePermissions(PermissionNames.WorkflowDesignRead);
    }

    public override async Task HandleAsync(AnalyzeScopedVariablesRequest request, CancellationToken cancellationToken)
    {
        if (request.State is null)
        {
            ThrowError("A workflow definition state is required.", 400);
            return;
        }

        if (request.NodeId is not null && string.IsNullOrWhiteSpace(request.NodeId))
        {
            ThrowError("The selected activity node id cannot be empty.", 400);
            return;
        }

        var state = request.State.ToState();
        await Send.OkAsync(new(
            authoring.GetVisibleVariables(state, request.NodeId),
            authoring.GetShadowingWarnings(state)), cancellationToken);
    }
}
