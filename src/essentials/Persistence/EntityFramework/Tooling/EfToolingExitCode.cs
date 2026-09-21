namespace Elsa.Persistence.EntityFramework.Tooling;

/// <summary>
/// The exit codes spec 171 assigns the persistence CLI. <see cref="EfToolingHost"/> reports one on every
/// response it writes, so the worker that launched it has a code to exit with without classifying anything
/// itself, and so a refusal here can never acquire a second, parallel meaning there.
/// </summary>
public static class EfToolingExitCode
{
    /// <summary>The command did what was asked.</summary>
    public const int Success = 0;

    /// <summary>A negative result: pending migrations, drift, or a required post-migration action.</summary>
    public const int NegativeResult = 1;

    /// <summary>A usage error or a refusal, including <c>script --provider Sqlite</c>.</summary>
    public const int Refusal = 2;

    /// <summary>A resolution failure: host, module, provider artifact, engine, provider disagreement, or dependency.</summary>
    public const int ResolutionFailure = 3;

    /// <summary>A database failure.</summary>
    public const int DatabaseFailure = 4;
}

/// <summary>
/// A refusal carrying the exit code the spec assigns it, a stable machine-readable
/// <see cref="Code"/>, and — for the refusals that must name every offender rather than the first one —
/// one <see cref="Details"/> line per offender.
/// </summary>
internal sealed class EfToolingRefusal(int exitCode, string code, string message, IReadOnlyList<string>? details = null)
    : Exception(message)
{
    public int ExitCode { get; } = exitCode;

    public string Code { get; } = code;

    public IReadOnlyList<string> Details { get; } = details ?? [];

    public static EfToolingRefusal Usage(string code, string message, IReadOnlyList<string>? details = null) =>
        new(EfToolingExitCode.Refusal, code, message, details);

    public static EfToolingRefusal Resolution(string code, string message, IReadOnlyList<string>? details = null) =>
        new(EfToolingExitCode.ResolutionFailure, code, message, details);

    /// <summary><c>validate</c> found a pending migration (FR-052): a negative result, not a refusal or a resolution failure.</summary>
    public static EfToolingRefusal NegativeResult(string code, string message, IReadOnlyList<string>? details = null) =>
        new(EfToolingExitCode.NegativeResult, code, message, details);

    /// <summary><c>apply</c> or <c>validate</c> could not reach or read the database itself.</summary>
    public static EfToolingRefusal DatabaseFailure(string code, string message, IReadOnlyList<string>? details = null) =>
        new(EfToolingExitCode.DatabaseFailure, code, message, details);
}
