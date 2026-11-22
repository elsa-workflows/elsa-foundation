namespace Elsa.Activities.Compensation.Exceptions;

/// <summary>Raised when a compensation scope's compiled structure or private state is invalid at run time.</summary>
public sealed class CompensationScopeExecutionException : Exception
{
    public CompensationScopeExecutionException(string message) : base(message)
    {
    }

    public CompensationScopeExecutionException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
