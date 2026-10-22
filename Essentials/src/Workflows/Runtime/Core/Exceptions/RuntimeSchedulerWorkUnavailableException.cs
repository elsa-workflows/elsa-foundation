namespace Elsa.Workflows.Runtime.Core.Exceptions;

/// <summary>
/// Signals that scheduler work could not be evaluated because a durable runtime dependency is temporarily unavailable.
/// The safe code and summary deliberately exclude provider diagnostics and retained workflow data.
/// Delayed retry is currently supported for bookmark-resume work with a live, unconsumed durable queue claim.
/// Unsupported command/provider combinations propagate this signal without acknowledging or poisoning the work.
/// </summary>
public sealed class RuntimeSchedulerWorkUnavailableException(string code, string safeSummary)
    : Exception(ValidateSafeSummary(safeSummary))
{
    public string Code { get; } = ValidateCode(code);

    public string SafeSummary { get; } = safeSummary;

    private static string ValidateCode(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value;
    }

    private static string ValidateSafeSummary(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value;
    }
}
