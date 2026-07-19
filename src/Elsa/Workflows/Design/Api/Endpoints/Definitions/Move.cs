using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Api.Commands;
using Elsa.Workflows.Design.Api.Constants;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Design.Api.Endpoints.Definitions;

/// <summary>Moves a selection atomically without modifying authored workflow content.</summary>
internal sealed class Move(ICommandSender commandSender, ILogger<Move> logger) : ElsaEndpoint<MoveWorkflowDefinitions>
{
    public override void Configure()
    {
        Post(RouteConstants.GetRoute("definitions/move"));
        ConfigurePermissions(PermissionNames.WorkflowDesignManage);
    }

    public override async Task HandleAsync(MoveWorkflowDefinitions request, CancellationToken cancellationToken)
    {
        try
        {
            await commandSender.Send(request, cancellationToken);
            await Send.NoContentAsync(cancellationToken);
        }
        catch (WorkflowDefinitionFolderMoveConflictException exception)
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
            logger.LogError(exception, "Unexpected error occurred when moving workflow definitions");
            ThrowError("Unexpected error occurred", 500);
        }
    }
}
