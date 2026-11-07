namespace Elsa.Workflows.Runtime.Core.Exceptions;

/// <summary>A requested test-time boundary cannot be established; real time is never a fallback.</summary>
public sealed class WorkflowTestTimeUnavailableException(string code, string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException)
{
    public string Code { get; } = code;
}
