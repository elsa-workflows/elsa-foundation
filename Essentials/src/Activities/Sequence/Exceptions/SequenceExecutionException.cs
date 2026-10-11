namespace Elsa.Activities.Sequence.Exceptions;

public sealed class SequenceExecutionException : Exception
{
    public SequenceExecutionException(string message)
        : base(message)
    {
    }

    public SequenceExecutionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
