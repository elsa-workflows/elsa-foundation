using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Resolves one secret reference for one tenant when an activity is activated. The runtime owns this contract so it
/// never references a secrets module; a bridge feature implements it over the host's secret store.
/// </summary>
/// <remarks>
/// <para>
/// This is a <b>replacement contract</b> (framework constitution §2.6.2): at most one implementation is composed per
/// container. A host that composes none cannot resolve secrets at all, which activation reports as a missing
/// capability that parks the activity, not as a resolution failure.
/// </para>
/// <para>
/// The tenant always comes from the executing workflow instance: activation reads it from
/// <see cref="IWorkflowExecutionPartitionAccessor"/>. No binding, request payload, setting or default supplies it.
/// </para>
/// </remarks>
public interface IRuntimeSecretResolver
{
    /// <summary>
    /// Resolves <paramref name="request"/>. Returns a failure for every domain outcome; throws only
    /// <see cref="OperationCanceledException"/> when <paramref name="cancellationToken"/> is canceled.
    /// </summary>
    ValueTask<RuntimeSecretResolution> ResolveAsync(RuntimeSecretResolutionRequest request, CancellationToken cancellationToken = default);
}

/// <summary>One secret reference to resolve under one tenant.</summary>
public sealed record RuntimeSecretResolutionRequest
{
    public RuntimeSecretResolutionRequest(string tenantId, RuntimeSecretReference reference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(reference);

        TenantId = tenantId;
        Reference = reference;
    }

    /// <summary>The tenant of the executing workflow instance, never one a binding chose.</summary>
    public string TenantId { get; }

    public RuntimeSecretReference Reference { get; }
}

/// <summary>
/// The outcome of one resolution: the value, or a failure code and whether retrying may help. Create it through
/// <see cref="Success"/> or <see cref="Failure"/>, so a result never carries both a value and a failure.
/// </summary>
/// <remarks>
/// A class rather than a record on purpose: a record's generated <see cref="object.ToString"/> would print the
/// value. Nothing here may reach a log, an exception message, a metric, a span or persisted state.
/// </remarks>
public sealed class RuntimeSecretResolution
{
    private RuntimeSecretResolution(bool succeeded, string? value, string? failureCode, bool isRetryable)
    {
        Succeeded = succeeded;
        Value = value;
        FailureCode = failureCode;
        IsRetryable = isRetryable;
    }

    public bool Succeeded { get; }

    /// <summary>The resolved text. Set only when <see cref="Succeeded"/>.</summary>
    public string? Value { get; }

    /// <summary>
    /// A stable code name, such as <c>NotFound</c> or <c>StoreUnavailable</c>, never the store's error text. Set only
    /// when resolution failed.
    /// </summary>
    public string? FailureCode { get; }

    /// <summary>Whether a later attempt may succeed. Meaningful only when resolution failed.</summary>
    public bool IsRetryable { get; }

    public static RuntimeSecretResolution Success(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(succeeded: true, value, failureCode: null, isRetryable: false);
    }

    /// <summary>
    /// A failed resolution. <paramref name="failureCode"/> must be a code name (a letter followed by letters or digits),
    /// so free text, which could carry store detail, is refused here rather than reaching a fault message.
    /// </summary>
    public static RuntimeSecretResolution Failure(string failureCode, bool isRetryable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        if (!char.IsAsciiLetter(failureCode[0]) || !failureCode.All(char.IsAsciiLetterOrDigit))
            throw new ArgumentException("A secret resolution failure code must be a code name of ASCII letters and digits.", nameof(failureCode));

        return new(succeeded: false, value: null, failureCode, isRetryable);
    }
}
