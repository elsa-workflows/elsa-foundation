using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Api.Constants;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Services;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Microsoft.AspNetCore.Http;

namespace Elsa.Workflows.Design.Api.Endpoints.Definitions;

internal sealed class GetTags(WorkflowDefinitionTagApplicationService service)
    : ElsaEndpointWithoutRequest<WorkflowDefinitionTagSetView>
{
    public override void Configure()
    {
        Get(RouteConstants.GetRoute("definitions/{workflowDefinitionId}/tags"));
        ConfigurePermissions(PermissionNames.WorkflowDesignRead);
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.GetAsync(Route<string>("workflowDefinitionId")!, cancellationToken);
            HttpContext.Response.Headers.ETag = result.Revision;
            await Send.OkAsync(result, cancellationToken);
        }
        catch (EntityNotFoundException exception)
        {
            ThrowError(exception.Message, 404);
        }
    }
}

internal sealed class ReplaceTags(WorkflowDefinitionTagApplicationService service)
    : ElsaEndpoint<ReplaceWorkflowDefinitionTagsRequest, WorkflowDefinitionTagSetView>
{
    public override void Configure()
    {
        Put(RouteConstants.GetRoute("definitions/{workflowDefinitionId}/tags"));
        ConfigurePermissions(PermissionNames.WorkflowDesignTagsAssign);
    }

    public override async Task HandleAsync(
        ReplaceWorkflowDefinitionTagsRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var expectedRevision = WorkflowDefinitionTagApplicationService.Unquote(
                HttpContext.Request.Headers.IfMatch.ToString());
            var result = await service.ReplaceAsync(
                request.WorkflowDefinitionId,
                expectedRevision,
                request.TagDefinitionIds,
                cancellationToken);
            if (result.Status == WorkflowDefinitionTagReplaceStatus.Conflict)
            {
                HttpContext.Response.StatusCode = 409;
                await HttpContext.Response.WriteAsJsonAsync(
                    new WorkflowDefinitionTagConflictView(
                        "tag-set-revision-conflict",
                        WorkflowDefinitionTagApplicationService.Quote(result.CurrentRevision!)),
                    cancellationToken);
                return;
            }

            var view = WorkflowDefinitionTagApplicationService.ToView(result.TagSet!, canAssign: true);
            HttpContext.Response.Headers.ETag = view.Revision;
            await Send.OkAsync(view, cancellationToken);
        }
        catch (EntityNotFoundException exception)
        {
            ThrowError(exception.Message, 404);
        }
        catch (UnauthorizedAccessException exception)
        {
            ThrowError(exception.Message, 403);
        }
        catch (ArgumentException exception)
        {
            ThrowError(exception, 400);
        }
        catch (InvalidOperationException exception)
        {
            ThrowError(exception, 409);
        }
    }
}
