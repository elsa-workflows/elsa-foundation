using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Runtime.Services.Incidents;

/// <summary>
/// Default <see cref="IRuntimeFaultCapturePolicy"/>: captures the exception's full type name and message, and the
/// stack trace only when <see cref="RuntimeFaultCaptureOptions.CaptureStackTrace"/> is enabled. Falls back to the
/// exception type name when the message is blank so a fault is never captured as an empty string. For a
/// <see cref="SecretMaskedException"/> it reports the type name of the exception that one stands in for, with the masked
/// message and stack trace, so masking a fault changes its text and nothing else about it.
/// </summary>
public sealed class DefaultRuntimeFaultCapturePolicy : IRuntimeFaultCapturePolicy
{
    private readonly RuntimeFaultCaptureOptions _options;

    public DefaultRuntimeFaultCapturePolicy(IOptions<RuntimeFaultCaptureOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    /// <summary>Creates a policy with default options, for fallback construction outside DI.</summary>
    public static DefaultRuntimeFaultCapturePolicy CreateDefault() => new(Options.Create(new RuntimeFaultCaptureOptions()));

    public RuntimeFaultInfo Capture(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var (exceptionType, typeName) = exception is SecretMaskedException masked
            ? (masked.OriginalExceptionType, ShortName(masked.OriginalExceptionType))
            : (exception.GetType().FullName ?? exception.GetType().Name, exception.GetType().Name);
        var message = string.IsNullOrWhiteSpace(exception.Message) ? typeName : exception.Message;
        var stackTrace = _options.CaptureStackTrace ? exception.StackTrace : null;

        return new RuntimeFaultInfo(exceptionType, message, stackTrace);
    }

    /// <summary>
    /// The type name without its namespace, declaring type or generic arguments, as
    /// <see cref="System.Reflection.MemberInfo.Name"/> gives it.
    /// </summary>
    private static string ShortName(string fullName)
    {
        var name = fullName.Split('[')[0];
        return name[(name.LastIndexOfAny(['.', '+']) + 1)..];
    }
}
