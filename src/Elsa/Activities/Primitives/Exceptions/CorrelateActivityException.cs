namespace Elsa.Activities.Primitives.Exceptions;

/// <summary>
/// Raised by the <c>Correlate</c> activity when it is executed outside an Elsa runtime activity execution context.
/// </summary>
public sealed class CorrelateActivityException : Exception
{
    public CorrelateActivityException(string message)
        : base(message)
    {
    }
}
