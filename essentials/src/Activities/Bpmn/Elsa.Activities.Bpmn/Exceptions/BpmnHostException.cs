namespace Elsa.Activities.Bpmn.Exceptions;

/// <summary>A BPMN host boundary could not preserve or apply the accepted execution contract.</summary>
public sealed class BpmnHostException(string message, Exception? innerException = null) : Exception(message, innerException);
