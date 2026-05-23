namespace Elsa.Mediator.Core.Contracts;

public interface ICommandSender
{
    /// <summary>
    /// Sends a command using the default strategy.
    /// </summary>
    Task<T> Send<T>(ICommand<T> command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a command using the default strategy.
    /// </summary>
    /// <param name="command">The command to send.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task Send(ICommand command, CancellationToken cancellationToken = default);
}
