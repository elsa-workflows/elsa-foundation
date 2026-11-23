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

namespace Elsa.Workflows.Design.Api.Endpoints.Authoring.PlanVariableRename;

/// <summary>
/// Plans how a draft's scripts follow a variable rename (spec 1222 FR-021, FR-022): the script edits that keep each
/// resolvable literal read on the renamed variable, and the references to repair by hand. It changes nothing; Studio
/// applies the edits to its draft and shows the flags. The script of a script activity (the <c>elsa.outcomes</c>
/// facet's <c>scriptInput</c>) is planned too when the activity catalog is available.
/// </summary>
[Post("scoped-variables/rename-plan")]
[RequirePermission(WorkflowDesignPermissions.Read)]
public sealed class Endpoint(IScriptVariableRenamePlanner planner, IActivityDefinitionLookup? catalog = null)
    : ApiEndpoint<PlanVariableRenameRequest, ScriptVariableRenamePlan>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "AuthoringPlanVariableRename";
        options.Accepts = ["application/json"];
    }

    public override async Task<ScriptVariableRenamePlan> HandleAsync(PlanVariableRenameRequest request, CancellationToken cancellationToken)
    {
        // Reported through the shared translator, which maps ArgumentException to 400.
        if (request.State is null)
            throw new ArgumentException("A workflow definition state is required.");
        if (string.IsNullOrWhiteSpace(request.ScopeId) || string.IsNullOrWhiteSpace(request.ReferenceKey) || string.IsNullOrWhiteSpace(request.NewName))
            throw new ArgumentException("The variable's scope id, reference key and new name are required.");

        return await planner.PlanAsync(
            request.State.ToState(),
            request.ScopeId,
            request.ReferenceKey,
            request.NewName,
            catalog is null ? null : ScriptInputKeyAsync,
            cancellationToken);
    }

    private async ValueTask<string?> ScriptInputKeyAsync(ActivityNode node, CancellationToken cancellationToken)
    {
        if (node.Intrinsic is not null || string.IsNullOrWhiteSpace(node.ActivityVersionId))
            return null;
        if (await catalog!.FindVersion(node.ActivityVersionId, cancellationToken) is not { } version)
            return null;
        var facet = version.DesignFacets.FirstOrDefault(item => item.Kind == "elsa.outcomes")?.Payload;
        return facet is { ValueKind: JsonValueKind.Object } payload &&
               payload.TryGetProperty("scriptInput", out var scriptInput) &&
               scriptInput.GetString() is { Length: > 0 } key
            ? key
            : null;
    }
}
