using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

namespace Elsa.Persistence.EntityFramework.Tooling;

/// <summary>
/// The one entry point the <c>dotnet elsa persistence</c> worker calls into, from inside the host's own
/// dependency closure (ADR 0076 D1, FR-003). It backs <c>list</c>, <c>plan</c>, <c>script</c>, <c>apply</c>,
/// <c>validate</c> and <c>post-migrate</c> over versioned JSON on two streams, so the worker needs no EF
/// reference of its own and cannot skew from the host's EF version.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately built from EF Core and Relational alone — <see cref="IMigrator"/>,
/// <see cref="IMigrationsAssembly"/> — because this package must keep its admitted EF package set
/// (FR-004): no <c>Microsoft.EntityFrameworkCore.Design</c>, no provider engine. The engine arrives the
/// same way it does for a running host, reflectively through
/// <see cref="EfRelationalProviderBinding"/>.
/// </para>
/// <para>
/// <c>list</c>, <c>plan</c> and <c>script</c> open no database: they configure their contexts with a
/// placeholder connection string, the same one design-time tooling uses, and generate <c>0 → head</c>
/// idempotent SQL offline. <c>apply</c>, <c>validate</c> and <c>post-migrate</c> are the three that do
/// open one, on the connection the request carries (D7) and never on one this build invents.
/// </para>
/// </remarks>
public static class EfToolingHost
{
    /// <summary>
    /// EF cannot express an idempotent script for SQLite: <c>SqliteHistoryRepository.GetEndIfScript</c>
    /// throws, because SQLite has no conditional statement to wrap a migration in (D5). Calling
    /// <see cref="IMigrator"/> directly instead of shelling out to <c>dotnet-ef</c> does not lift that, and a
    /// plain script would sit in the same directory looking identical to every idempotent file while being
    /// unsafe to re-run — so the refusal keeps the message shape <c>tools/ef/module-migrate.sh</c> already
    /// gives, naming <c>apply Sqlite</c> as the alternative.
    /// </summary>
    internal const string SqliteScriptRefusal =
        "script: SQLite cannot produce an idempotent script (EF throws NotSupportedException).\n" +
        "Script a server provider, and bring a SQLite database up to date with:\n" +
        "  dotnet elsa persistence apply --provider Sqlite";

    private static readonly byte[] Newline = "\n"u8.ToArray();

    /// <summary>Creates one host-owned, frozen configuration context for sequential tooling operations.</summary>
    public static EfToolingConfigurationContext CreateConfigurationContext(
        Stream request,
        CancellationToken cancellationToken) =>
        EfToolingConfigurationContext.CreateFromRequest(request, cancellationToken);

    /// <summary>Runs a closed version-2 operation against the identical frozen host context.</summary>
    public static Task<int> RunAsync(
        Stream request,
        Stream response,
        EfToolingConfigurationContext context,
        CancellationToken cancellationToken) =>
        EfToolingContextOperation.RunAsync(request, response, context, LoadedAssemblies(), cancellationToken);

    /// <summary>
    /// Runs one command, reading the request from <paramref name="request"/> to its end and writing exactly
    /// one response to <paramref name="response"/>. The returned code is the same one the response carries,
    /// so a worker can exit with it without classifying anything itself.
    /// </summary>
    /// <remarks>
    /// Modules are discovered from every assembly loaded into this process, across every
    /// <see cref="AssemblyLoadContext"/>, because a Nuplane <c>HostIntegrated</c> package graph loads into a
    /// context of its own rather than the default one. A caller that already holds the exact set it loaded
    /// should hand it over through the overload instead.
    /// </remarks>
    public static Task<int> RunAsync(Stream request, Stream response) =>
        RunAsync(request, response, LoadedAssemblies(), CancellationToken.None);

    /// <inheritdoc cref="RunAsync(Stream,Stream)"/>
    public static async Task<int> RunAsync(
        Stream request,
        Stream response,
        IEnumerable<Assembly> assemblies,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(assemblies);

        string? command = null;
        EfToolingResponse result;
        try
        {
            var parsed = await ReadAsync(request, cancellationToken);
            command = parsed.Command;
            result = await Execute(parsed, assemblies, cancellationToken);
        }
        catch (EfToolingRefusal refusal)
        {
            result = Failed(command, refusal);
        }
        // A cancelled run is not a failed one: laundering it into an exit-3 response would report the
        // operator's own Ctrl-C as a tooling fault, and would claim an artifact was refused when it was
        // simply never produced.
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception failure) when (IsNonFatal(failure))
        {
            result = Failed(command, EfToolingRefusal.Resolution("internal-error", $"{failure.GetType().Name}: {failure.Message}"));
        }

