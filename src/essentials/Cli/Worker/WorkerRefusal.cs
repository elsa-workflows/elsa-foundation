namespace Elsa.Cli.Worker;

/// <summary>
/// A refusal the worker makes itself, carrying the exit code spec 171 assigns it, a stable machine-readable
/// <see cref="Code"/>, and one <see cref="Details"/> line per offender for the refusals that must name every
/// one rather than the first.
/// </summary>
/// <remarks>
/// Shaped like the host tooling entry point's own refusal on purpose: the front end renders both through one
/// path, so an operator cannot tell from the output whether a refusal happened before or after the process
/// boundary — only what was refused.
/// </remarks>
public sealed class WorkerRefusal(int exitCode, string code, string message, IReadOnlyList<string>? details = null)
    : Exception(message)
{
    public int ExitCode { get; } = exitCode;

    public string Code { get; } = code;

    public IReadOnlyList<string> Details { get; } = details ?? [];

    public static WorkerRefusal Usage(string code, string message, IReadOnlyList<string>? details = null) =>
        new(ToolExitCode.Refusal, code, message, details);

    public static WorkerRefusal Resolution(string code, string message, IReadOnlyList<string>? details = null) =>
        new(ToolExitCode.ResolutionFailure, code, message, details);

    public WorkerResponse ToResponse() => new()
    {
        ExitCode = ExitCode,
        Error = new() { Code = Code, Message = Message, Details = Details }
    };
}
