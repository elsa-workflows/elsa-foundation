using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Declares <see cref="IRuntimeSecretResolver"/> as a single-implementation replacement contract (framework constitution
/// §2.6.2).
/// </summary>
[AttributeUsage(AttributeTargets.Interface, Inherited = false)]
public sealed class RuntimeSecretResolverReplacementContractAttribute : Attribute;

/// <summary>
/// Resolves one secret reference for one tenant when an activity is activated. The runtime owns this contract so it
/// never references a secrets module; a bridge feature implements it over the host's secret store.
/// </summary>
/// <remarks>
/// <para>
/// This is a <b>replacement contract</b> (framework constitution §2.6.2), declared by
/// <see cref="RuntimeSecretResolverReplacementContractAttribute"/>: at most one implementation is meaningful per
/// container. The runtime registers none and does not itself detect a second registration; the feature that registers
/// the implementation (the Secrets bridge, spec 188 slice 4) must refuse one. A host that composes none cannot resolve
/// secrets at all, which activation reports as a missing capability that parks the activity, not as a resolution
/// failure.
/// </para>
/// <para>
/// The tenant is the partition the execution runs under (<see cref="IWorkflowExecutionPartitionAccessor"/>), which is
/// the scope the instance's own rows are stored under. When the instance records a tenant, it must match that partition,
/// or resolution refuses with <c>TenantMismatch</c> before the resolver is called. No binding, request payload, setting
/// or default selects it.
/// </para>
/// </remarks>
[RuntimeSecretResolverReplacementContract]
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

    /// <summary>The partition the execution runs under, never one a binding chose.</summary>
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
