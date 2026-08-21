using Elsa.Api.AspNetCore;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Commands;
using Elsa.Workflows.Design.Api.Constants;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Projections;
using Elsa.Workflows.Design.Api.Requests;
using Elsa.Workflows.Design.Api.Services;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.Json;

namespace Elsa.Workflows.Design.Api;

/// <summary>Maps the workflow design management surface using ordinary ASP.NET Core endpoints.</summary>
/// <summary>Workflow definition and version endpoints: list, create, submit, read, update, delete, restore, and version access.</summary>
public static partial class WorkflowsDesignApi
{
    private static async Task HandleListDefinitionsAsync(HttpContext context)
    {
        var query = context.Request.Query;
        var request = new ListDefinitions(
            Query(query, "id"), Query(query, "name"), Query(query, "searchTerm"), Query(query, "description"), NullableBool(query, "tenantAgnostic"), Query(query, "state"));
        await RequestResult<ListDefinitions, WorkflowDefinitionListView>(context, request);
    }

    private static async Task HandleAddDefinitionAsync(HttpContext context) =>
        await CommandResult<AddDefinition, WorkflowDefinitionDetailsView>(context, await ReadJsonAsync<AddDefinition>(context));

    private static async Task HandleSubmitDefinitionAsync(HttpContext context) =>
        await CommandResult<SubmitDefinition, SubmittedWorkflowDefinitionView>(context, await ReadJsonAsync<SubmitDefinition>(context));

    private static Task HandleSubmitSchemaAsync(HttpContext context) =>
        RequestResult<GetWorkflowDefinitionSubmitSchema, WorkflowDefinitionSubmitSchemaView>(context, new());

    private static Task HandleGetDefinitionAsync(HttpContext context) =>
        RequestResult<GetDefinition, WorkflowDefinitionDetailsView>(context, new(Route(context, "definitionId") ?? string.Empty));

    private static async Task HandleDeleteDefinitionAsync(HttpContext context)
    {
        if (DeleteWithoutJsonBody(context))
        {
            await NoContentResult(context, new SoftDeleteDefinition(null, Route(context, "definitionId") ?? string.Empty));
            return;
        }
        var request = await ReadJsonAsync<SoftDeleteDefinition>(context);
        if (request is not null)
            request = request with { DefinitionId = Route(context, "definitionId") ?? request.DefinitionId };
        await NoContentResult(context, request);
    }

    private static async Task HandleUpdateDefinitionAsync(HttpContext context)
    {
        var request = await ReadJsonAsync<UpdateDefinitionMetadata>(context);
        if (request is not null)
            request = request with { DefinitionId = Route(context, "definitionId") ?? request.DefinitionId };
        await CommandResult<UpdateDefinitionMetadata, WorkflowDefinitionDetailsView>(context, request);
    }

    private static async Task HandleDeleteDefinitionPermanentlyAsync(HttpContext context)
    {
        if (DeleteWithoutJsonBody(context))
        {
            await NoContentResult(context, new DeleteDefinitionPermanently(null, Route(context, "definitionId") ?? string.Empty));
            return;
        }
        var request = await ReadJsonAsync<DeleteDefinitionPermanently>(context);
        if (request is not null)
            request = request with { DefinitionId = Route(context, "definitionId") ?? request.DefinitionId };
        await NoContentResult(context, request);
    }

    private static async Task HandleRestoreDefinitionAsync(HttpContext context)
    {
        if (string.IsNullOrWhiteSpace(context.Request.ContentType) ||
            !string.Equals(context.Request.ContentType.Split(';', 2)[0].Trim(), "application/json", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
            return;
        }
        var request = await ReadJsonAsync<RestoreDefinition>(context);
        if (request is not null)
            request = request with { DefinitionId = Route(context, "definitionId") ?? request.DefinitionId };
        await NoContentResult(context, request);
    }

    private static Task HandleListVersionsAsync(HttpContext context) =>
        RequestResult<ListDefinitionVersions, IEnumerable<WorkflowDefinitionVersionSummary>>(context, new(Route(context, "definitionId") ?? string.Empty));

    private static async Task HandleAddVersionAsync(HttpContext context) =>
        await CommandResult<AddVersion, WorkflowDefinitionVersionDetailsView>(context, await ReadJsonAsync<AddVersion>(context));

    private static Task HandleGetVersionAsync(HttpContext context) =>
        RequestResult<GetVersion, WorkflowDefinitionVersionDetailsView>(context, new(Route(context, "versionId") ?? string.Empty));
}
