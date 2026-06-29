namespace Elsa.Activities.If.Exceptions;

public sealed class IfExecutionException : Exception
{
    public IfExecutionException(string message)
        : base(message)
    {
    }

    public IfExecutionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
