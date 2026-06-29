namespace Elsa.Activities.Primitives.Exceptions;

/// <summary>
/// Raised by the <c>SetOutput</c> activity when it is executed outside an Elsa runtime activity execution context.
/// </summary>
public sealed class SetOutputActivityException : Exception
{
    public SetOutputActivityException(string message)
        : base(message)
    {
    }
}
