namespace Elsa.Activities.Primitives.Exceptions;

/// <summary>
/// Raised by the <c>SetName</c> activity when it is executed outside an Elsa runtime activity execution context.
/// </summary>
public sealed class SetNameActivityException : Exception
{
    public SetNameActivityException(string message)
        : base(message)
    {
    }
}
