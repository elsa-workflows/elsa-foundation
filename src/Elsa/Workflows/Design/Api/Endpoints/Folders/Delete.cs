using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Api.Commands;
using Elsa.Workflows.Design.Api.Constants;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Design.Api.Endpoints.Folders;

internal sealed class Delete(ICommandSender commandSender, ILogger<Delete> logger) : ElsaEndpoint<DeleteWorkflowFolder>
{
    public override void Configure()
    {
        Delete(RouteConstants.GetRoute("folders/{folderId}"));
        ConfigurePermissions(PermissionNames.WorkflowDesignManage);
    }

    public override async Task HandleAsync(DeleteWorkflowFolder request, CancellationToken cancellationToken)
    {
        try { await commandSender.Send(request, cancellationToken); await Send.NoContentAsync(cancellationToken); }
        catch (WorkflowFolderRestructureConflictException exception) { ThrowError(exception.Message, 409); }
        catch (EntityNotFoundException exception) { ThrowError(exception.Message, 404); }
        catch (ArgumentException exception) { ThrowError(exception, 400); }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { logger.LogError(exception, "Unexpected error occurred when deleting a workflow folder"); ThrowError("Unexpected error occurred", 500); }
    }
}
