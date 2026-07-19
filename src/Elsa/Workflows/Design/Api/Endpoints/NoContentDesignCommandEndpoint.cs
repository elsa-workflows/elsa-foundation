using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Design.Api.Endpoints;

internal abstract class NoContentDesignCommandEndpoint<TCommand>(
    ICommandSender commandSender,
    ILogger logger) : ElsaEndpoint<TCommand>
    where TCommand : ICommand
{
    public override async Task HandleAsync(TCommand request, CancellationToken cancellationToken)
    {
        try
        {
            await commandSender.Send(request, cancellationToken);
            await Send.NoContentAsync(cancellationToken);
        }
        catch (EntityNotFoundException exception)
        {
            ThrowError(exception.Message, 404);
        }
        catch (WorkflowFolderRestructureConflictException exception)
        {
            ThrowError(exception.Message, 409);
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
            logger.LogError(exception, "Unexpected error occurred when handling request '{type}'", typeof(TCommand));
            ThrowError("Unexpected error occurred", 500);
        }
    }
}
