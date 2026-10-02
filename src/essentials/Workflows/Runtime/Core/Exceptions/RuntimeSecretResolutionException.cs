using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Core.Exceptions;

/// <summary>
/// A secret-bound activity input could not be resolved at activation, so the activity faults. The message names the
/// secret reference and the failure code only: never a value, and never the store's error text.
/// </summary>
/// <remarks>
/// The code is a resolver's failure code (only <c>StoreUnavailable</c> is retryable), <see cref="TenantMismatch"/>,
/// <see cref="ConversionFailed"/> or <see cref="ResolverFailed"/>. The classification reaches the recorded fault through
/// <see cref="IRuntimeFaultClassification"/>.
/// </remarks>
public sealed class RuntimeSecretResolutionException : Exception, IRuntimeFaultClassification
{
    /// <summary>
    /// The executing instance records a tenant that differs from the partition it runs under, so no secret is read.
    /// </summary>
    public const string TenantMismatch = "TenantMismatch";

    /// <summary>
    /// The resolved text could not be converted to the input's type with the conversion plan pinned at publish. It is
    /// not <c>TypeMismatch</c>, which means only that the stored secret's type differs from the reference's.
    /// </summary>
    public const string ConversionFailed = "ConversionFailed";

    /// <summary>
    /// The resolver threw instead of returning a result, which breaks its contract. What it threw is dropped, because
    /// its message may carry the value or store-private detail.
    /// </summary>
    public const string ResolverFailed = "ResolverFailed";

    public RuntimeSecretResolutionException(string referenceName, string failureCode, bool isRetryable)
        : base(FormatMessage(referenceName, failureCode))
    {
        ReferenceName = referenceName;
        FailureCode = failureCode;
        IsRetryable = isRetryable;
    }

    public string ReferenceName { get; }

    public string FailureCode { get; }

    public bool IsRetryable { get; }

    private static string FormatMessage(string referenceName, string failureCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(referenceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        return $"Secret '{referenceName}' could not be resolved ({failureCode}).";
    }
}
