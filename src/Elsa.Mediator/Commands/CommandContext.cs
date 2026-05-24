using Elsa.Mediator.Core.Contracts;
using Elsa.Mediator.Core.Models;

namespace Elsa.Mediator.Commands;

public sealed record CommandContext(ICommand Command, IServiceProvider ServiceProvider, CancellationToken CancellationToken) : ICommandContext
{
    public Type ResultType => typeof(Unit);

    public object? Result { get; set; }
}

public sealed record CommandContext<TResult> : ICommandContext
    where TResult : notnull
{
    private readonly ICommand _command;
    public CommandContext(ICommand<TResult> command, IServiceProvider serviceProvider, CancellationToken cancellationToken)
        : this((ICommand)command, serviceProvider, cancellationToken)
    {
    }

    public CommandContext(ICommand command, IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        _command = command;
        ServiceProvider = serviceProvider;
        CancellationToken = cancellationToken;
    }

    public Type ResultType => typeof(TResult);

    public ICommand Command => _command;

    public IServiceProvider ServiceProvider { get; }
    public CancellationToken CancellationToken { get; }

    public TResult? Result { get; set; }

    object? ICommandContext.Result { get => Result; set => Result = (TResult?)value; }
}