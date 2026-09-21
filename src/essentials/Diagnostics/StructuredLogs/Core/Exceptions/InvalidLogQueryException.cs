namespace Elsa.Diagnostics.StructuredLogs.Core.Exceptions;

/// <summary>
/// Thrown when a diagnostics query carries invalid input (e.g. an unrecognised log level). Endpoints map
/// this to a domain-scoped <c>400</c> instead of leaking infrastructure parsing exceptions.
/// </summary>
public sealed class InvalidLogQueryException : StructuredLogsException
{
    public InvalidLogQueryException(string message)
        : base(message)
    {
    }
}
