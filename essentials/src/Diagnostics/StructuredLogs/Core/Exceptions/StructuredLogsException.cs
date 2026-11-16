namespace Elsa.Diagnostics.StructuredLogs.Core.Exceptions;

/// <summary>
/// The domain exception for the structured-logs diagnostics feature. Infrastructure failures crossing a
/// feature boundary are wrapped in this type so callers never see raw infrastructure exceptions.
/// </summary>
public class StructuredLogsException : Exception
{
    public StructuredLogsException(string message)
        : base(message)
    {
    }

    public StructuredLogsException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
