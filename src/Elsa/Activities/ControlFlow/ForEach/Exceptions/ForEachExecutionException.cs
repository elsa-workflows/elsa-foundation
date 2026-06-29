namespace Elsa.Activities.ForEach.Exceptions;

public sealed class ForEachExecutionException : Exception
{
    public ForEachExecutionException(string message)
        : base(message)
    {
    }

    public ForEachExecutionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
