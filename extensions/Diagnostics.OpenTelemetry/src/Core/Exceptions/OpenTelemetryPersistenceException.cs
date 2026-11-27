using System.Collections.ObjectModel;

namespace Elsa.Diagnostics.OpenTelemetry.Core.Exceptions;

/// <summary>Stable failure classifications exposed by OpenTelemetry persistence contracts.</summary>
public enum OpenTelemetryPersistenceFailureReason
{
    CapabilityUnavailable,
    ConflictingOperation,
    ExpiredOperation,
    InvalidRecord,
    CorruptData,
    ProviderFailure
}

/// <summary>
/// Provider-neutral base exception for failures crossing an OpenTelemetry persistence boundary.
/// Infrastructure details remain available only through <see cref="Exception.InnerException"/>.
/// </summary>
public abstract class OpenTelemetryPersistenceException : Exception
{
    protected OpenTelemetryPersistenceException(
        OpenTelemetryPersistenceFailureReason reason,
        string operation,
        string message,
        IReadOnlyDictionary<string, string>? context = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        Reason = reason;
        Operation = operation;
        Context = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(context ?? new Dictionary<string, string>(), StringComparer.Ordinal));
    }

    public OpenTelemetryPersistenceFailureReason Reason { get; }
    public string Operation { get; }
    public IReadOnlyDictionary<string, string> Context { get; }
}

/// <summary>Reports that a required portable store capability is unavailable.</summary>
public sealed class OpenTelemetryPersistenceCapabilityException(
    string operation,
    string capability,
    string message,
    IReadOnlyDictionary<string, string>? context = null)
    : OpenTelemetryPersistenceException(
        OpenTelemetryPersistenceFailureReason.CapabilityUnavailable,
        operation,
        message,
        With(context, "capability", capability))
{
    private static IReadOnlyDictionary<string, string> With(
        IReadOnlyDictionary<string, string>? context,
        string key,
        string value)
    {
        var result = new Dictionary<string, string>(context ?? new Dictionary<string, string>(), StringComparer.Ordinal)
        {
            [key] = value
        };
        return result;
    }
}

/// <summary>Reports an operation-identity conflict without exposing the concrete store type.</summary>
public sealed class OpenTelemetryPersistenceConflictException(
    string operation,
    string message,
    IReadOnlyDictionary<string, string> context,
    Exception innerException)
    : OpenTelemetryPersistenceException(
        OpenTelemetryPersistenceFailureReason.ConflictingOperation,
        operation,
        message,
        context,
        innerException);

/// <summary>Reports that a retry can no longer safely continue.</summary>
public sealed class OpenTelemetryPersistenceExpiredException(
    string operation,
    string message,
    IReadOnlyDictionary<string, string> context,
    Exception innerException)
    : OpenTelemetryPersistenceException(
        OpenTelemetryPersistenceFailureReason.ExpiredOperation,
        operation,
        message,
        context,
        innerException);

/// <summary>Reports that normalized telemetry cannot be accepted by the portable persistence contract.</summary>
public sealed class OpenTelemetryPersistenceValidationException(
    string operation,
    string message,
    IReadOnlyDictionary<string, string> context,
    Exception innerException)
    : OpenTelemetryPersistenceException(
        OpenTelemetryPersistenceFailureReason.InvalidRecord,
        operation,
        message,
        context,
        innerException);

/// <summary>Reports malformed or inconsistent durable OpenTelemetry persistence state.</summary>
public sealed class OpenTelemetryPersistenceDataException(
    string operation,
    string message,
    IReadOnlyDictionary<string, string> context,
    Exception? innerException = null)
    : OpenTelemetryPersistenceException(
        OpenTelemetryPersistenceFailureReason.CorruptData,
        operation,
        message,
        context,
        innerException);

/// <summary>Reports an operational persistence failure without exposing the concrete provider.</summary>
public sealed class OpenTelemetryPersistenceUnavailableException(
    string operation,
    string message,
    IReadOnlyDictionary<string, string> context,
    Exception innerException)
    : OpenTelemetryPersistenceException(
        OpenTelemetryPersistenceFailureReason.ProviderFailure,
        operation,
        message,
        context,
        innerException);
