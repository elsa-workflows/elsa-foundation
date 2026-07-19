using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Commands;
using Elsa.Workflows.Design.Api.Constants;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Elsa.Primitives.Exceptions;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Design.Api.Endpoints.Folders;

internal sealed class Create(ICommandSender commandSender, ILogger<Create> logger)
    : ElsaEndpoint<CreateWorkflowFolder, WorkflowFolderView>
{
    public override void Configure()
    {
        Post(RouteConstants.Folders);
        ConfigurePermissions(PermissionNames.WorkflowDesignManage);
    }

    public override async Task HandleAsync(CreateWorkflowFolder request, CancellationToken cancellationToken)
    {
        try
        {
            await Send.ResponseAsync(await commandSender.Send(request, cancellationToken), 201, cancellationToken);
        }
        catch (WorkflowFolderSiblingConflictException exception)
        {
            ThrowError(exception.Message, 409);
        }
        catch (EntityNotFoundException exception)
        {
            ThrowError(exception.Message, 404);
        }
        catch (ArgumentException exception)
        {
            ThrowError(exception, 400);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unexpected error occurred when creating a workflow folder");
            ThrowError("Unexpected error occurred", 500);
        }
    }
}
