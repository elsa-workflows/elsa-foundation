using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Api.Commands;
using Elsa.Workflows.Design.Api.Constants;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Design.Api.Endpoints.Folders;

internal sealed class Rename(ICommandSender commandSender, ILogger<Rename> logger) : ElsaEndpoint<RenameWorkflowFolder, WorkflowFolderView>
{
    public override void Configure()
    {
        Post(RouteConstants.GetRoute("folders/{folderId}/rename"));
        ConfigurePermissions(PermissionNames.WorkflowDesignManage);
    }

    public override async Task HandleAsync(RenameWorkflowFolder request, CancellationToken cancellationToken)
    {
        try { await Send.ResponseAsync(await commandSender.Send(request, cancellationToken), 200, cancellationToken); }
        catch (WorkflowFolderRestructureConflictException exception) { ThrowError(exception.Message, 409); }
        catch (WorkflowFolderSiblingConflictException exception) { ThrowError(exception.Message, 409); }
        catch (EntityNotFoundException exception) { ThrowError(exception.Message, 404); }
        catch (ArgumentException exception) { ThrowError(exception, 400); }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { logger.LogError(exception, "Unexpected error occurred when renaming a workflow folder"); ThrowError("Unexpected error occurred", 500); }
    }
}
