namespace Elsa.Mediator.Core.Contracts;

/// <summary>
/// Represents a command.
/// </summary>
public interface ICommand
{
}

/// <summary>
/// Represents a command.
/// </summary>
/// <typeparam name="T">The type of the result.</typeparam>
public interface ICommand<T> : ICommand
{
}