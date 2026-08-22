using Elsa.Api.AspNetCore;
using Elsa.Mediator.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Api.Mediator;

/// <summary>
/// An endpoint whose handling is an existing mediator request handler.
/// </summary>
/// <remarks>
/// The migration bridge: the endpoint class owns the route, metadata, and permission, while the
/// domain logic stays in its <see cref="IRequestHandler{TRequest,TResponse}"/>. Absorbing that logic
/// into <c>HandleAsync</c> later is a per-endpoint decision, made together with the tests that fake
/// the sender seam today.
/// </remarks>
public abstract class RequestEndpoint<TRequest, TResponse> : ApiEndpoint<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : notnull
{
    public override Task<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken) =>
        HttpContext.RequestServices.GetRequiredService<IRequestSender>().Send(request, cancellationToken);
}

/// <summary>An endpoint whose handling is an existing mediator command handler returning a body.</summary>
public abstract class CommandEndpoint<TCommand, TResponse> : ApiEndpoint<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
    where TResponse : notnull
{
    public override Task<TResponse> HandleAsync(TCommand command, CancellationToken cancellationToken) =>
        HttpContext.RequestServices.GetRequiredService<ICommandSender>().Send(command, cancellationToken);
}

/// <summary>An endpoint whose handling is an existing mediator command handler returning no content.</summary>
public abstract class CommandEndpoint<TCommand> : ApiEndpoint<TCommand>
    where TCommand : ICommand
{
    public override Task HandleAsync(TCommand command, CancellationToken cancellationToken) =>
        HttpContext.RequestServices.GetRequiredService<ICommandSender>().Send(command, cancellationToken);
}
