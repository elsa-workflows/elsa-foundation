using Elsa.Mediator.Core.Contracts;
using Elsa.Mediator.Core.Models;

namespace Elsa.Mediator.Requests;

/// <summary>
/// Sends requests through the shared mediator pipeline. Requests resolve their handler through the
/// closed <see cref="IRequestHandler{TRequest,TResponse}"/> generic.
/// </summary>
public sealed class RequestSender(RequestPipeline requestPipeline, IServiceProvider serviceProvider) : IRequestSender
{
    private const string Kind = "request";

    /// <inheritdoc />
    public async Task<T> Send<T>(IRequest<T> request, CancellationToken cancellationToken = default)
        where T : notnull
    {
        var context = new MessageContext<T>(request, typeof(IRequestHandler<,>), Kind, serviceProvider, cancellationToken);
        await requestPipeline.Execute(context);

        return context.Result!;
    }
}
