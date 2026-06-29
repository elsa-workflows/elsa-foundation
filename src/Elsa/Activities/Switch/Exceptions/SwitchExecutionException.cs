namespace Elsa.Activities.Switch.Exceptions;

public sealed class SwitchExecutionException : Exception
{
    public SwitchExecutionException(string message)
        : base(message)
    {
    }

    public SwitchExecutionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
