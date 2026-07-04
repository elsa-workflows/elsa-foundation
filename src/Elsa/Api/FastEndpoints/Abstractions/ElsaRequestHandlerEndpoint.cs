using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Exceptions;
using Microsoft.Extensions.Logging;

namespace Elsa.Api.FastEndpoints.Abstractions;

public abstract class ElsaRequestHandlerEndpoint<TRequest, TResponse>(IRequestSender requestSender, ILogger logger) : ElsaEndpoint<TRequest, TResponse>
    where TResponse : notnull
     where TRequest : IRequest<TResponse>
{
    public override async Task HandleAsync(TRequest req, CancellationToken ct)
    {
        try
        {
            var result = await requestSender.Send(req, ct);
            await Send.OkAsync(result, ct);
        }
        catch (EntityNotFoundException e)
        {
            ThrowError(e.Message, 404);
        }
        catch (ArgumentException e)
        {
            ThrowError(e, 400);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Unexpected error occurred when handling request '{type}'", typeof(TRequest));

            ThrowError("Unexpected error occurred", 500);
        }
    }
}
