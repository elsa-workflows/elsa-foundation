namespace Elsa.Activities.Primitives.Exceptions;

/// <summary>
/// Raised by the <c>Fault</c> activity to deliberately fault its execution. The runtime engine catches
/// this and records a blocking incident rather than propagating it to the host.
/// </summary>
public sealed class FaultActivityException : Exception
{
    public FaultActivityException(string message)
        : base(message)
    {
    }
}
