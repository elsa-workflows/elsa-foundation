using Elsa.Mediator.Core.Contracts;
using Elsa.Mediator.Core.Models;

namespace Elsa.Mediator.Commands;

public sealed class CommandSender(IServiceProvider serviceProvider, ICommandPipeline commandPipeline) : ICommandSender
{
    public async Task<T> Send<T>(ICommand<T> command, CancellationToken cancellationToken)
        where T : notnull
    {
        var context = new CommandContext<T>(command, serviceProvider, cancellationToken);
        await commandPipeline.Execute(context);

        return context.Result!;
    }

    public async Task Send(ICommand command, CancellationToken cancellationToken)
    {
        var context = new CommandContext<Unit>(command, serviceProvider, cancellationToken);
        await commandPipeline.Execute(context);
    }
}
