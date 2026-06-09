using Elsa.Mediator.Core.Contracts;
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
        catch (ArgumentException e)
        {
            ThrowError(e, 400);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Unexpected error occurred when handling request '{type}'", typeof(TRequest));

            ThrowError("Unexpected error occurred", 400);
        }
    }
}
