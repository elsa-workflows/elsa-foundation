namespace Elsa.Persistence.Groundwork.Exceptions;

public sealed class GroundworkRuntimeCheckpointWriterException(string message, Exception innerException)
    : Exception(message, innerException);
