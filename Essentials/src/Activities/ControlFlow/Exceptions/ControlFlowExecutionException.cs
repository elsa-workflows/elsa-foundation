namespace Elsa.Activities.ControlFlow.Exceptions;

/// <summary>
/// Thrown when a control-flow activity cannot execute its structure, inputs or a child callback. One type
/// serves every activity in this module because nothing handles the activities' failures differently; the
/// message names the activity, and the incident records the activity type.
/// </summary>
public sealed class ControlFlowExecutionException : Exception
{
    public ControlFlowExecutionException(string message)
        : base(message)
    {
    }

    public ControlFlowExecutionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
