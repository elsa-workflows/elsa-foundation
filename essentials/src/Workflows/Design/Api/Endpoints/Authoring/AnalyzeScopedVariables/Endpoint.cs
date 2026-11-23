using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Projections;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using MinimalEndpoints;

namespace Elsa.Workflows.Design.Api.Endpoints.Authoring.AnalyzeScopedVariables;

[Post("scoped-variables/analyze")]
[RequirePermission(WorkflowDesignPermissions.Read)]
public sealed class Endpoint(
    ScopedVariableAuthoringContract authoring,
    IScriptBodyWriteAnalyzer? scriptWrites = null,
    IActivityDefinitionLookup? catalog = null)
    : ApiEndpoint<AnalyzeScopedVariablesRequest, ScopedVariableAnalysisResponse>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "AuthoringAnalyzeScopedVariables";
        options.Accepts = ["application/json"];
    }

    public override async Task<ScopedVariableAnalysisResponse> HandleAsync(AnalyzeScopedVariablesRequest request, CancellationToken cancellationToken)
    {
        // Reported through the shared translator, which maps ArgumentException to 400.
        if (request.State is null)
            throw new ArgumentException("A workflow definition state is required.");
        if (request.NodeId is not null && string.IsNullOrWhiteSpace(request.NodeId))
            throw new ArgumentException("The selected activity node id cannot be empty.");

        var state = request.State.ToState();
        // Spec 1231 FR-036: a script activity's may-write set comes from its body, resolved as publication pins it.
        var writes = request.NodeId is { } nodeId && scriptWrites is not null && catalog is not null
            ? await scriptWrites.AnalyzeAsync(state, nodeId, ScriptInputKeyAsync, cancellationToken)
            : null;
        return new ScopedVariableAnalysisResponse(
            authoring.GetVisibleVariables(state, request.NodeId), authoring.GetShadowingWarnings(state), writes);
    }

    // The script input of a script activity: the elsa.outcomes facet's scriptInput, as the rename plan finds it.
    private async ValueTask<string?> ScriptInputKeyAsync(ActivityNode node, CancellationToken cancellationToken)
    {
        if (node.Intrinsic is not null || string.IsNullOrWhiteSpace(node.ActivityVersionId))
            return null;
        if (await catalog!.FindVersion(node.ActivityVersionId, cancellationToken) is not { } version)
            return null;
        var facet = version.DesignFacets.FirstOrDefault(item => item.Kind == "elsa.outcomes")?.Payload;
        return facet is { ValueKind: JsonValueKind.Object } payload &&
               payload.TryGetProperty("scriptInput", out var scriptInput) &&
               scriptInput.ValueKind == JsonValueKind.String &&
               scriptInput.GetString() is { Length: > 0 } key
            ? key
            : null;
    }
}
