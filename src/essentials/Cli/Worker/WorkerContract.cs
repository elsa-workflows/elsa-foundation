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

/// <summary>The commands this worker backs.</summary>
public static class WorkerCommands
{
    public const string List = "list";
    public const string Plan = "plan";
    public const string Script = "script";
    public const string Apply = "apply";
    public const string Validate = "validate";
    public const string PostMigrate = "post-migrate";

    public static readonly string[] All = [List, Plan, Script, Apply, Validate, PostMigrate];

    /// <summary>The commands that open the host's database and so need a connection (D7).</summary>
    public static bool OpensDatabase(string command) => command is Apply or Validate or PostMigrate;
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

    /// <summary>The <c>--shell</c> the provider-agreement check was narrowed to, or <c>null</c> for every configured shell.</summary>
    public string? Shell { get; init; }

    /// <summary>
    /// The host's enabled shell features, read from <c>shells.json</c> plus its <c>--environment</c> overlay
    /// (FR-035). Present — even empty — exactly when that configuration was found beside the host, which is
    /// what the manifest's <c>providerAgreement: checked</c> states; <c>null</c> when none was found at all.
    /// Each entry carries a feature's <c>Provider</c> setting and nothing else: no other shell setting is
    /// lifted out of the file, so no connection string can travel here.
    /// </summary>
    public IReadOnlyList<WorkerShellFeature>? Shells { get; init; }

    /// <summary>
    /// The name of the environment variable <c>--connection-env</c> names (D7). Only its name travels here;
    /// the worker reads the value from its own (inherited) environment, never from this front end's request.
    /// Required by the commands that open a database unless <see cref="Connection"/> is given instead.
    /// </summary>
    public string? ConnectionEnv { get; init; }

    /// <summary>
    /// The connection string itself, read by this front end from its own stdin when <c>--connection-stdin</c>
    /// was given (D7) — the one case where the value has nowhere to travel but this request. Never populated
    /// from <c>--connection-env</c>, and never logged, echoed, or included in a refusal.
    /// </summary>
    public string? Connection { get; init; }
}

/// <summary>Which modules a command runs against, discriminated the same way the tooling contract discriminates it.</summary>
public sealed record WorkerSelection
{
    public const string AllKind = "all";
    public const string ModulesKind = "modules";

    /// <summary>Every module the host's own enabled shell features map to through <c>[UsesEfModule]</c> (FR-028).</summary>
    public const string FromHostKind = "from-host";

    public string? Kind { get; init; }

    public IReadOnlyList<string>? Modules { get; init; }
}

/// <summary>
/// One feature a shell enables, reduced to what the provider-agreement check needs: the shell, the CShells
/// feature name, and that feature's configured <c>Provider</c> setting (<c>null</c> when the shell sets
/// none). A feature a shell disables never appears here — an unenabled feature is ignored (FR-036).
/// </summary>
public sealed record WorkerShellFeature
{
    public string? Shell { get; init; }

    public string? Feature { get; init; }

    public string? Provider { get; init; }
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
