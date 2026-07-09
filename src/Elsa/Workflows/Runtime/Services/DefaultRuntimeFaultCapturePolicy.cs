using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Default <see cref="IRuntimeFaultCapturePolicy"/>: captures the exception's full type name and message, and the
/// stack trace only when <see cref="RuntimeFaultCaptureOptions.CaptureStackTrace"/> is enabled. Falls back to the
/// exception type name when the message is blank so a fault is never captured as an empty string.
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

        var exceptionType = exception.GetType().FullName ?? exception.GetType().Name;
        var message = string.IsNullOrWhiteSpace(exception.Message) ? exception.GetType().Name : exception.Message;
        var stackTrace = _options.CaptureStackTrace ? exception.StackTrace : null;

        return new RuntimeFaultInfo(exceptionType, message, stackTrace);
    }
}
