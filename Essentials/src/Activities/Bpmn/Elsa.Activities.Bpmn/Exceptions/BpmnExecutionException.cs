namespace Elsa.Activities.Bpmn.Exceptions;

public sealed class BpmnExecutionException : Exception
{
    public BpmnExecutionException(string message) : base(message)
    {
    }

    public BpmnExecutionException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
