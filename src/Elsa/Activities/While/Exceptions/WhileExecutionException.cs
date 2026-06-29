namespace Elsa.Activities.While.Exceptions;

public sealed class WhileExecutionException : Exception
{
    public WhileExecutionException(string message)
        : base(message)
    {
    }

    public WhileExecutionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
