using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Elsa.Persistence.Schema.SchemaFinalization;

namespace Elsa.Persistence.EntityFramework.Tooling;

/// <summary>
/// The one entry point the <c>dotnet elsa persistence</c> worker calls into, from inside the host's own
/// dependency closure (ADR 0076 D1, FR-003). It backs <c>list</c>, <c>plan</c>, <c>script</c>, <c>apply</c>,
/// <c>validate</c> and <c>post-migrate</c> over versioned JSON on two streams, so the worker needs no EF
/// reference of its own and cannot skew from the host's EF version.
/// </summary>
/// <remarks>
/// Candidate inspection has separate capabilities and envelopes. The selected host owns configuration,
/// selection reconciliation and EF preparation; the CLI owns capture and the worker owns its transport.
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

    /// <summary>Inspects a candidate from supplied file bytes using this host's loaded feature closure.</summary>
    /// <exception cref="EfToolingRefusal">The selected host closure or bounded projection is unavailable; the worker emits its fixed outer refusal.</exception>
    public static Task<int> RunCandidateInspectionAsync(
        Stream request,
        Stream response,
        CancellationToken cancellationToken) =>
        new EfCandidateInspectionOperation(LoadedAssemblies).RunAsync(request, response, cancellationToken);

    /// <summary>Inspects a captured candidate with one explicitly supplied environment document.</summary>
    /// <exception cref="EfToolingRefusal">The selected host closure or bounded projection is unavailable.</exception>
    public static Task<int> RunCandidateEnvironmentInspectionAsync(
        Stream request,
        Stream response,
        CancellationToken cancellationToken) =>
        new EfCandidateEnvironmentInspectionOperation(LoadedAssemblies).RunAsync(request, response, cancellationToken);

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
            EfToolingCommands.Apply => await Apply(ordered, provider!, schema, actions, request.Connection!, MigrateOptions(request), cancellationToken),
            EfToolingCommands.Validate => await Validate(ordered, provider!, schema, actions, request.Connection!, cancellationToken),
            EfToolingCommands.PostMigrate => await PostMigrate(ordered, provider!, schema, actions, request.Connection!, cancellationToken),
            EfToolingCommands.Hold or EfToolingCommands.Release or EfToolingCommands.Status =>
                await Finalization(command, ordered, closure, provider!, schema, request.Finalization, SkewAllowance(request), request.Connection!, cancellationToken),
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
        var finalization = EfToolingCommands.IsFinalization(command);
        var changesHolds = command is EfToolingCommands.Hold or EfToolingCommands.Release;
        var opensDatabase = command is EfToolingCommands.Apply or EfToolingCommands.Validate or EfToolingCommands.PostMigrate || finalization;
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
            ("connection", request.Connection is not null, opensDatabase, opensDatabase),
            ("skewAllowance", request.SkewAllowance is not null, command == EfToolingCommands.Status, false),
            ("sqliteMigrationLockStaleAfter", request.SqliteMigrationLockStaleAfter is not null, command == EfToolingCommands.Apply, false),
            ("finalization", request.Finalization is not null, finalization, changesHolds),
            ("finalization.family", request.Finalization?.Family is not null, finalization, changesHolds),
            ("finalization.version", request.Finalization?.Version is not null, changesHolds, false),
            ("finalization.reason", request.Finalization?.Reason is not null, command == EfToolingCommands.Hold, command == EfToolingCommands.Hold),
            ("finalization.operator", request.Finalization?.Operator is not null, changesHolds, changesHolds)
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

    internal static IReadOnlyList<EfModuleDescriptor> Discover(IEnumerable<Assembly> assemblies)
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

    internal static EfToolingResponse ListModules(IReadOnlyList<EfModuleDescriptor> modules) => new()
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

    /// <summary>Runs the existing offline planner after a context operation has selected and checked its modules.</summary>
    internal static EfToolingResponse PlanModules(
        IReadOnlyList<EfModuleDescriptor> modules,
        string provider,
        string? schema,
        CancellationToken cancellationToken)
    {
        var canonical = Canonical(provider);
        var normalizedSchema = NormalizeSchema(canonical, schema);
        ValidateProviderSupport(modules, canonical);
        ValidateEngine(canonical);
        var actions = PostMigrationActions(modules);
        return Plan(modules, canonical, normalizedSchema, actions, cancellationToken);
    }

    /// <summary>Scripts the selected context modules with host-owned identity and redacted selection facts.</summary>
    internal static EfToolingResponse ScriptModules(
        IReadOnlyList<EfModuleDescriptor> modules,
        string provider,
        string? schema,
        string output,
        EfToolingEngineFacts engine,
        IReadOnlyList<EfToolingPackageFacts> packages,
        EfToolingHostFacts host,
        EfToolingConfigurationContextFacts contextFacts,
        CancellationToken cancellationToken)
    {
        var canonical = Canonical(provider);
        if (canonical == "Sqlite")
            throw EfToolingRefusal.Usage("sqlite-script-refused", SqliteScriptRefusal);
        var normalizedSchema = NormalizeSchema(canonical, schema);
        ValidateProviderSupport(modules, canonical);
        ValidateEngine(canonical);
        var actions = PostMigrationActions(modules);
        var request = new EfToolingRequest { Output = output, Engine = engine, Packages = packages, Host = host };
        return Script(modules, canonical, normalizedSchema, actions, request, cancellationToken, contextFacts);
    }

    /// <summary>Runs the existing live module operation after the context boundary verifies every target.</summary>
    internal static async Task<EfToolingResponse> RunLiveModulesAsync(
        IReadOnlyList<EfModuleDescriptor> modules,
        string command,
        string provider,
        string? schema,
        string connection,
        Action verifyTargets,
        EfMigrateOptions migrate,
        CancellationToken cancellationToken)
    {
        var canonical = Canonical(provider);
        var normalizedSchema = NormalizeSchema(canonical, schema);
        ValidateProviderSupport(modules, canonical);
        ValidateEngine(canonical);
        verifyTargets();
        var actions = PostMigrationActions(modules);
        return command switch
        {
            EfToolingCommands.Apply => await Apply(modules, canonical, normalizedSchema, actions, connection, migrate, cancellationToken),
            EfToolingCommands.Validate => await Validate(modules, canonical, normalizedSchema, actions, connection, cancellationToken),
            EfToolingCommands.PostMigrate => await PostMigrate(modules, canonical, normalizedSchema, actions, connection, cancellationToken),
            _ => throw EfToolingRefusal.Usage("unknown-command", "The live context operation command is not supported.")
        };
    }

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
        CancellationToken cancellationToken,
        EfToolingConfigurationContextFacts? contextFacts = null)
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

        var manifest = EfMigrationPlan.Render(new(provider, engine, EfCoreVersion(), schema, host, contextFacts), artifacts);
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
    /// it reads the host's own compiled migrations. A SQLite database's migration lock is waited for as long as
    /// <paramref name="migrate"/> says (<see cref="EfMigrateOptions.SqliteMigrationLockStaleAfter"/>, read from the host's
    /// configuration), whatever policy that host runs under.
    /// </summary>
    /// <remarks>
    /// Each module is audited for outstanding post-migration actions in the same pass, against the context
    /// whose migrations just applied, but the refusal is deferred until every module has applied (ADR 0076
    /// D8): a required action is a data step, not a schema dependency, so aborting the run over one would
    /// leave the remaining modules' schemas unapplied for a reason that has nothing to do with them. Nothing
    /// here ever runs an action — exiting 0 with one outstanding is the failure this audit exists to
    /// prevent, and running it silently is the other.
    /// <para>
    /// A module whose pending batch holds a contracting migration its schema family is not yet finalized for is
    /// refused by <see cref="EfDatabaseMigrator.ApplyAsync"/> before anything of that batch runs (spec 185, FR-024),
    /// and reported as a refusal, exit code 2, naming the family and the version it waits for. On a database no host
    /// has admitted the module in, <c>apply</c> creates each contracted family's finalization record before the
    /// contracting migration runs, naming <c>migrator:&lt;machine name&gt;</c> (#2136).
    /// </para>
    /// </remarks>
    private static async Task<EfToolingResponse> Apply(
        IReadOnlyList<EfModuleDescriptor> modules,
        string provider,
        string? schema,
        IReadOnlyDictionary<string, IReadOnlyList<IEfPostMigrationAction>> actions,
        string connection,
        EfMigrateOptions migrate,
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
                await EfDatabaseMigrator.ApplyAsync(context, EfRelationalProviderBinding.ExpectedProviderName(provider), migrate.WithPolicy(EfMigratePolicy.AutoMigrate), host: null, cancellationToken);
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
            catch (SchemaFinalization.EfContractingMigrationRefusedException refusal)
            {
                // Spec 185, FR-024: a refusal, not a database failure — the database was read, and it says this module's
                // pending batch may not run yet. Nothing of it was applied, unless no host had admitted the module there,
                // when the migrations before its first contracting one were (#2136); modules before it in the order were.
                throw EfToolingRefusal.Usage(
                    "contracting-migration-refused",
                    refusal.Applied.Count == 0
                        ? $"'{descriptor.Name}' was not applied: a pending contracting migration waits for its schema family's " +
                          "version to be finalized, so none of its pending migrations was applied."
                        : $"'{descriptor.Name}' was applied only up to its first contracting migration, which waits for its " +
                          "schema family's version to be finalized, so none of its pending migrations from there on was applied.",
                    [EfToolingRedaction.Redact(refusal.Message, connection)]);
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
    /// <c>hold</c>, <c>release</c> and <c>status</c> (spec 181, FR-019, FR-020 and FR-022): each reads the finalization
    /// record of the selected modules' schema families in the database directly, so an operator can place a hold before
    /// any gate-aware host runs, which a canary requires. None finalizes, forces finalization or lowers a finalized
    /// version: a hold only ever keeps a version from finalizing, and one on a version already finalized is refused,
    /// because the rollback boundary has been crossed. A hold placed on a database whose family has no record yet
    /// creates the record first, at the oldest version this host's build reads, as the first activation would.
    /// </summary>
    /// <remarks>
    /// <c>status</c> also reads the cluster's members from the membership table in this database, through the provider the
    /// host's closure carries (<see cref="IEfToolingFleetSource"/>), so each pending version names the counted members that
    /// cannot read it (spec 181, FR-022) and the operator sees who is in the fleet without a query of their own.
    /// </remarks>
    private static async Task<EfToolingResponse> Finalization(
        string command,
        IReadOnlyList<EfModuleDescriptor> modules,
        IReadOnlyCollection<Assembly> closure,
        string provider,
        string? schema,
        EfToolingFinalizationRequest? request,
        TimeSpan? skewAllowance,
        string connection,
        CancellationToken cancellationToken)
    {
        var owned = modules
            .SelectMany(descriptor => SchemaFinalization.EfSchemaModuleFamilies.For(descriptor.Name, descriptor.Assembly).Chains
                .Select(chain => (Descriptor: descriptor, Chain: chain)))
            .Where(candidate => request?.Family is null || StringComparer.Ordinal.Equals(candidate.Chain.Family, request.Family))
            .ToArray();
        if (request?.Family is { } family && owned.Length != 1)
            throw EfToolingRefusal.Resolution(
                "unknown-family",
                owned.Length == 0
                    ? $"No selected module owns schema family '{family}'."
                    : $"Schema family '{family}' is owned by more than one selected module; select the one to act on with --modules.",
                [.. owned.Select(candidate => candidate.Descriptor.Name)]);

        var cluster = command == EfToolingCommands.Status
            ? await ReadClusterAsync(closure, provider, connection, schema, skewAllowance, cancellationToken)
            : null;
        var families = new List<EfToolingFinalizationFamily>(owned.Length);
        foreach (var (descriptor, chain) in owned)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var contextType = descriptor.RequireProviderContext(provider);
            try
            {
                await using var context = CreateContext(descriptor, contextType, provider, connection, schema);
                // The record table is created by the module's own migrations; a record read or written against a
                // schema that is not current would answer for a database the host will not run against.
                await EfDatabaseMigrator.ApplyAsync(context, EfRelationalProviderBinding.ExpectedProviderName(provider), EfMigratePolicy.Validate, cancellationToken);
                var record = command switch
                {
                    EfToolingCommands.Hold => await ChangeHoldsAsync(context, chain, request!, place: true, cancellationToken),
                    EfToolingCommands.Release => await ChangeHoldsAsync(context, chain, request!, place: false, cancellationToken),
                    _ => await new SchemaFinalization.EfSchemaFinalizationStore(context).FindAsync(chain.Family, cancellationToken)
                };
                families.Add(Describe(descriptor.Name, chain.Family, chain.Module, chain.ReadableVersions, record, cluster?.Fleet));
            }
            catch (EfPendingMigrationsException failure)
            {
                throw EfToolingRefusal.NegativeResult(
                    "pending-migrations",
                    $"'{descriptor.Name}' has pending migrations, so its finalization record cannot be read or written. Apply them first.",
                    [EfToolingRedaction.Redact(failure.Message, connection)]);
            }
            catch (SchemaFinalizationRefusedException refusal)
            {
                throw EfToolingRefusal.Usage(RefusalCode(refusal.Refusal), refusal.Message);
            }
            catch (Exception failure) when (failure is not EfToolingRefusal and not OperationCanceledException)
            {
                throw EfToolingRefusal.DatabaseFailure(
                    "finalization-record-failed",
                    EfToolingRedaction.Redact($"The finalization record of '{chain.Family}' in '{descriptor.Name}' could not be read or written for {provider}: {failure.Message}", connection));
            }
        }

        return new()
        {
            ExitCode = EfToolingExitCode.Success,
            Command = command,
            Finalization = new() { Provider = provider, Schema = schema, Families = families, Cluster = cluster?.Describe(families) }
        };
    }

    /// <summary>
    /// The cluster's members from the membership table the connection reaches. Every way of not reading them is a marker on
    /// the payload rather than an absence: a closure with no provider that keeps them in an EF module, a module with no
    /// context for the engine, a table this database does not have and one that cannot be read each say so, because none is
    /// the same as a cluster with nobody in it. None takes the families' own status away, which the operator asked for first.
    /// </summary>
    private static async Task<ClusterRead> ReadClusterAsync(
        IReadOnlyCollection<Assembly> closure,
        string provider,
        string connection,
        string? schema,
        TimeSpan? skewAllowance,
        CancellationToken cancellationToken)
    {
        var declared = Discover(closure);
        var found = declared
            .SelectMany(descriptor => FleetSourcesIn(descriptor.Assembly).Select(source => (Source: source, Module: EfModuleCatalog.Find(declared, source.ModuleName))))
            .FirstOrDefault(candidate => candidate.Module is not null);
        return found.Module is { } module
            ? await ReadClusterAsync(found.Source, module, provider, connection, schema, skewAllowance, cancellationToken)
            : ClusterRead.Unread(new() { Availability = EfToolingClusterAvailability.NoMembershipProvider });
    }

    /// <summary>The members <paramref name="source"/> reads from <paramref name="module"/>'s table, or the marker that says why it could not.</summary>
    internal static async Task<ClusterRead> ReadClusterAsync(
        IEfToolingFleetSource source,
        EfModuleDescriptor module,
        string provider,
        string connection,
        string? schema,
        TimeSpan? skewAllowance,
        CancellationToken cancellationToken)
    {
        if (module.ProviderContext(provider) is not { } contextType)
        {
            return ClusterRead.Unread(new()
            {
                Availability = EfToolingClusterAvailability.NoProviderContext,
                Module = module.Name,
                Note = $"'{module.Name}' has no context for provider '{provider}', so its members cannot be read here."
            });
        }

        try
        {
            await using var context = CreateContext(module, contextType, provider, connection, schema);
            try
            {
                await EfDatabaseMigrator.ApplyAsync(context, EfRelationalProviderBinding.ExpectedProviderName(provider), EfMigratePolicy.Validate, cancellationToken);
            }
            catch (EfPendingMigrationsException)
            {
                return ClusterRead.Unread(new()
                {
                    Availability = EfToolingClusterAvailability.NotMigrated,
                    Module = module.Name,
                    Note = $"'{module.Name}' has migrations not applied in this database, so it holds no members: a cluster of one."
                });
            }

            return new ClusterRead(module.Name, await source.ReadAsync(context, skewAllowance, cancellationToken), null);
        }
        catch (Exception failure) when (failure is not EfToolingRefusal and not OperationCanceledException)
        {
            return ClusterRead.Unread(new()
            {
                Availability = EfToolingClusterAvailability.Unreadable,
                Module = module.Name,
                Note = EfToolingRedaction.Redact($"The members could not be read from '{module.Name}': {failure.Message}", connection)
            });
        }
    }

    /// <summary>The migrate options a version-1 <c>apply</c> runs with: the defaults, and the lock bound the request carries (#2196).</summary>
    private static EfMigrateOptions MigrateOptions(EfToolingRequest request)
    {
        var options = new EfMigrateOptions();
        if (request.SqliteMigrationLockStaleAfter is null)
            return options;

        options.SqliteMigrationLockStaleAfter = EfMigrateOptions.TryParsePositiveTimeSpan(request.SqliteMigrationLockStaleAfter, out var staleAfter)
            ? staleAfter
            : throw EfToolingRefusal.Usage("invalid-request", "The 'apply' request is not valid.", [$"'sqliteMigrationLockStaleAfter' must be a positive time span such as 00:10:00, not '{request.SqliteMigrationLockStaleAfter}'."]);
        return options;
    }

    /// <summary>The skew allowance a <c>status</c> request names, refused when it is not a non-negative <c>TimeSpan</c>.</summary>
    private static TimeSpan? SkewAllowance(EfToolingRequest request)
    {
        if (request.SkewAllowance is null)
            return null;

        return TimeSpan.TryParse(request.SkewAllowance, System.Globalization.CultureInfo.InvariantCulture, out var skew) && skew >= TimeSpan.Zero
            ? skew
            : throw EfToolingRefusal.Usage("invalid-request", "The 'status' request is not valid.", [$"'skewAllowance' must be a non-negative time span such as 00:00:05, not '{request.SkewAllowance}'."]);
    }

    /// <summary>What <c>status</c> made of the cluster: the fleet it read, or the marker that says why it read none.</summary>
    internal sealed record ClusterRead(string Module, EfToolingFleet? Fleet, EfToolingCluster? UnreadCluster)
    {
        public static ClusterRead Unread(EfToolingCluster cluster) => new(cluster.Module, null, cluster);

        /// <summary>The payload, with each member's reads for the families this status lists, in each family's database.</summary>
        public EfToolingCluster Describe(IReadOnlyList<EfToolingFinalizationFamily> families) => Fleet is { } fleet
            ? new()
            {
                Availability = EfToolingClusterAvailability.Read,
                Module = Module,
                JudgedAt = fleet.JudgedAt,
                SkewAllowance = fleet.SkewAllowance.ToString("c", System.Globalization.CultureInfo.InvariantCulture),
                Members = fleet.MembersFor([.. families.Select(family => new EfToolingFamilyDatabase(family.Family, family.DatabaseIdentity))])
            }
            : UnreadCluster!;
    }

    private static IEnumerable<IEfToolingFleetSource> FleetSourcesIn(Assembly assembly)
    {
        Type[] types;
        try
        {
            types = assembly.GetExportedTypes();
        }
        catch (Exception failure) when (failure is ReflectionTypeLoadException or FileNotFoundException or TypeLoadException)
        {
            return [];
        }

        return types
            .Where(type => type is { IsAbstract: false, IsGenericTypeDefinition: false } && typeof(IEfToolingFleetSource).IsAssignableFrom(type) && type.GetConstructor(Type.EmptyTypes) is not null)
            .Select(type => (IEfToolingFleetSource)Activator.CreateInstance(type)!);
    }

    /// <summary>
    /// Places or releases a hold by compare-and-set, reading the record again after a lost race rather than failing:
    /// another host evaluating at the same moment is not a reason to refuse an operator.
    /// </summary>
    private static async Task<SchemaFinalizationRecord> ChangeHoldsAsync(
        DbContext context,
        EfSchemaChain chain,
        EfToolingFinalizationRequest request,
        bool place,
        CancellationToken cancellationToken)
    {
        var store = new SchemaFinalization.EfSchemaFinalizationStore(context);
        var readable = chain.ReadableVersions;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var record = place
                ? await store.GetOrCreateAsync(chain.Family, readable[0], readable, SchemaFinalizationActor.OfOperator(request.Operator!), cancellationToken)
                : await store.FindAsync(chain.Family, cancellationToken)
                  ?? throw new SchemaFinalizationRefusedException(chain.Family, SchemaFinalizationRefusal.NoHold, "has no finalization record in this database, so no hold is in place.");
            var write = place
                ? await store.PlaceHoldAsync(chain.Family, record.Revision, request.Version, request.Reason!, request.Operator!, readable, cancellationToken)
                : await store.ReleaseHoldAsync(chain.Family, record.Revision, request.Version, request.Operator!, cancellationToken);
            if (write.Applied)
                return write.Record;
        }

        throw EfToolingRefusal.DatabaseFailure(
            "finalization-record-contended",
            $"The finalization record of '{chain.Family}' kept changing under this command; nothing was changed. Run it again.");
    }

    private static string RefusalCode(SchemaFinalizationRefusal refusal) => refusal switch
    {
        SchemaFinalizationRefusal.RollbackBoundaryCrossed => "rollback-boundary-crossed",
        SchemaFinalizationRefusal.HoldAlreadyPlaced => "hold-already-placed",
        SchemaFinalizationRefusal.NoHold => "no-hold",
        SchemaFinalizationRefusal.UnknownVersion => "unknown-version",
        _ => "finalization-refused"
    };

    internal static EfToolingFinalizationFamily Describe(
        string module,
        string family,
        string? owner,
        IReadOnlyList<string> readable,
        SchemaFinalizationRecord? record,
        EfToolingFleet? fleet)
    {
        var status = EfSchemaFamilyStatus.Describe(family, owner, readable, record);
        return new()
        {
            Module = module,
            Family = family,
            DatabaseIdentity = status.DatabaseIdentity,
            FinalizedVersion = status.FinalizedVersion,
            ReadableVersions = status.ReadableVersions,
            Intent = status.Intent is { } intent ? new() { Version = intent.Version, Member = intent.Member.ToString(), At = intent.At } : null,
            Holds = [.. status.Holds.Select(hold => new EfToolingFinalizationHold { Version = hold.Version, Reason = hold.Reason, PlacedBy = hold.PlacedBy, PlacedAt = hold.PlacedAt })],
            Pending = [.. status.Pending.Select(pending => new EfToolingPendingVersion
            {
                Version = pending.Version,
                State = pending.State == SchemaFinalizationState.ReadableEverywhere ? "readable-everywhere" : "pending",
                HeldBy = [.. pending.HeldBy.Select(hold => hold.Reason)],
                WaitsFor = fleet?.Blockers(family, status.DatabaseIdentity, pending.Version)
            })],
            CompletionVersion = status.Finish?.CompletionVersion,
            BackfillRun = (status.Finish?.Run ?? status.Withdrawal?.Run) is { } run
                ? new() { TargetVersion = run.TargetVersion, Member = run.Member.ToString(), ExpiresAt = run.ExpiresAt }
                : null,
            CompletionWithdrawn = status.Withdrawal is { } withdrawal
                ? new() { Version = withdrawal.Version, WithdrawnBy = withdrawal.Actor.ToString(), At = withdrawal.At, Reason = withdrawal.Reason }
                : null
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
        EfRelationalProviderBinding.UseMigrationsFrom(
            builder,
            provider,
            connection,
            descriptor.HistoryTableName,
            descriptor.Assembly,
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
