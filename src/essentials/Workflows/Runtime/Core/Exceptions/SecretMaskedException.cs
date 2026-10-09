using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Core.Exceptions;

/// <summary>
/// Stands in for an exception thrown while an activity was activated or ran, once a value resolved from a secret for
/// that activity execution is registered with <see cref="IRuntimeSecretMask"/> (spec 188, FR-012). The activities
/// runtime's fault boundaries hand this on instead of the original, so the message, stack trace and inner exception
/// chain that the fault, the incident and a log line rendering the exception read are masked. The failure code and the
/// type names are not masked (see the remarks).
/// </summary>
/// <remarks>
/// <para>
/// It carries, from the exception it stands in for: the message, the stack trace and the inner exception chain, each
/// passed through the mask (one <see cref="SecretMaskedException"/> per level of the chain; the messages of an
/// <see cref="AggregateException"/>'s other inner exceptions survive in its masked message); the full name and the name
/// of its type, in <see cref="OriginalExceptionType"/> and <see cref="OriginalExceptionTypeName"/>, which the fault
/// capture policy reports instead of this type's; and its classification, when it implements
/// <see cref="IRuntimeFaultClassification"/>, so masking does not turn a retryable failure into a permanent one.
/// </para>
/// <para>
/// The failure code is copied as it is, not passed through the mask: masking a short value that happens to occur in a
/// code (a value <c>e</c> in <c>StoreUnavailable</c>) would corrupt the persisted code. A secret resolution failure code
/// is validated as an ASCII code name (<see cref="RuntimeSecretResolution.Failure"/>); a code another
/// <see cref="IRuntimeFaultClassification"/> implementation reports is persisted as that implementation chose it, validated only as non-blank. The
/// type names are not masked either: they come from the <see cref="Type"/>, not from text activity code wrote.
/// </para>
/// <para>
/// Nothing else is carried: not the original exception object, its <see cref="Exception.Data"/>, its other properties,
/// or its source.
/// </para>
/// </remarks>
public sealed class SecretMaskedException : Exception, IRuntimeFaultClassification
{
    private readonly string? _stackTrace;

    /// <param name="exception">The exception this stands in for. It is not kept.</param>
    /// <param name="mask">Masks one piece of text produced for the activity execution that failed.</param>
    public SecretMaskedException(Exception exception, Func<string, string> mask)
        : base(MaskMessage(exception, mask), MaskInner(exception, mask))
    {
        var type = exception.GetType();
        OriginalExceptionType = type.FullName ?? type.Name;
        OriginalExceptionTypeName = type.Name;
        _stackTrace = exception.StackTrace is { } stackTrace ? mask(stackTrace) : null;
        HResult = exception.HResult;
        if (exception is not IRuntimeFaultClassification classification)
            return;

        IsRetryable = classification.IsRetryable;
        FailureCode = classification.FailureCode;
    }

    /// <summary>The full name of the type of the exception this stands in for.</summary>
    public string OriginalExceptionType { get; }

    /// <summary>
    /// The name of the type of the exception this stands in for, without its namespace or declaring type, as
    /// <see cref="System.Reflection.MemberInfo.Name"/> gives it.
    /// </summary>
    public string OriginalExceptionTypeName { get; }

    /// <summary>The masked stack trace of the exception this stands in for, where it was thrown.</summary>
    public override string? StackTrace => _stackTrace;

    public bool IsRetryable { get; }

    public string? FailureCode { get; }

    private static string MaskMessage(Exception exception, Func<string, string> mask)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(mask);
        return mask(exception.Message);
    }

    private static SecretMaskedException? MaskInner(Exception exception, Func<string, string> mask) =>
        exception.InnerException is { } inner ? new SecretMaskedException(inner, mask) : null;
}
