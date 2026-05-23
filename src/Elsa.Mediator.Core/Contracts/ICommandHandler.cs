using Elsa.Mediator.Core.Models;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Input;

namespace Elsa.Mediator.Core.Contracts;

/// <summary>
/// Represents a command handler.
/// </summary>
public interface ICommandHandler
{
}

/// <summary>
/// Represents a command handler.
/// </summary>
/// <typeparam name="TCommand">The type of the command to handle.</typeparam>
/// <typeparam name="TResult">The type of the result.</typeparam>
public interface ICommandHandler<in TCommand, TResult> : ICommandHandler where TCommand : ICommand
{
    /// <summary>
    /// Handles the command.
    /// </summary>
    /// <param name="command">The command to handle.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result.</returns>
    Task<TResult> Handle(TCommand command, CancellationToken cancellationToken);
}

/// <summary>
/// Represents a command handler.
/// </summary>
/// <typeparam name="TCommand">The type of the command to handle.</typeparam>
public interface ICommandHandler<in TCommand> : ICommandHandler<TCommand, Unit> where TCommand : ICommand
{
}
