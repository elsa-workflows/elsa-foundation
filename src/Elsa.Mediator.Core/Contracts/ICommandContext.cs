namespace Elsa.Mediator.Core.Contracts;

public interface ICommandContext
{
    ICommand Command { get; }

    Type ResultType { get; }

    IServiceProvider ServiceProvider { get; }

    CancellationToken CancellationToken { get; }

    object? Result { get; set; }
}
