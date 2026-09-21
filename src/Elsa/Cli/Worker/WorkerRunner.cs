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
        catch (Exception failure)
        {
            return new()
            {
                ExitCode = ToolExitCode.ResolutionFailure,
                Error = new() { Code = "worker-internal-error", Message = $"{failure.GetType().Name}: {failure.Message}" }
            };
        }
    }

    private static async Task<WorkerResponse> ExecuteAsync(WorkerRequest request, CancellationToken cancellationToken)
    {
        var command = Validate(request);
        var deps = HostDepsFile.Read(request.DepsFile!);
        var packages = await NuplanePackageSet.LoadAsync(request.PackageRoots, request.HostDirectory!, cancellationToken);
        foreach (var failure in packages.Failures.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            Console.Error.WriteLine($"warning: package '{failure.Key}' could not be loaded: {failure.Value}");

        HostClosure.Preload(deps);
        var tooling = ToolingEntryPoint.Resolve(
            HostClosure.LoadPersistence(),
            deps.ForAssembly(HostClosure.PersistenceAssemblyName)?.Version,
            ToolVersion);

        if (command == WorkerCommands.List)
            return await Respond(tooling, new { version = 1, command, selection = Selection(request.Selection) }, cancellationToken);

        var provider = tooling.CanonicalProvider(request.Provider ?? throw WorkerRefusal.Usage("invalid-request", $"'{command}' needs a provider."));

        // SQLite cannot be scripted idempotently, and that refusal belongs to the one place that owns its
        // message. Checking for an engine first would answer a different question (exit 3, "no engine") for
        // a request that is refused outright (exit 2) whether the engine is there or not.
        if (command == WorkerCommands.Script && provider == SqliteProvider)
            return await Respond(tooling, ScriptRequest(request, provider, [], Unused()), cancellationToken);

        var engine = ResolveEngine(tooling, provider, deps, packages, request);
        if (command == WorkerCommands.Plan)
        {
            return await Respond(
                tooling,
                new { version = 1, command, provider, schema = request.Schema, selection = Selection(request.Selection) },
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
            new { version = 1, command = WorkerCommands.List, selection = Selection(request.Selection) },
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
        host = new
        {
            name = request.HostName,
            // Slice 9 adds the per-feature comparison; until it does, claiming anything else would tell a
            // reviewer a check ran that did not.
            providerAgreement = "not-checked",
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

    private static async Task<WorkerResponse> Respond(ToolingEntryPoint tooling, object request, CancellationToken cancellationToken)
    {
        var (exitCode, response) = await tooling.InvokeAsync(request, cancellationToken);
        return new() { ExitCode = exitCode, Tooling = response };
    }
}
