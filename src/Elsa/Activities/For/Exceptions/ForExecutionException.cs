namespace Elsa.Activities.For.Exceptions;

public sealed class ForExecutionException : Exception
{
    public ForExecutionException(string message)
        : base(message)
    {
    }

    public ForExecutionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
