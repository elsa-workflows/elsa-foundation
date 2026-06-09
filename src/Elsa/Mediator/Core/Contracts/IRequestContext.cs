namespace Elsa.Mediator.Core.Contracts;

public interface IRequestContext
{
    IRequest Request { get; }
    Type ResponseType { get; }
    IServiceProvider ServiceProvider { get; }

    object? Response { get; set; }

    CancellationToken CancellationToken { get; }
}
