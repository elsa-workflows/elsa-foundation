namespace Elsa.Activities.Primitives.Exceptions;

/// <summary>
/// Raised by the <c>Finish</c> activity when it is executed outside an Elsa runtime activity execution context.
/// </summary>
public sealed class FinishActivityException : Exception
{
    public FinishActivityException(string message)
        : base(message)
    {
    }
}
