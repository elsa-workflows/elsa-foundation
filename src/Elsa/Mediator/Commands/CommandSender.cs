using Elsa.Mediator.Core.Contracts;
using Elsa.Mediator.Core.Models;

namespace Elsa.Mediator.Commands;

/// <summary>
/// Sends commands through the shared mediator pipeline. Commands resolve their handler through the
/// closed <see cref="ICommandHandler{TCommand,TResult}"/> generic.
/// </summary>
public sealed class CommandSender(IServiceProvider serviceProvider, CommandPipeline commandPipeline) : ICommandSender
{
    private const string Kind = "command";

    /// <inheritdoc />
    public async Task<T> Send<T>(ICommand<T> command, CancellationToken cancellationToken = default)
        where T : notnull
    {
        var context = new MessageContext<T>(command, typeof(ICommandHandler<,>), Kind, serviceProvider, cancellationToken);
        await commandPipeline.Execute(context);

        return context.Result!;
    }

    /// <inheritdoc />
    public async Task Send(ICommand command, CancellationToken cancellationToken = default)
    {
        var context = new MessageContext<Unit>(command, typeof(ICommandHandler<,>), Kind, serviceProvider, cancellationToken);
        await commandPipeline.Execute(context);
    }
}
