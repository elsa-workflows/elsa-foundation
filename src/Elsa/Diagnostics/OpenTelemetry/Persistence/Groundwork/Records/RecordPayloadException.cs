namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.Records;

/// <summary>Raised when an immutable OpenTelemetry record cannot be mapped to or from its durable payload.</summary>
public sealed class RecordPayloadException : Exception
{
    public RecordPayloadException(string message) : base(message)
    {
    }

    public RecordPayloadException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
