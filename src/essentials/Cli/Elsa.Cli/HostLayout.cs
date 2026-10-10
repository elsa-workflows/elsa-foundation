using Elsa.Cli.Worker;

namespace Elsa.Cli;

/// <summary>
/// The published host output a command runs against: the directory, the application's own name, and the two
/// files that state how it resolves assemblies.
/// </summary>
/// <remarks>
/// Both files are required before anything else happens (FR-011). A self-contained or single-file publish
/// produces neither, and a tool that discovered that only when the worker failed to start would report a
/// host that cannot be inspected as a tooling fault.
/// </remarks>
public sealed record HostLayout(string Directory, string Name, string RuntimeConfig, string DepsFile)
{
    private const string RuntimeConfigSuffix = ".runtimeconfig.json";
    private const string DepsSuffix = ".deps.json";

    public static HostLayout Resolve(string host)
    {
        var directory = Path.GetFullPath(host);
        if (File.Exists(directory))
        {
            throw CliRefusal.Resolution(
                "host-not-a-directory",
                $"--host must name a published host's output directory; '{directory}' is a file.");
        }

        if (!System.IO.Directory.Exists(directory))
            throw CliRefusal.Resolution("host-missing", $"The host directory '{directory}' does not exist.");

        var candidates = System.IO.Directory
            .EnumerateFiles(directory, $"*{RuntimeConfigSuffix}")
            .Select(file => Path.GetFileName(file)[..^RuntimeConfigSuffix.Length])
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (candidates.Length == 0)
        {
            throw CliRefusal.Resolution(
                "host-runtime-files-missing",
                $"The host directory '{directory}' carries no application to inspect.",
                [
                    $"An <application>{RuntimeConfigSuffix} is required and none is present.",
                    $"An <application>{DepsSuffix} is required and none is present.",
                    "A single-file or self-contained publish produces neither; publish framework-dependent output instead."
                ]);
        }

        var pairs = candidates.Where(name => File.Exists(Path.Join(directory, name + DepsSuffix))).ToArray();
        if (pairs.Length == 0)
        {
            throw CliRefusal.Resolution(
                "host-runtime-files-missing",
                $"The host directory '{directory}' has no application carrying both files this tool needs.",
                [.. candidates.Select(name => $"'{name}{RuntimeConfigSuffix}' is present and '{name}{DepsSuffix}' is missing.")]);
        }

        if (pairs.Length > 1)
        {
            throw CliRefusal.Resolution(
                "host-ambiguous",
                $"The host directory '{directory}' carries more than one application, and nothing there says which one to inspect.",
                [.. pairs.Select(name => $"'{name}' carries both {RuntimeConfigSuffix} and {DepsSuffix}.")]);
        }

        var application = pairs[0];
        return new(
            directory,
            application,
            Path.Join(directory, application + RuntimeConfigSuffix),
            Path.Join(directory, application + DepsSuffix));
    }
}

/// <summary>
/// A refusal the front end makes before, or instead of, reaching the worker — shaped exactly like the
/// worker's own so both render through one path.
/// </summary>
public sealed class CliRefusal(int exitCode, string code, string message, IReadOnlyList<string>? details = null)
    : Exception(message)
{
    public int ExitCode { get; } = exitCode;

    public string Code { get; } = code;

    public IReadOnlyList<string> Details { get; } = details ?? [];

    public static CliRefusal Usage(string code, string message, IReadOnlyList<string>? details = null) =>
        new(ToolExitCode.Refusal, code, message, details);

    public static CliRefusal Resolution(string code, string message, IReadOnlyList<string>? details = null) =>
        new(ToolExitCode.ResolutionFailure, code, message, details);

    public WorkerResponse ToResponse() => new()
    {
        ExitCode = ExitCode,
        Error = new() { Code = Code, Message = Message, Details = Details }
    };
}
