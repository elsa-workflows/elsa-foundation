using Groundwork.Documents.Store;
using OpenIddict.Abstractions;

namespace Elsa.Foundation.Identity.OpenIddict.Groundwork.Exceptions;

/// <summary>Raised before provider access when an OpenIddict operation has no admitted bounded route.</summary>
public sealed class OpenIddictGroundworkCapabilityException(string operation) : InvalidOperationException(
    $"{UnsupportedGenericQueryCode}: OpenIddict Groundwork operation '{operation}' is not admitted by a bounded storage capability.")
{
    /// <summary>The stable code for every rejected OpenIddict generic query delegate.</summary>
    public const string UnsupportedGenericQueryCode = "ELSA-OIDC-GW-001";

    public string Code => UnsupportedGenericQueryCode;

    public string Operation { get; } = string.IsNullOrWhiteSpace(operation)
        ? throw new ArgumentException("An operation identity is required.", nameof(operation))
        : operation;
}

/// <summary>Raised when the selected provider/schema declaration cannot serve OpenIddict traffic.</summary>
public sealed class OpenIddictGroundworkReadinessException(string message, Exception? innerException = null) :
    InvalidOperationException(message, innerException);

/// <summary>Wraps malformed or unsupported canonical OpenIddict record content.</summary>
public sealed class OpenIddictGroundworkSerializationException(string message, Exception? innerException = null) :
    InvalidOperationException(message, innerException);

/// <summary>Wraps a non-cancellation provider failure at the OpenIddict adapter boundary.</summary>
public sealed class OpenIddictGroundworkProviderException(string operation, Exception innerException) :
    InvalidOperationException($"OpenIddict Groundwork provider operation '{operation}' failed.", innerException)
{
    public string Operation { get; } = operation;
}

/// <summary>
/// Raised by <c>OpenIddictGroundworkAtomicWrite</c> (spec 106 T030) when a mutation's commit outcome could
/// not be classified within its bounded reconciliation window: the write may or may not have committed, and
/// its mutation receipt could not be read back to settle the question. Mirrors
/// <c>GroundworkIdentityUncertainCommitException</c>.
/// </summary>
public sealed class OpenIddictGroundworkUncertainCommitException(string message, Exception? innerException = null) :
    InvalidOperationException(message, innerException);

public static class OpenIddictGroundworkFailureMapper
{
    public static OpenIddictGroundworkCapabilityException UnsupportedGenericQuery(string operation) => new(operation);

    public static Exception Translate(Exception exception, string operation = "storage")
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception is OperationCanceledException
            ? exception
            : new OpenIddictGroundworkProviderException(operation, exception);
    }

    public static Exception WriteFailure(DocumentStoreWriteStatus status) => status switch
    {
        DocumentStoreWriteStatus.ConcurrencyConflict => new OpenIddictExceptions.ConcurrencyException(
            "The OpenIddict record was changed by another operation."),
        _ => new OpenIddictGroundworkProviderException(
            "write",
            new InvalidOperationException($"Groundwork document write returned '{status}'."))
    };
}
