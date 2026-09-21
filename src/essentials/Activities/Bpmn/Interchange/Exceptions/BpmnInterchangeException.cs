namespace Elsa.Activities.Bpmn.Interchange.Exceptions;

public sealed class BpmnInterchangeException : Exception
{
    public BpmnInterchangeException(string message) : base(message)
    {
    }

    public BpmnInterchangeException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
