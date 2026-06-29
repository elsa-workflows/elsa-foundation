namespace Elsa.Activities.Parallel.Exceptions;

public sealed class ParallelExecutionException : Exception
{
    public ParallelExecutionException(string message)
        : base(message)
    {
    }

    public ParallelExecutionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
