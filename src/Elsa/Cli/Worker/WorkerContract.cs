using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Cli.Worker;

/// <summary>
/// The versioned JSON <c>dotnet-elsa</c> exchanges with its worker over stdin/stdout. Two processes, one
/// package: the front end resolves nothing about persistence itself, and the worker answers nothing the
/// front end did not ask for.
/// </summary>
/// <remarks>
/// Deliberately separate from the frozen <c>EfToolingContract</c> the worker speaks on the other side. That
/// one is a contract with an assembly inside the host's closure, versioned independently of this tool; this
/// one is private to the two halves of one package and carries what only the front end knows (where the
/// host is) and what only the worker can see (the host's deps file and package set). The tooling response is
/// passed through verbatim rather than re-modelled, so there is one place that defines its shape.
/// </remarks>
public static class WorkerContract
{
    /// <summary>The only envelope version this build speaks in either direction.</summary>
    public const int Version = 1;

    /// <summary>
    /// Case-sensitive camelCase with unmapped members refused, matching the tooling contract: a worker and a
    /// front end from different builds must fail loudly rather than silently ignore a field one of them
    /// meant as an instruction.
    /// </summary>
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

/// <summary>The commands this slice's worker backs; <c>apply</c>, <c>validate</c> and <c>post-migrate</c> arrive with their own slices.</summary>
public static class WorkerCommands
{
    public const string List = "list";
    public const string Plan = "plan";
    public const string Script = "script";

    public static readonly string[] All = [List, Plan, Script];
}

/// <summary>
/// The exit codes spec 171 assigns the CLI, mirrored here so the front end can classify a failure of its
/// own — a missing deps file, a worker that never started — with the same vocabulary the worker and the
/// host's tooling entry point use.
/// </summary>
public static class ToolExitCode
{
    public const int Success = 0;
    public const int NegativeResult = 1;
    public const int Refusal = 2;
    public const int ResolutionFailure = 3;
    public const int DatabaseFailure = 4;
}

/// <summary>One request from the front end to the worker.</summary>
public sealed record WorkerRequest
{
    public int Version { get; init; } = WorkerContract.Version;

    /// <summary>One of <see cref="WorkerCommands.All"/>.</summary>
    public string? Command { get; init; }

    /// <summary>The <c>--host</c> directory, already validated to carry a runtimeconfig/deps pair (FR-011).</summary>
    public string? HostDirectory { get; init; }

    /// <summary>The host application's own name, as the manifest's <c>host.name</c> records it.</summary>
    public string? HostName { get; init; }

    /// <summary>The host's <c>.deps.json</c>, the one file that states what the host pins.</summary>
    public string? DepsFile { get; init; }

    /// <summary>Every <c>--packages</c> root, in the order given. Empty means "resolve from the host alone".</summary>
    public IReadOnlyList<string> PackageRoots { get; init; } = [];

    /// <summary>Which modules to run against; <c>null</c> on <c>list</c> means every module in the closure.</summary>
    public WorkerSelection? Selection { get; init; }

    public string? Provider { get; init; }

    public string? Schema { get; init; }

    /// <summary>Where <c>script</c> writes its artifact.</summary>
    public string? Output { get; init; }

    /// <summary>The <c>--environment</c> value the manifest records (FR-038, FR-047).</summary>
    public string? Environment { get; init; }

    /// <summary>The <c>--shell</c> the provider-agreement check used, once slice 9 adds one.</summary>
    public string? Shell { get; init; }
}

/// <summary>Which modules a command runs against, discriminated the same way the tooling contract discriminates it.</summary>
public sealed record WorkerSelection
{
    public const string AllKind = "all";
    public const string ModulesKind = "modules";

    public string? Kind { get; init; }

    public IReadOnlyList<string>? Modules { get; init; }
}

/// <summary>One response from the worker to the front end.</summary>
public sealed record WorkerResponse
{
    public int Version { get; init; } = WorkerContract.Version;

    /// <summary>The code the front end exits with, whether it came from the worker or from the host's tooling entry point.</summary>
    public int ExitCode { get; init; }

    /// <summary>The tooling entry point's own response, verbatim, when one was produced.</summary>
    public JsonElement? Tooling { get; init; }

    /// <summary>The worker's own refusal, for everything that fails before or around the tooling call.</summary>
    public WorkerError? Error { get; init; }
}

/// <summary>A refusal the worker itself made: resolving the host, its packages, or its tooling entry point.</summary>
public sealed record WorkerError
{
    public string Code { get; init; } = "";

    public string Message { get; init; } = "";

    /// <summary>One line per offender, for the refusals that must name every one rather than the first.</summary>
    public IReadOnlyList<string> Details { get; init; } = [];
}