        await JsonSerializer.SerializeAsync(response, result, EfToolingContract.Json, cancellationToken);
        await response.WriteAsync(Newline, cancellationToken);
        await response.FlushAsync(cancellationToken);
        return result.ExitCode;
    }

    /// <summary>
    /// True for anything the internal-error handler should catch and report, false for CLR-fatal
    /// exceptions that must propagate instead of being laundered into a JSON response.
    /// </summary>
    internal static bool IsNonFatal(Exception failure) => failure is not (
        OutOfMemoryException or
        StackOverflowException or
        AccessViolationException or
        AppDomainUnloadedException or
        BadImageFormatException or
        CannotUnloadAppDomainException or
        ThreadAbortException);

    private static IEnumerable<Assembly> LoadedAssemblies() =>
        AssemblyLoadContext.All.SelectMany(context => context.Assemblies).Distinct();

    private static async Task<EfToolingRequest> ReadAsync(Stream request, CancellationToken cancellationToken)
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<EfToolingRequest>(request, EfToolingContract.Json, cancellationToken)
                   ?? throw EfToolingRefusal.Usage("invalid-request", "The request is empty.");
        }
        catch (JsonException failure)
        {
            throw EfToolingRefusal.Usage("invalid-request", $"The request is not valid tooling JSON: {failure.Message}");
        }
    }

    private static EfToolingResponse Failed(string? command, EfToolingRefusal refusal) => new()
    {
        Status = "error",
        ExitCode = refusal.ExitCode,
        Command = command,
        Error = new() { Code = refusal.Code, Message = refusal.Message, Details = refusal.Details }
    };

    private static async Task<EfToolingResponse> Execute(EfToolingRequest request, IEnumerable<Assembly> assemblies, CancellationToken cancellationToken)
    {
        var command = ValidateEnvelope(request);
        ValidateFields(command, request);

        // The SQLite refusal depends on the command and the provider alone, so it lands before discovery
        // and long before the output directory is touched.
        var provider = command == EfToolingCommands.List ? null : Canonical(request.Provider!);

        // Needs no discovery either — it compares two host facts — so it is decided here and reported in
        // two places: on its own when the SQLite rule below would otherwise answer a different question,
        // and beside the per-feature offenders for every other run.
        var capability = provider is null ? null : EfProviderAgreement.CheckCapabilitySelection(request.CapabilitySelection, provider);

        if (command == EfToolingCommands.Script && provider == "Sqlite")
        {
            // A host whose closure selects another engine was never a SQLite host, so "SQLite cannot be
            // scripted idempotently" would answer a question the operator should not have asked. The
            // disagreement is the actual mistake and outranks the idempotency rule — and only for this
            // offender source, because it alone is knowable before anything is discovered.
            if (capability is not null)
                throw ProviderDisagreement("Sqlite", [capability.ToString()]);
            throw EfToolingRefusal.Usage("sqlite-script-refused", SqliteScriptRefusal);
        }

        // Materialized once: the closure is now read twice — for module declarations and for feature
        // classes — and a caller that handed over a one-shot sequence would otherwise have the second read
        // find nothing, which would look exactly like a host that declares no feature at all.
        var closure = assemblies as IReadOnlyCollection<Assembly> ?? [.. assemblies];

        // The feature-to-module map is read only when the host's shell configuration was actually found:
        // without it there is neither a `from-host` selection to resolve nor an agreement to check, and
        // scanning a host's closure for feature classes would cost for no possible result.
        var features = request.Shells is null ? [] : EfProviderAgreement.Discover(closure);
        var ordered = EfModuleOrder.Sort(Selected(request.Selection, Discover(closure), request.Shells, features));

        if (command == EfToolingCommands.List)
            return ListModules(ordered);

        // Every input is validated before the first byte of output is written (FR-032), and before any
        // context is built, so a refusal names every offender rather than the module that happened to fail.
        CheckProviderAgreement(request.Shells, features, ordered, provider!, capability);
        var schema = NormalizeSchema(provider!, request.Schema);
        ValidateProviderSupport(ordered, provider!);
        ValidateEngine(provider!);
        var actions = PostMigrationActions(ordered);

        return command switch
        {
            EfToolingCommands.Plan => Plan(ordered, provider!, schema, actions, cancellationToken),
            EfToolingCommands.Script => Script(ordered, provider!, schema, actions, request, cancellationToken),
            EfToolingCommands.Apply => await Apply(ordered, provider!, schema, actions, request.Connection!, cancellationToken),
            EfToolingCommands.Validate => await Validate(ordered, provider!, schema, actions, request.Connection!, cancellationToken),
            EfToolingCommands.PostMigrate => await PostMigrate(ordered, provider!, schema, actions, request.Connection!, cancellationToken),
            _ => throw new InvalidOperationException($"Unreachable: '{command}' passed envelope validation without a handler.")
        };
    }

    private static string ValidateEnvelope(EfToolingRequest request)
    {
        if (request.Version != EfToolingContract.Version)
        {
            throw EfToolingRefusal.Usage(
                "unsupported-request-version",
                $"This build speaks request version {EfToolingContract.Version}, and the request declares " +
                $"{request.Version?.ToString() ?? "none"}.");
        }

        if (string.IsNullOrWhiteSpace(request.Command) || !EfToolingCommands.All.Contains(request.Command, StringComparer.Ordinal))
        {
            throw EfToolingRefusal.Usage(
                "unknown-command",
                $"Unknown command '{request.Command}'. Expected {string.Join(", ", EfToolingCommands.All)}.");
        }

        return request.Command;
    }

    /// <summary>
    /// Each command accepts exactly the fields it uses. A field a command has no use for is a refusal, not
    /// something to ignore: <c>list</c> silently accepting a <c>provider</c> would let an operator believe a
    /// provider was considered when nothing about it was.
    /// </summary>
    private static void ValidateFields(string command, EfToolingRequest request)
    {
        var list = command == EfToolingCommands.List;
        var script = command == EfToolingCommands.Script;
        var opensDatabase = command is EfToolingCommands.Apply or EfToolingCommands.Validate or EfToolingCommands.PostMigrate;
        (string Name, bool Present, bool Allowed, bool Required)[] fields =
        [
            ("selection", request.Selection is not null, true, !list),
            ("provider", request.Provider is not null, !list, !list),
            ("schema", request.Schema is not null, !list, false),
            ("output", request.Output is not null, script, script),
            ("host", request.Host is not null, script, script),
            ("engine", request.Engine is not null, script, script),
            ("packages", request.Packages is not null, script, script),
            ("shells", request.Shells is not null, true, request.Selection?.Kind == EfToolingSelection.FromHostKind),
            // Optional everywhere it is accepted: absent means the host sets no such key, which is not the
            // same answer as "the selection agrees" and must not be required into looking like one.
            ("capabilitySelection", request.CapabilitySelection is not null, !list, false),
            ("connection", request.Connection is not null, opensDatabase, opensDatabase)
        ];

        var offenders = fields
            .Where(field => field.Present ? !field.Allowed : field.Required)
            .Select(field => field.Present
                ? $"'{field.Name}' is not accepted by '{command}'."
                : $"'{field.Name}' is required by '{command}'.")
            .ToArray();
        if (offenders.Length > 0)
            throw EfToolingRefusal.Usage("invalid-request", $"The '{command}' request is not valid.", offenders);
    }

    /// <summary>The canonical spelling every response, message and manifest uses for a provider the operator may have aliased.</summary>
    private static string Canonical(string provider)
    {
        try
        {
            return EfRelationalProviderBinding.Select(provider, "relational", "Sqlite", "SqlServer", "PostgreSql", "MySql");
        }
        catch (ArgumentException failure)
        {
            throw EfToolingRefusal.Usage("unknown-provider", failure.Message);
        }
    }

    private static string? NormalizeSchema(string provider, string? schema)
    {
        try
        {
            return EfSchema.Normalize("Elsa", provider, schema);
        }
        catch (InvalidOperationException failure)
        {
            throw EfToolingRefusal.Usage("invalid-schema", failure.Message);
        }
    }

    private static IReadOnlyList<EfModuleDescriptor> Discover(IEnumerable<Assembly> assemblies)
    {
        try
        {
            return EfModuleCatalog.Discover(assemblies.Where(assembly => !assembly.IsDynamic));
        }
        catch (Exception failure) when (failure is InvalidOperationException or ReflectionTypeLoadException or TypeLoadException or FileNotFoundException)
        {
            throw EfToolingRefusal.Resolution("module-discovery-failed", failure.Message);
        }
    }

    private static IReadOnlyList<EfModuleDescriptor> Selected(
        EfToolingSelection? selection,
        IReadOnlyList<EfModuleDescriptor> discovered,
        IReadOnlyList<EfToolingShellFeature>? shells,
        IReadOnlyList<EfFeatureModuleUsage> features)
    {
        if (selection is null || selection.Kind == EfToolingSelection.AllKind)
        {
            if (selection?.Modules is not null)
                throw EfToolingRefusal.Usage("invalid-request", "'selection.modules' is not accepted with selection kind 'all'.");
            return discovered;
        }

        if (selection.Kind == EfToolingSelection.FromHostKind)
        {
            if (selection.Modules is not null)
                throw EfToolingRefusal.Usage("invalid-request", $"'selection.modules' is not accepted with selection kind '{EfToolingSelection.FromHostKind}'.");
            return Resolve(FromHost(shells!, features), discovered);
        }

        if (selection.Kind != EfToolingSelection.ModulesKind)
        {
            throw EfToolingRefusal.Usage(
                "invalid-request",
                $"Unknown selection kind '{selection.Kind}'. Expected '{EfToolingSelection.AllKind}', " +
                $"'{EfToolingSelection.ModulesKind}' or '{EfToolingSelection.FromHostKind}'.");
        }

        var names = selection.Modules;
        if (names is null || names.Count == 0 || names.Any(string.IsNullOrWhiteSpace))
            throw EfToolingRefusal.Usage("invalid-request", "Selection kind 'modules' needs a non-empty 'selection.modules' of non-blank names.");

        var duplicates = names
            .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => $"'{group.Key}' is selected {group.Count()} times.")
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (duplicates.Length > 0)
            throw EfToolingRefusal.Usage("invalid-request", "A module is selected more than once.", duplicates);

        return Resolve(names, discovered);
    }

    private static IReadOnlyList<EfModuleDescriptor> Resolve(IReadOnlyList<string> names, IReadOnlyList<EfModuleDescriptor> discovered)
    {
        var resolved = names.Select(name => (Name: name, Descriptor: EfModuleCatalog.Find(discovered, name))).ToArray();
        var unknown = resolved
            .Where(candidate => candidate.Descriptor is null)
            .Select(candidate => $"No module named '{candidate.Name}' is declared in this host's closure.")
            .ToArray();
        if (unknown.Length > 0)
            throw EfToolingRefusal.Resolution("unknown-module", "A selected module does not resolve.", unknown);

        return [.. resolved.Select(candidate => candidate.Descriptor!)];
    }

    /// <summary>
    /// The modules the host's enabled features map to through <see cref="UsesEfModuleAttribute"/>. An empty
    /// result is refused rather than treated as "nothing to do": a run that selected the host's own features
    /// and found none is a run whose artifact would silently describe nothing.
    /// </summary>
    private static IReadOnlyList<string> FromHost(IReadOnlyList<EfToolingShellFeature> shells, IReadOnlyList<EfFeatureModuleUsage> features)
    {
        var enabled = shells
            .Select(entry => entry.Feature)
            .Where(feature => !string.IsNullOrWhiteSpace(feature))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var modules = features
            .Where(usage => enabled.Contains(usage.Feature))
            .SelectMany(usage => usage.Modules)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (modules.Length == 0)
        {
            throw EfToolingRefusal.Resolution(
                "from-host-selected-nothing",
                $"'{EfToolingSelection.FromHostKind}' selected no module: none of the {shells.Count} feature(s) this host's " +
                "shell configuration enables carries a [UsesEfModule] mapping to a module in this host's closure.");
        }

        return modules;
    }

    /// <summary>
    /// The provider-agreement check (FR-035–FR-037, spec 172 FR-004, D4), over both offender sources at
    /// once. The per-feature half runs whenever the host's shell configuration was found, under any
    /// selector, and lists <i>every</i> offender: one feature of a module agreeing never speaks for another
    /// feature of the same module. The capability half runs whether or not that configuration was found —
    /// it is a fact about the host's package closure, not about its shells — so a host with no
    /// <c>shells.json</c> is still refused when its closure selects a different engine.
    /// </summary>
    private static void CheckProviderAgreement(
        IReadOnlyList<EfToolingShellFeature>? shells,
        IReadOnlyList<EfFeatureModuleUsage> features,
        IReadOnlyList<EfModuleDescriptor> modules,
        string provider,
        EfCapabilitySelectionDisagreement? capability)
    {
        var offenders = shells is null
            ? []
            : EfProviderAgreement.Check(
                shells.Select(entry => (Shell: entry.Shell ?? "", Feature: entry.Feature ?? "", entry.Provider)),
                features,
                modules.Select(descriptor => descriptor.Name),
                provider);
        if (offenders.Count == 0 && capability is null)
            return;

        throw ProviderDisagreement(
            provider,
            [.. offenders.Select(offender => offender.ToString()), .. capability is null ? [] : new[] { capability.ToString() }]);
    }

    /// <summary>
    /// The one refusal both offender sources produce, so a run refused for either reason carries the same
    /// code and the same exit code (D4).
    /// </summary>
    private static EfToolingRefusal ProviderDisagreement(string provider, IReadOnlyList<string> offenders) =>
        EfToolingRefusal.Resolution(
            "provider-disagreement",
            $"--provider is authoritative and {offenders.Count} of this host's own provider decisions disagree with " +
            $"{provider} — an enabled feature of a selected module, or the engine its package closure selects. " +
            "No other provider was tried, and nothing was written.",
            offenders);

    private static void ValidateProviderSupport(IReadOnlyList<EfModuleDescriptor> modules, string provider)
    {
        var offenders = modules
            .Where(descriptor => descriptor.ProviderContext(provider) is null)
            .Select(descriptor => $"'{descriptor.Name}' declares no {provider} context.")
            .ToArray();
        if (offenders.Length > 0)
            throw EfToolingRefusal.Resolution("provider-unsupported-for-module", $"A selected module does not support {provider}.", offenders);
    }

    private static void ValidateEngine(string provider)
    {
        if (EfRelationalProviderBinding.DescribeBindingFailure(provider) is not { } failure)
            return;
        throw EfToolingRefusal.Resolution(
            "provider-engine-unavailable",
            $"The {provider} provider engine could not be bound: {failure} No other provider was tried.");
    }

    private static EfToolingResponse ListModules(IReadOnlyList<EfModuleDescriptor> modules) => new()
    {
        ExitCode = EfToolingExitCode.Success,
        Command = EfToolingCommands.List,
        List = new()
        {
            Modules =
            [
                .. modules.Select(descriptor => new EfToolingModuleListing
                {
                    Module = descriptor.Name,
                    Assembly = descriptor.Assembly.GetName().Name!,
                    Context = descriptor.ContextType.Name,
                    HistoryTable = descriptor.HistoryTableName,
                    DependsOn = EfModuleOrder.Dependencies(descriptor),
                    Providers =
                    [
                        .. new[] { "Sqlite", "SqlServer", "PostgreSql", "MySql" }
                            .Where(provider => descriptor.ProviderContext(provider) is not null)
                            .Order(StringComparer.Ordinal)
                    ]
                })
            ]
        }
    };

    private static EfToolingResponse Plan(
        IReadOnlyList<EfModuleDescriptor> modules,
        string provider,
        string? schema,
        IReadOnlyDictionary<string, IReadOnlyList<IEfPostMigrationAction>> actions,
        CancellationToken cancellationToken)
    {
        var entries = modules
            .Select((descriptor, index) => Build(descriptor, index + 1, provider, schema, actions, generate: false, cancellationToken).Entry)
            .ToArray();

        return new()
        {
            ExitCode = EfToolingExitCode.Success,
            Command = EfToolingCommands.Plan,
            Plan = new()
            {
                Provider = provider,
                Schema = schema,
                Modules =
                [
                    .. entries.Select(entry => new EfToolingPlanEntry
                    {
                        Order = entry.Order,
                        Module = entry.Module,
                        Assembly = entry.Assembly,
                        Context = entry.Context,
                        HistoryTable = entry.HistoryTable,
                        To = entry.To,
                        Count = entry.MigrationIds.Count,
                        Ids = entry.MigrationIds,
                        DependsOn = entry.DependsOn
                    })
                ]
            }
        };
    }

    private static EfToolingResponse Script(
        IReadOnlyList<EfModuleDescriptor> modules,
        string provider,
        string? schema,
        IReadOnlyDictionary<string, IReadOnlyList<IEfPostMigrationAction>> actions,
        EfToolingRequest request,
        CancellationToken cancellationToken)
    {
        var engine = ValidateEngineFacts(request.Engine!, provider);
        var host = ValidateHostFacts(request.Host!);
        var packages = ValidatePackageFacts(request.Packages!);
        var output = request.Output!;
        if (string.IsNullOrWhiteSpace(output))
            throw EfToolingRefusal.Usage("invalid-request", "'output' must name a directory.");

        var missing = modules
            .Where(descriptor => !packages.ContainsKey(descriptor.Assembly.GetName().Name!))
            .Select(descriptor => $"'{descriptor.Name}' is in assembly '{descriptor.Assembly.GetName().Name}', which no 'packages' entry describes.")
            .ToArray();
        if (missing.Length > 0)
        {
            throw EfToolingRefusal.Resolution(
                "package-metadata-missing",
                "The manifest records where every module's package id and version came from, and this build " +
                "can observe neither, so a missing entry is refused rather than recorded as unknown.",
                missing);
        }

        // Every file is built in memory first: a module that fails to script must leave no half-written
        // artifact behind for a deployment job to pick up.
        var artifacts = modules
            .Select((descriptor, index) =>
            {
                var order = index + 1;
                var (entry, script) = Build(descriptor, order, provider, schema, actions, generate: true, cancellationToken);
                return new EfModuleArtifact(
                    entry,
                    EfMigrationPlan.ScriptFileName(order, descriptor.Name),
                    // The MySQL rewrite (#1914) sits inside the normalizer's argument on purpose: everything
                    // downstream — the per-file sha256, script-check's byte comparison, FR-043's determinism —
                    // reads the artifact, so putting it anywhere later would leave all three describing text
                    // no server could run. It is a no-op for every other provider.
                    EfToolingLineEndings.Utf8Lf(EfMySqlIdempotentScript.Rewrite(provider, descriptor.Name, script!)),
                    packages[entry.Assembly]);
            })
            .ToArray();

        var manifest = EfMigrationPlan.Render(new(provider, engine, EfCoreVersion(), schema, host), artifacts);
        EfMigrationPlan.Write(output, artifacts, manifest);

        return new()
        {
            ExitCode = EfToolingExitCode.Success,
            Command = EfToolingCommands.Script,
            Script = new()
            {
                Manifest = EfMigrationPlan.FileName,
                ManifestSha256 = EfMigrationPlan.Sha256(manifest),
                Files =
                [
                    .. artifacts.Select(artifact => new EfToolingScriptFile
                    {
                        Order = artifact.Entry.Order,
                        Module = artifact.Entry.Module,
                        File = artifact.File,
                        Sha256 = artifact.Sha256
                    })
                ]
            }
        };
    }

    /// <summary>
    /// Every selected module's declared <see cref="IEfPostMigrationAction"/>s, instantiated once for the whole
    /// command (ADR 0076 D8). A declaration this build cannot honour is refused for the whole selection,
    /// naming every offender: silently skipping one would leave a real obligation unaudited while <c>apply</c>
    /// and <c>validate</c> still exited 0 and <c>migration-plan.json</c> still recorded <c>postMigration: []</c>,
    /// telling a DBA there was nothing left to run.
    /// </summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<IEfPostMigrationAction>> PostMigrationActions(
        IReadOnlyList<EfModuleDescriptor> modules)
    {
        var resolved = new Dictionary<string, IReadOnlyList<IEfPostMigrationAction>>(StringComparer.OrdinalIgnoreCase);
        var offenders = new List<string>();
        foreach (var descriptor in modules)
        {
            EfPostMigrationActions.TryCreate(descriptor.Name, descriptor.PostMigration, out var actions, out var faults);
            resolved[descriptor.Name] = actions;
            offenders.AddRange(faults);
        }

        if (offenders.Count > 0)
        {
            throw EfToolingRefusal.Resolution(
                "post-migration-invalid",
                "A selected module declares a post-migration action this build cannot use.",
                [.. offenders.Order(StringComparer.Ordinal)]);
        }

        return resolved;
    }

    private static EfToolingEngineFacts ValidateEngineFacts(EfToolingEngineFacts engine, string provider)
    {
        var offenders = new List<string>();
        if (string.IsNullOrWhiteSpace(engine.Package))
            offenders.Add("'engine.package' is required.");
        if (string.IsNullOrWhiteSpace(engine.Version))
            offenders.Add("'engine.version' is required.");
        if (engine.Source is null || !EfToolingPackageSource.All.Contains(engine.Source, StringComparer.Ordinal))
            offenders.Add($"'engine.source' must be one of {string.Join(", ", EfToolingPackageSource.All)}.");
        if (offenders.Count > 0)
            throw EfToolingRefusal.Usage("invalid-request", "The 'engine' block is not valid.", offenders);

        var expected = EfRelationalProviderBinding.ProviderPackageId(provider);
        if (!string.Equals(engine.Package, expected, StringComparison.Ordinal))
        {
            throw EfToolingRefusal.Resolution(
                "engine-package-mismatch",
                $"The manifest would name engine package '{engine.Package}' while {provider} binds '{expected}'.");
        }

        return engine;
    }

    private static EfToolingHostFacts ValidateHostFacts(EfToolingHostFacts host)
    {
        var agreement = host.ProviderAgreement ?? EfToolingProviderAgreement.NotChecked;
        var offenders = new List<string>();
        if (string.IsNullOrWhiteSpace(host.Name))
            offenders.Add("'host.name' is required.");
        if (string.IsNullOrWhiteSpace(host.Environment))
            offenders.Add("'host.environment' is required: the CLI owns the --environment default, so it is stated rather than defaulted twice.");
        if (!EfToolingProviderAgreement.All.Contains(agreement, StringComparer.Ordinal))
            offenders.Add($"'host.providerAgreement' must be one of {string.Join(", ", EfToolingProviderAgreement.All)}.");
        if (offenders.Count > 0)
            throw EfToolingRefusal.Usage("invalid-request", "The 'host' block is not valid.", offenders);

        return new() { Name = host.Name, ProviderAgreement = agreement, Shell = host.Shell, Environment = host.Environment };
    }

    private static IReadOnlyDictionary<string, EfToolingPackageFacts> ValidatePackageFacts(IReadOnlyList<EfToolingPackageFacts> packages)
    {
        if (packages.Any(package => package is null))
            throw EfToolingRefusal.Usage("invalid-request", "'packages' must not contain a null entry.");

        var offenders = packages
            .SelectMany(Faults)
            .Concat(packages
                .Where(package => !string.IsNullOrWhiteSpace(package.Assembly))
                .GroupBy(package => package.Assembly!, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => $"'packages' describes assembly '{group.Key}' {group.Count()} times."))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (offenders.Length > 0)
            throw EfToolingRefusal.Usage("invalid-request", "The 'packages' block is not valid.", offenders);

        return packages.ToDictionary(package => package.Assembly!, package => package, StringComparer.OrdinalIgnoreCase);

        static IEnumerable<string> Faults(EfToolingPackageFacts package, int index)
        {
            if (string.IsNullOrWhiteSpace(package.Assembly))
                yield return $"'packages[{index}].assembly' is required.";
            if (string.IsNullOrWhiteSpace(package.Id))
                yield return $"'packages[{index}].id' is required.";
            if (string.IsNullOrWhiteSpace(package.Version))
                yield return $"'packages[{index}].version' is required.";
            if (package.Source is null || !EfToolingPackageSource.All.Contains(package.Source, StringComparer.Ordinal))
                yield return $"'packages[{index}].source' must be one of {string.Join(", ", EfToolingPackageSource.All)}.";
        }
    }

    private static (EfModulePlanEntry Entry, string? Script) Build(
        EfModuleDescriptor descriptor,
        int order,
        string provider,
        string? schema,
        IReadOnlyDictionary<string, IReadOnlyList<IEfPostMigrationAction>> actions,
        bool generate,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var contextType = descriptor.RequireProviderContext(provider);
        try
        {
            using var context = CreateContext(descriptor, contextType, provider, PlaceholderConnection(provider), schema);
            var ids = context.GetService<IMigrationsAssembly>().Migrations.Keys.ToArray();
            var script = generate
                // Script and Idempotent together, exactly as `dotnet ef migrations script --idempotent`
                // composes them: Idempotent alone drops the batch separators each engine's own client needs.
                ? context.GetService<IMigrator>().GenerateScript(null, null, MigrationsSqlGenerationOptions.Script | MigrationsSqlGenerationOptions.Idempotent)
                : null;
            return (new EfModulePlanEntry(order, descriptor, contextType.Name, ids, actions[descriptor.Name]), script);
        }
        catch (Exception failure) when (failure is not EfToolingRefusal and not OperationCanceledException)
        {
            throw EfToolingRefusal.Resolution(
                "module-generation-failed",
                $"'{descriptor.Name}' could not be read for {provider} from {contextType.Name}: {failure.Message}");
        }
    }

    /// <summary>
    /// <c>apply</c> runs each selected module's compiled migrations against <paramref name="connection"/>
    /// through <see cref="EfDatabaseMigrator.ApplyAsync"/> — the same path a running host's
    /// <c>EfModuleMigrator&lt;T&gt;</c> uses — one module at a time, in dependency order, stopping at the
    /// first one that fails: a module after it may depend on the one that just failed to apply. This never
    /// reads or writes <c>migration-plan.json</c> (that is <c>script</c>'s artifact, for a DBA to review);
    /// it reads the host's own compiled migrations.
    /// </summary>
    /// <remarks>
    /// Each module is audited for outstanding post-migration actions in the same pass, against the context
    /// whose migrations just applied, but the refusal is deferred until every module has applied (ADR 0076
    /// D8): a required action is a data step, not a schema dependency, so aborting the run over one would
    /// leave the remaining modules' schemas unapplied for a reason that has nothing to do with them. Nothing
    /// here ever runs an action — exiting 0 with one outstanding is the failure this audit exists to
    /// prevent, and running it silently is the other.
    /// </remarks>
    private static async Task<EfToolingResponse> Apply(
        IReadOnlyList<EfModuleDescriptor> modules,
        string provider,
        string? schema,
        IReadOnlyDictionary<string, IReadOnlyList<IEfPostMigrationAction>> actions,
        string connection,
        CancellationToken cancellationToken)
    {
        var entries = new List<EfToolingApplyEntry>(modules.Count);
        var outstanding = new List<string>();
        for (var index = 0; index < modules.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var descriptor = modules[index];
            var order = index + 1;
            var contextType = descriptor.RequireProviderContext(provider);
            try
            {
                using var context = CreateContext(descriptor, contextType, provider, connection, schema);
                var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
                await EfDatabaseMigrator.ApplyAsync(context, EfRelationalProviderBinding.ExpectedProviderName(provider), EfMigratePolicy.AutoMigrate, cancellationToken);
                outstanding.AddRange(await RequiredActions(context, descriptor, provider, actions, cancellationToken));
                entries.Add(new()
                {
                    Order = order,
                    Module = descriptor.Name,
                    Context = contextType.Name,
                    HistoryTable = descriptor.HistoryTableName,
                    Applied = pending
                });
            }
            catch (Exception failure) when (failure is not EfToolingRefusal and not OperationCanceledException)
            {
                throw EfToolingRefusal.DatabaseFailure(
                    "module-apply-failed",
                    EfToolingRedaction.Redact($"'{descriptor.Name}' could not be applied for {provider} from {contextType.Name}: {failure.Message}", connection));
            }
        }

        RefusePostMigration(outstanding, "Migrations were applied.");

        return new()
        {
            ExitCode = EfToolingExitCode.Success,
            Command = EfToolingCommands.Apply,
            Apply = new() { Provider = provider, Schema = schema, Modules = entries }
        };
    }

    /// <summary>
    /// <c>validate</c> checks every selected module against <paramref name="connection"/> through
    /// <see cref="EfDatabaseMigrator.ApplyAsync"/> under <see cref="EfMigratePolicy.Validate"/>, which only
    /// reads <c>IHistoryRepository</c> and never migrates (FR-052). Every module is checked — not just the
    /// first offender — so the refusal names every module with a pending migration, the same way every
    /// other whole-selection refusal here does; nothing is applied whether one module is pending or all of
    /// them are.
    /// </summary>
    private static async Task<EfToolingResponse> Validate(
        IReadOnlyList<EfModuleDescriptor> modules,
        string provider,
        string? schema,
        IReadOnlyDictionary<string, IReadOnlyList<IEfPostMigrationAction>> actions,
        string connection,
        CancellationToken cancellationToken)
    {
        var entries = new List<EfToolingValidateEntry>(modules.Count);
        var offenders = new List<string>();
        var outstanding = new List<string>();
        for (var index = 0; index < modules.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var descriptor = modules[index];
            var order = index + 1;
            var contextType = descriptor.RequireProviderContext(provider);
            try
            {
                using var context = CreateContext(descriptor, contextType, provider, connection, schema);
                try
                {
                    await EfDatabaseMigrator.ApplyAsync(context, EfRelationalProviderBinding.ExpectedProviderName(provider), EfMigratePolicy.Validate, cancellationToken);
                }
                catch (EfPendingMigrationsException failure)
                {
                    // EfDatabaseMigrator's own fail-closed check (pending migrations), not a database
                    // failure: this is exactly the negative result validate exists to report. Caught by
                    // this dedicated type, not InvalidOperationException, so a genuine connectivity,
                    // credential or schema failure thrown by GetPendingMigrationsAsync before that check
                    // is reached is not misreported as a pending migration.
                    offenders.Add(EfToolingRedaction.Redact(failure.Message, connection));
                    continue;
                }

                // A host under Migrate:Policy=Validate runs this same audit at startup and refuses to start
                // when it reports something required, so reporting success here would tell an operator the
                // host is ready when it is not (SC-005).
                outstanding.AddRange(await RequiredActions(context, descriptor, provider, actions, cancellationToken));
                entries.Add(new() { Order = order, Module = descriptor.Name, Context = contextType.Name, HistoryTable = descriptor.HistoryTableName });
            }
            catch (Exception failure) when (failure is not EfToolingRefusal and not OperationCanceledException)
            {
                throw EfToolingRefusal.DatabaseFailure(
                    "module-validate-failed",
                    EfToolingRedaction.Redact($"'{descriptor.Name}' could not be validated for {provider} from {contextType.Name}: {failure.Message}", connection));
            }
        }

        if (offenders.Count > 0)
        {
            throw EfToolingRefusal.NegativeResult(
                "pending-migrations",
                "A selected module has a pending migration. Nothing was applied.",
                [.. offenders.Order(StringComparer.Ordinal)]);
        }

        RefusePostMigration(outstanding, "Every selected module's migrations are already applied.");

        return new()
        {
            ExitCode = EfToolingExitCode.Success,
            Command = EfToolingCommands.Validate,
            Validate = new() { Provider = provider, Schema = schema, Modules = entries }
        };
    }

    /// <summary>
    /// <c>post-migrate</c> is the only command that calls <see cref="IEfPostMigrationAction.RunAsync"/>
    /// (ADR 0076 D8). It audits first and runs only what the audit reports as required, so a second run over
    /// a database already repaired does nothing at all; then it audits again, and fails closed if anything
    /// is still required, rather than reporting a repair that did not take. It applies no migration: a
    /// module whose schema is not current is refused, because an action audited against a schema the module
    /// has moved past is answering a question about a database that no longer exists.
    /// </summary>
    private static async Task<EfToolingResponse> PostMigrate(
        IReadOnlyList<EfModuleDescriptor> modules,
        string provider,
        string? schema,
        IReadOnlyDictionary<string, IReadOnlyList<IEfPostMigrationAction>> actions,
        string connection,
        CancellationToken cancellationToken)
    {
        var entries = new List<EfToolingPostMigrateEntry>(modules.Count);
        var pendingMigrations = new List<string>();
        var stillRequired = new List<string>();
        for (var index = 0; index < modules.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var descriptor = modules[index];
            var order = index + 1;
            var declared = actions[descriptor.Name];
            var contextType = descriptor.RequireProviderContext(provider);
            try
            {
                using var context = CreateContext(descriptor, contextType, provider, connection, schema);
                try
                {
                    await EfDatabaseMigrator.ApplyAsync(context, EfRelationalProviderBinding.ExpectedProviderName(provider), EfMigratePolicy.Validate, cancellationToken);
                }
                catch (EfPendingMigrationsException failure)
                {
                    pendingMigrations.Add(EfToolingRedaction.Redact(failure.Message, connection));
                    continue;
                }

                var required = await EfPostMigrationActions.RequiredAsync(context, declared, cancellationToken);
                foreach (var action in required)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await action.RunAsync(context, cancellationToken);
                }

                stillRequired.AddRange(
                    (await EfPostMigrationActions.RequiredAsync(context, required, cancellationToken))
                    .Select(action => $"'{descriptor.Name}' action '{action.Id}' still reports itself required after running."));

                entries.Add(new()
                {
                    Order = order,
                    Module = descriptor.Name,
                    Context = contextType.Name,
                    Declared = [.. declared.Select(action => action.Id)],
                    Ran = [.. required.Select(action => action.Id)]
                });
            }
            catch (Exception failure) when (failure is not EfToolingRefusal and not OperationCanceledException)
            {
                throw EfToolingRefusal.DatabaseFailure(
                    "module-post-migrate-failed",
                    EfToolingRedaction.Redact($"'{descriptor.Name}' post-migration could not be run for {provider} from {contextType.Name}: {failure.Message}", connection));
            }
        }

        if (pendingMigrations.Count > 0)
        {
            throw EfToolingRefusal.NegativeResult(
                "pending-migrations",
                "A selected module has a pending migration, so its post-migration actions were not run. " +
                "Apply the migrations first: dotnet elsa persistence apply.",
                [.. pendingMigrations.Order(StringComparer.Ordinal)]);
        }

        if (stillRequired.Count > 0)
        {
            throw EfToolingRefusal.NegativeResult(
                "post-migration-incomplete",
                "A post-migration action ran and its own audit still reports it required.",
                [.. stillRequired.Order(StringComparer.Ordinal)]);
        }

        return new()
        {
            ExitCode = EfToolingExitCode.Success,
            Command = EfToolingCommands.PostMigrate,
            PostMigrate = new() { Provider = provider, Schema = schema, Modules = entries }
        };
    }

    /// <summary>
    /// One module's outstanding actions, described for a refusal. Read-only: this audits and never runs
    /// anything, which is what lets <c>apply</c> and <c>validate</c> call it.
    /// </summary>
    private static async Task<IReadOnlyList<string>> RequiredActions(
        DbContext context,
        EfModuleDescriptor descriptor,
        string provider,
        IReadOnlyDictionary<string, IReadOnlyList<IEfPostMigrationAction>> actions,
        CancellationToken cancellationToken)
    {
        var required = await EfPostMigrationActions.RequiredAsync(context, actions[descriptor.Name], cancellationToken);
        return
        [
            .. required.Select(action =>
                $"'{descriptor.Name}' requires post-migration action '{action.Id}' ({action.RequiredWhen}). " +
                $"Run: {EfPostMigrationActions.CommandFor(descriptor.Name, provider)}")
        ];
    }

    /// <summary>
    /// The one negative result (exit 1, FR-034) a required-but-unrun action produces, wherever it was
    /// audited from. Naming <c>post-migrate</c> rather than running the action keeps a bounded-batch data
    /// rewrite an operator's decision.
    /// </summary>
    private static void RefusePostMigration(IReadOnlyList<string> outstanding, string applied)
    {
        if (outstanding.Count == 0)
            return;

        throw EfToolingRefusal.NegativeResult(
            "post-migration-required",
            $"{applied} A selected module has a post-migration action that has not been run, and nothing here runs one.",
            [.. outstanding.Order(StringComparer.Ordinal)]);
    }

    /// <summary>
    /// Binds the module's own history table, migrations assembly and schema the same way a running host
    /// does, so the SQL a DBA reviews (or the database <c>apply</c>/<c>validate</c> open) records exactly
    /// what a runtime validate reads back. <c>plan</c> and <c>script</c> pass a placeholder connection —
    /// the same one design-time tooling uses — and never open it; <c>apply</c> and <c>validate</c> pass the
    /// real one and do.
    /// </summary>
    private static DbContext CreateContext(EfModuleDescriptor descriptor, Type contextType, string provider, string connection, string? schema)
    {
        var builder = (DbContextOptionsBuilder)Activator.CreateInstance(typeof(DbContextOptionsBuilder<>).MakeGenericType(contextType))!;
        EfRelationalProviderBinding.Use(
            builder,
            provider,
            connection,
            descriptor.HistoryTableName,
            descriptor.Assembly.GetName().Name,
            schema);
        return (DbContext)Activator.CreateInstance(contextType, builder.Options)!;
    }

    private static string PlaceholderConnection(string provider) => EfRelationalProviderBinding.Select(
        provider,
        "relational",
        "Data Source=elsa-design-time.db",
        "Server=localhost;Database=elsa_design_time;Trusted_Connection=True;TrustServerCertificate=True",
        "Host=localhost;Database=elsa_design_time",
        "Server=localhost;Database=elsa_design_time");

    /// <summary>
    /// The EF Core the host actually bound, read off the loaded assembly rather than taken from the
    /// request: unlike a package id and version, this is a fact about the running closure that only code
    /// inside it can state. Build metadata is dropped so the value moves only when the version does.
    /// </summary>
    private static string EfCoreVersion()
    {
        var assembly = typeof(DbContext).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(informational))
            return assembly.GetName().Version?.ToString() ?? "";
        var metadata = informational.IndexOf('+');
        return metadata < 0 ? informational : informational[..metadata];
    }
}
