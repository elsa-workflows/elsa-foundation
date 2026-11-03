namespace Elsa.Workflows.Runtime.Core.Exceptions;

/// <summary>
/// Signals that scheduler work could not be evaluated because a durable runtime dependency is temporarily unavailable.
/// The safe code and summary deliberately exclude provider diagnostics and retained workflow data.
/// Delayed retry supports bookmark resumes, invocation and structural notification/control/completion delivery and workflow checkpoints with a live, unconsumed durable queue claim.
/// Whatever the provider, a claimed dispatch of those kinds that already sent a non-retaining checkpoint to the checkpoint
/// store, without that commit consuming its claim, is handled as a handler fault (acknowledged and recorded,
/// <c>Poisoned</c> by default) instead: running it again would repeat that checkpoint (#3683). A checkpoint store throws
/// this exception to refuse a commit without applying it, and such a refused commit does not count as sent.
/// Otherwise, unsupported command/provider combinations propagate this signal without acknowledging or poisoning the work.
/// See <c>docs/runtime-fault-behavior.md</c>, "Temporarily unavailable work".
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
