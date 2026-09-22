using System.Reflection;
using System.Text.Json;

namespace Elsa.Cli.Worker;

/// <summary>
/// One command, end to end, inside the host's own dependency closure: resolve what the host pins, load what
/// it would load, and hand the question to the host's own tooling entry point.
/// </summary>
/// <remarks>
/// Everything this type adds to that entry point is the part the entry point deliberately cannot do
/// (FR-049): it can observe neither a <c>.deps.json</c> nor a <c>.nupkg</c>, so a module's package id and
/// version, the engine's, and where each was read from are established here and stated to it.
/// </remarks>
internal static class WorkerRunner
{
    private const string SqliteProvider = "Sqlite";

    /// <summary>
    /// The tool's own version, named only in the refusal that reports a host pinning a persistence build
    /// too old to have an entry point. It never reaches an artifact (FR-043).
    /// </summary>
    private static string ToolVersion =>
        typeof(WorkerRunner).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion is { } version
            ? version.Split('+')[0]
            : typeof(WorkerRunner).Assembly.GetName().Version?.ToString() ?? "unknown";

    public static async Task<WorkerResponse> RunAsync(WorkerRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await ExecuteAsync(request, cancellationToken);
        }
        catch (WorkerRefusal refusal)
        {
            return refusal.ToResponse();
        }
        // A cancelled run is not a failed one: reporting the operator's own Ctrl-C as a resolution failure
        // would claim an artifact was refused when it was simply never produced.
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception failure) when (IsNonFatal(failure))
        {
            return new()
            {
                ExitCode = ToolExitCode.ResolutionFailure,
                Error = new() { Code = "worker-internal-error", Message = $"{failure.GetType().Name}: {failure.Message}" }
            };
        }
    }

    /// <summary>
    /// True for anything worth reporting as a resolution failure; false for the handful of CLR exceptions
    /// that mean the process itself is no longer trustworthy, which must propagate rather than be swallowed
    /// into a tidy JSON response.
    /// </summary>
    internal static bool IsNonFatal(Exception failure) => failure is not (
        OutOfMemoryException or
        StackOverflowException or
        AccessViolationException or
        AppDomainUnloadedException or
        BadImageFormatException or
        CannotUnloadAppDomainException or
        ThreadAbortException);

    private static async Task<WorkerResponse> ExecuteAsync(WorkerRequest request, CancellationToken cancellationToken)
    {
        var command = Validate(request);
        var deps = HostDepsFile.Read(request.DepsFile!);
        // Opt-in, and the only step in this worker that may write under the host's directories. It runs
        // before the set is read, because what it writes is exactly what the read then finds; without
        // --restore it does nothing at all and no other flag sets it (ADR 0076 D10).
        await HostPackageRestore.RunAsync(request, deps, cancellationToken);
        var packages = await NuplanePackageSet.LoadAsync(request.PackageRoots, request.HostDirectory!, cancellationToken);
        foreach (var failure in packages.Failures.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            Console.Error.WriteLine($"warning: package '{failure.Key}' could not be loaded: {failure.Value}");

        HostClosure.Preload(deps);
        var tooling = ToolingEntryPoint.Resolve(
            HostClosure.LoadPersistence(),
            deps.ForAssembly(HostClosure.PersistenceAssemblyName)?.Version,
            ToolVersion);

        if (command == WorkerCommands.List)
            return await Respond(tooling, new { version = 1, command, selection = Selection(request.Selection), shells = Shells(request) }, cancellationToken);

        var provider = tooling.CanonicalProvider(request.Provider ?? throw WorkerRefusal.Usage("invalid-request", $"'{command}' needs a provider."));

        // SQLite cannot be scripted idempotently, and that refusal belongs to the one place that owns its
        // message. Checking for an engine first would answer a different question (exit 3, "no engine") for
        // a request that is refused outright (exit 2) whether the engine is there or not.
        if (command == WorkerCommands.Script && provider == SqliteProvider)
            return await Respond(tooling, ScriptRequest(request, provider, [], Unused()), cancellationToken);

        // `apply`, `validate` and `post-migrate` open the database directly and write no manifest, so none
        // of them needs the provider engine's package facts `ResolveEngine` exists to establish (FR-053) —
        // the host's own tooling entry point already refuses a provider engine it cannot bind (D4), the same
        // way it does for every other command.
        if (WorkerCommands.OpensDatabase(command))
        {
            return await Respond(
                tooling,
                new
                {
                    version = 1,
                    command,
                    provider,
                    schema = request.Schema,
                    selection = Selection(request.Selection),
                    shells = Shells(request),
                    connection = ResolveConnection(request)
                },
                cancellationToken);
        }

        var engine = ResolveEngine(tooling, provider, deps, packages, request);
        if (command == WorkerCommands.Plan)
        {
            return await Respond(
                tooling,
                new { version = 1, command, provider, schema = request.Schema, selection = Selection(request.Selection), shells = Shells(request) },
                cancellationToken);
        }

        var (listed, listing) = await ListModulesAsync(tooling, request, cancellationToken);
        if (listing is not null)
            return listing;

        return await Respond(tooling, ScriptRequest(request, provider, ModulePackages(listed, deps, packages), engine), cancellationToken);
    }

    private static string Validate(WorkerRequest request)
    {
        if (request.Version != WorkerContract.Version)
        {
            throw WorkerRefusal.Usage(
                "unsupported-request-version",
                $"This worker speaks request version {WorkerContract.Version}, and the request declares {request.Version}.");
        }

        if (request.Command is null || !WorkerCommands.All.Contains(request.Command, StringComparer.Ordinal))
            throw WorkerRefusal.Usage("unknown-command", $"Unknown command '{request.Command}'.");

        var missing = new[]
            {
                ("hostDirectory", request.HostDirectory),
                ("hostName", request.HostName),
                ("depsFile", request.DepsFile),
                ("environment", request.Environment)
            }
            .Where(field => string.IsNullOrWhiteSpace(field.Item2))
            .Select(field => $"'{field.Item1}' is required.")
            .ToArray();
        if (missing.Length > 0)
            throw WorkerRefusal.Usage("invalid-request", "The worker request is not valid.", missing);

        return request.Command;
    }

    /// <summary>
    /// Establishes the provider engine's package facts, refusing before anything is written when the host
    /// pins no such engine (FR-009). The message says where the tool looked, in the order it looked, and
    /// ends by stating that nothing else was substituted — the one thing an operator must be able to rule
    /// out when a deployment's SQL is for the wrong dialect.
    /// </summary>
    private static PackageFacts ResolveEngine(
        ToolingEntryPoint tooling,
        string provider,
        HostDepsFile deps,
        NuplanePackageSet packages,
        WorkerRequest request)
    {
        var packageId = tooling.ProviderPackageId(provider);
        if (deps.ForPackage(packageId) is { } pinned)
            return pinned;
        if (packages.ForPackage(packageId) is { } resolved)
            return resolved;

        var depsFile = Path.GetFileName(request.DepsFile!);
        NeverReconciled(provider, packageId, depsFile, tooling, deps, packages, request);
        var hasPackageSet = packages.Count > 0;
        var packageSet = hasPackageSet ? $"the resolved package set ({packages.Count} packages)" : "any resolved package set";
        var binding = tooling.DescribeBindingFailure(provider) ?? $"No package '{packageId}' is pinned by this host.";
        var details = new List<string>
        {
            $"Looked in the host's dependency file '{depsFile}' for package '{packageId}'.",
            hasPackageSet
                ? $"Then looked in {packageSet}."
                : "Then looked for a package set: no --packages root was given and the host records no active one."
        };
        if (hasPackageSet || deps.ForPackage("Nuplane") is not null)
        {
            details.Add(
                "A host whose modules arrive as packages declares its provider engine nowhere: it must be named by hand " +
                "in the host's package closure. See docs/foundation-host-feeds.md.");
        }

        throw WorkerRefusal.Resolution(
            "provider-engine-unavailable",
            $"The {provider} provider engine is in neither the host's dependency file ('{depsFile}') nor {packageSet}. " +
            $"The engine could not be bound: {binding} No other provider was tried.",
            details);
    }

    /// <summary>
    /// The one shape of "nothing resolved" that is not a missing engine at all: a host whose modules and
    /// engine arrive as packages, which has never reconciled, so there is no package set to look in. Before
    /// <c>--restore</c> existed this surfaced as <c>provider-engine-unavailable</c>, naming an engine the
    /// operator had pinned correctly and never naming the state file that is actually absent.
    /// </summary>
    /// <remarks>
    /// Deliberately not raised where the set is loaded: an empty set is also the correct answer for a host
    /// that carries every module in its own deps file (FR-007), and for <c>list</c> against a Nuplane host
    /// that has nothing installed yet — which reports zero modules rather than refusing. It is only once a
    /// command needs something out of that set, and the set is absent rather than merely empty, that the
    /// never-reconciled host is the thing to say.
    /// </remarks>
    private static void NeverReconciled(
        string provider,
        string packageId,
        string depsFile,
        ToolingEntryPoint tooling,
        HostDepsFile deps,
        NuplanePackageSet packages,
        WorkerRequest request)
    {
        var stateFile = NuplaneInstallRoot.DefaultStateFile(request.HostDirectory!);
        if (packages.Count > 0 || request.PackageRoots.Count > 0 || deps.ForPackage("Nuplane") is null || File.Exists(stateFile))
            return;

        throw WorkerRefusal.Resolution(
            "packages-never-reconciled",
            $"This host resolves its modules and its provider engine from a Nuplane package set, and it has never " +
            $"recorded one: '{stateFile}' does not exist, so nothing is installed for the {provider} provider engine " +
            "to be found in. No other provider was tried.",
            [
                $"Looked in the host's dependency file '{depsFile}' for package '{packageId}', which does not pin it.",
                $"Then looked for this host's active package set: no --packages root was given and '{Path.GetFileName(stateFile)}' is not there.",
                tooling.DescribeBindingFailure(provider) ?? $"No package '{packageId}' is pinned by this host.",
                "Start the host once so it reconciles, or re-run this command with --restore, which populates that " +
                "state file from the host's own Nuplane configuration. Without --restore no command downloads anything.",
                "A host whose modules arrive as packages declares its provider engine nowhere: it must be named by hand " +
                "in the host's package closure. See docs/foundation-host-feeds.md."
            ]);
    }

    /// <summary>
    /// Asks the host's own tooling entry point which modules this selection resolves to, and in which
    /// assembly each one lives. That mapping is what turns a selection into the package facts the manifest
    /// records, and asking for it through the same discovery <c>script</c> will run means the two can never
    /// disagree about which assembly a module came from.
    /// </summary>
    private static async Task<(IReadOnlyList<(string Module, string Assembly)> Modules, WorkerResponse? Refusal)> ListModulesAsync(
        ToolingEntryPoint tooling,
        WorkerRequest request,
        CancellationToken cancellationToken)
    {
        var (exitCode, response) = await tooling.InvokeAsync(
            new { version = 1, command = WorkerCommands.List, selection = Selection(request.Selection), shells = Shells(request) },
            cancellationToken);
        if (exitCode != ToolExitCode.Success)
            return ([], new() { ExitCode = exitCode, Tooling = response });

        var modules = response.GetProperty("list").GetProperty("modules").EnumerateArray()
            .Select(module => (Module: module.GetProperty("module").GetString()!, Assembly: module.GetProperty("assembly").GetString()!))
            .ToArray();
        return (modules, null);
    }

    /// <summary>
    /// One <c>packages</c> entry per selected module's assembly, each stating where its id and version were
    /// read from. A module whose assembly neither source describes is refused rather than recorded as
    /// unknown: a manifest that names no version for a module tells a DBA nothing about what they applied.
    /// </summary>
    private static object[] ModulePackages(
        IReadOnlyList<(string Module, string Assembly)> modules,
        HostDepsFile deps,
        NuplanePackageSet packages)
    {
        var resolved = modules
            .DistinctBy(module => module.Assembly, StringComparer.OrdinalIgnoreCase)
            .Select(module => (module.Module, module.Assembly, Facts: deps.ForAssembly(module.Assembly) ?? packages.ForAssembly(module.Assembly)))
            .ToArray();

        var missing = resolved
            .Where(entry => entry.Facts is null)
            .Select(entry => $"'{entry.Module}' is in assembly '{entry.Assembly}', which the host's dependency file does not list and no resolved package installs.")
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (missing.Length > 0)
        {
            throw WorkerRefusal.Resolution(
                "package-metadata-missing",
                "The manifest records where every module's package id and version came from, and this host states neither for these modules.",
                [.. missing, .. packages.Failures.Select(failure => $"Package '{failure.Key}' could not be loaded: {failure.Value}").Order(StringComparer.Ordinal)]);
        }

        return
        [
            .. resolved.Select(entry => (object)new
            {
                assembly = entry.Assembly,
                id = entry.Facts!.Id,
                version = entry.Facts.Version,
                source = entry.Facts.Source
            })
        ];
    }

    private static object ScriptRequest(WorkerRequest request, string provider, object[] packages, PackageFacts engine) => new
    {
        version = 1,
        command = WorkerCommands.Script,
        provider,
        schema = request.Schema,
        output = request.Output,
        selection = Selection(request.Selection),
        shells = Shells(request),
        host = new
        {
            name = request.HostName,
            // "checked" states that the per-feature comparison ran, which it does exactly when the host's
            // shells configuration was found beside it (FR-039). Claiming it for a run that found none
            // would tell a reviewer a check ran that did not.
            providerAgreement = request.Shells is null ? "not-checked" : "checked",
            shell = request.Shell,
            environment = request.Environment
        },
        engine = new { package = engine.Id, version = engine.Version, source = engine.Source },
        packages
    };

    /// <summary>
    /// Engine facts for the one request that never reaches an engine: <c>script --provider Sqlite</c> is
    /// refused on the command and the provider alone, before the host's tooling validates a single fact
    /// about an engine — and refusing it needs no engine to exist, which for SQLite it may well not.
    /// </summary>
    private static PackageFacts Unused() => new("", "", PackageSource.HostDepsFile);

    private static object? Selection(WorkerSelection? selection) =>
        selection is null ? null : new { kind = selection.Kind, modules = selection.Modules };

    /// <summary>
    /// The host's enabled shell features as the tooling entry point reads them, or <c>null</c> when the
    /// front end found no shells configuration beside the host — the one case that reports
    /// <c>providerAgreement: not-checked</c>. An empty list is not that case: it means the configuration was
    /// found and enables no feature this closure maps to a module.
    /// </summary>
    private static object[]? Shells(WorkerRequest request) =>
        request.Shells is null
            ? null
            : [.. request.Shells.Select(feature => new { shell = feature.Shell, feature = feature.Feature, provider = feature.Provider })];

    /// <summary>
    /// Resolves the connection the database-opening commands pass to the host's tooling entry point (D7).
    /// <see cref="WorkerRequest.Connection"/> — read by the front end from its own stdin under
    /// <c>--connection-stdin</c> — takes precedence when given; otherwise <see cref="WorkerRequest.ConnectionEnv"/>
    /// names a variable this worker process reads from its own environment, which it has by ordinary process
    /// inheritance from the front end that launched it (ADR 0076 D7) — the value itself was never placed on
    /// either process's command line, and the front end never reads it out of its own environment either.
    /// </summary>
    private static string ResolveConnection(WorkerRequest request)
    {
        if (request.Connection is { Length: > 0 } fromStdin)
            return fromStdin;

        if (request.ConnectionEnv is { Length: > 0 } name)
        {
            return System.Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
                ? value
                : throw WorkerRefusal.Usage(
                    "connection-missing",
                    $"The environment variable '{name}' named by --connection-env is not set (or is empty) in this process's environment.");
        }

        throw WorkerRefusal.Usage(
            "connection-missing",
            "'apply', 'validate' and 'post-migrate' need a connection, given with --connection-env or --connection-stdin.");
    }

    private static async Task<WorkerResponse> Respond(ToolingEntryPoint tooling, object request, CancellationToken cancellationToken)
    {
        var (exitCode, response) = await tooling.InvokeAsync(request, cancellationToken);
        return new() { ExitCode = exitCode, Tooling = response };
    }
}
