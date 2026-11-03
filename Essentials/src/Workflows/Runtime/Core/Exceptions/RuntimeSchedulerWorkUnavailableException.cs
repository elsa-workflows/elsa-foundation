namespace Elsa.Workflows.Runtime.Core.Exceptions;

/// <summary>
/// Signals that scheduler work could not be evaluated because a durable runtime dependency is temporarily unavailable.
/// The safe code and summary deliberately exclude provider diagnostics and retained workflow data.
/// Delayed retry supports bookmark resumes, invocation and structural notification/control/completion delivery and workflow checkpoints with a live, unconsumed durable queue claim.
/// Unsupported command/provider combinations propagate this signal without acknowledging or poisoning the work.
/// Supported work whose dispatch already sent a non-retaining checkpoint to the checkpoint store, without that commit
/// consuming its claim, is handled as a handler fault instead of released: that checkpoint is durable, or will be at the
/// coalescing segment's flush, so running the work again would repeat it.
/// </summary>
public sealed class RuntimeSchedulerWorkUnavailableException : Exception
{
    public RuntimeSchedulerWorkUnavailableException(string code, string safeSummary) : this(code, safeSummary, null) { }

    public RuntimeSchedulerWorkUnavailableException(string code, string safeSummary, Exception? innerException)
        : base(ValidateSafeSummary(safeSummary), innerException)
    {
        Code = ValidateCode(code);
        SafeSummary = safeSummary;
    }

    public string Code { get; }
    public string SafeSummary { get; }

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
