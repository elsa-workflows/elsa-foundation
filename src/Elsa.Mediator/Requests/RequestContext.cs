using Elsa.Mediator.Core.Contracts;

namespace Elsa.Mediator.Requests;

public sealed record RequestContext(IRequest Request, Type ResponseType, IServiceProvider ServiceProvider, CancellationToken CancellationToken) : IRequestContext
{
    public object? Response { get; set; }
}

public sealed record RequestContext<TResponse>(IRequest Request, IServiceProvider ServiceProvider, CancellationToken CancellationToken) : IRequestContext
    where TResponse : notnull
{
    public TResponse? Response { get; set; }

    public Type ResponseType => typeof(TResponse);

    object? IRequestContext.Response { get => Response; set => Response = (TResponse?)value; }
}