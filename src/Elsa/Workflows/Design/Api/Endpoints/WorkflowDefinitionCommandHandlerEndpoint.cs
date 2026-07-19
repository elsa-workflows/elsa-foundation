using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Design.Api.Endpoints;

/// <summary>Maps retryable folder-membership fences consistently for response-bearing definition commands.</summary>
internal abstract class WorkflowDefinitionCommandHandlerEndpoint<TCommand, TResponse>(ICommandSender commandSender, ILogger logger)
    : ElsaEndpoint<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
    where TResponse : notnull
{
    public override async Task HandleAsync(TCommand request, CancellationToken cancellationToken)
    {
        try { await Send.OkAsync(await commandSender.Send(request, cancellationToken), cancellationToken); }
        catch (WorkflowFolderRestructureConflictException exception) { ThrowError(exception.Message, 409); }
        catch (EntityNotFoundException exception) { ThrowError(exception.Message, 404); }
        catch (ArgumentException exception) { ThrowError(exception, 400); }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unexpected error occurred when handling request '{type}'", typeof(TCommand));
            ThrowError("Unexpected error occurred", 500);
        }
    }
}
