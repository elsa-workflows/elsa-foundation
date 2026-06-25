namespace Elsa.Persistence.Groundwork.Exceptions;

public sealed class GroundworkActivityExecutionInspectionStoreException(string message, Exception innerException)
    : Exception(message, innerException);
