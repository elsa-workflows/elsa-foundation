namespace Elsa.Activities.Flowchart.Exceptions;

public sealed class FlowchartExecutionException : Exception
{
    public FlowchartExecutionException(string message)
        : base(message)
    {
    }

    public FlowchartExecutionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
