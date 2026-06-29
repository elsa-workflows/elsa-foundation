namespace Elsa.Activities.Do.Exceptions;

public sealed class DoExecutionException : Exception
{
    public DoExecutionException(string message)
        : base(message)
    {
    }

    public DoExecutionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
