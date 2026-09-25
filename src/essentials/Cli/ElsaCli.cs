using Elsa.Cli.Worker;
using System.CommandLine;

namespace Elsa.Cli;

/// <summary>
/// The <c>dotnet elsa</c> command surface (FR-024): <c>persistence list</c>, <c>plan</c>, <c>script</c>,
/// <c>script-check</c>, <c>apply</c>, <c>validate</c> and <c>post-migrate</c>.
/// </summary>
internal static class ElsaCli
{
    public static RootCommand Build()
    {
        var persistence = new Command("persistence", "Inspect, script, apply and validate a host's EF persistence modules.")
        {
            List(),
            Plan(),
            Script(),
            ScriptCheckCommand(),
            OpensDatabase(WorkerCommands.Apply, "Run each selected module's compiled migrations against a database."),
            OpensDatabase(WorkerCommands.Validate, "Fail if any selected module has a pending migration, or has a post-migration action that has not been run. Applies nothing."),
            OpensDatabase(WorkerCommands.PostMigrate, "Run each selected module's required post-migration actions against a database. The only command that runs one.")
        };

        var composition = new Command("composition", "Inspect and review feature selections.")
        {
            CompositionPlanCommand.Build(),
            CompositionImportCommand.Build()
        };

        return new RootCommand("Elsa command-line tool.") { persistence, composition };
    }

    private static Command List()
    {
        var selectors = new Selectors();
        var command = selectors.Build("list", "List the EF modules this host declares.");

        command.SetAction((result, cancellationToken) => Guarded(async () =>
        {
            var (layout, request) = selectors.Resolve(WorkerCommands.List, result, selectionRequired: false);
            return Report.Render(WorkerCommands.List, await WorkerProcess.RunAsync(layout, request, cancellationToken), Console.Out, Console.Error);
        }, cancellationToken));

        return command;
    }

    private static Command Plan()
    {
        var selectors = new Selectors();
        var provider = ProviderOption();
        var schema = SchemaOption();
        var command = selectors.Build("plan", "Report the migrations each selected module would apply, without writing anything.", provider, schema);

        command.SetAction((result, cancellationToken) => Guarded(async () =>
        {
            var (layout, resolved) = selectors.Resolve(WorkerCommands.Plan, result, selectionRequired: true);
            var request = resolved with
            {
                Provider = result.GetRequiredValue(provider),
                Schema = Schema(result, schema)
            };
            return Report.Render(WorkerCommands.Plan, await WorkerProcess.RunAsync(layout, request, cancellationToken), Console.Out, Console.Error);
        }, cancellationToken));

        return command;
    }

    private static Command Script()
    {
        var selectors = new Selectors();
        var provider = ProviderOption();
        var schema = SchemaOption();
        var output = new Option<string>("--output") { Description = "The directory the artifact is written to.", Required = true };
        // Accepted for compatibility with the surface the issue proposed. Scripting is always idempotent —
        // a non-idempotent file would sit in the same directory looking identical while being unsafe to
        // re-run — so the flag states what already holds rather than selecting between two behaviours.
        var idempotent = new Option<bool>("--idempotent") { Description = "Accepted and implied: every script is idempotent." };
        var command = selectors.Build("script", "Write per-module idempotent SQL and a migration plan for this host.", provider, schema, output, idempotent);

        command.SetAction((result, cancellationToken) => Guarded(async () =>
        {
            var (layout, resolved) = selectors.Resolve(WorkerCommands.Script, result, selectionRequired: true);
            var request = resolved with
            {
                Provider = result.GetRequiredValue(provider),
                Schema = Schema(result, schema),
                Output = Path.GetFullPath(result.GetRequiredValue(output))
            };
            return Report.Render(WorkerCommands.Script, await WorkerProcess.RunAsync(layout, request, cancellationToken), Console.Out, Console.Error);
        }, cancellationToken));

        return command;
    }

    /// <summary>
    /// The three commands that open the host's database: identical apart from their name and what the host's
    /// tooling does with the request, including their connection handling (FR-030, FR-050, D7), so they are
    /// built once rather than diverging one flag at a time.
    /// </summary>
    private static Command OpensDatabase(string name, string description)
    {
        var selectors = new Selectors();
        var provider = ProviderOption();
        var schema = SchemaOption();
        var connectionEnv = ConnectionEnvOption();
        var connectionStdin = ConnectionStdinOption();
        var command = selectors.Build(name, description, provider, schema, connectionEnv, connectionStdin);

        command.SetAction((result, cancellationToken) => Guarded(async () =>
        {
            var (layout, resolved) = selectors.Resolve(name, result, selectionRequired: true);
            var connection = await ResolveConnection(result, connectionEnv, connectionStdin, cancellationToken);
            var request = resolved with
            {
                Provider = result.GetRequiredValue(provider),
                Schema = Schema(result, schema),
                ConnectionEnv = connection.Env,
                Connection = connection.Value
            };
            return Report.Render(name, await WorkerProcess.RunAsync(layout, request, cancellationToken), Console.Out, Console.Error);
        }, cancellationToken));

        return command;
    }

    private static Command ScriptCheckCommand()
    {
        var host = HostOption();
        var packages = PackagesOption();
        var restore = RestoreOption();
        var directory = new Argument<string>("directory") { Description = "The committed artifact directory to check." };
        var command = new Command("script-check", "Regenerate a committed artifact from its own plan and report any difference.")
        {
            directory, host, packages, restore
        };

        command.SetAction((result, cancellationToken) => Guarded(async () =>
        {
            var layout = HostLayout.Resolve(result.GetRequiredValue(host));
            var artifact = Path.GetFullPath(result.GetRequiredValue(directory));
            var plan = MigrationPlan.Read(artifact);
            var regenerated = Directory.CreateTempSubdirectory("elsa-script-check-");
            try
            {
                // Regenerated from the committed plan's own facts, never from a selection typed here: the
                // plan is what the artifact claims to be, so it is what the check has to hold it to. That
                // includes the shell and environment it names, so the provider-agreement check re-runs
                // against exactly the configuration the committed artifact was produced from.
                var selection = new WorkerSelection { Kind = WorkerSelection.ModulesKind, Modules = [.. plan.Modules.Select(module => module.Module)] };
                var request = Request(WorkerCommands.Script, layout, result, packages, restore) with
                {
                    Selection = selection,
                    Provider = plan.Provider,
                    Schema = plan.Schema,
                    Output = regenerated.FullName,
                    HostName = plan.SchemaVersion == MigrationPlan.ContextSchemaVersion ? layout.Name : plan.HostName
                };
                if (plan.ConfigurationContext is { } context)
                {
                    if (!string.Equals(plan.HostName, layout.Name, StringComparison.Ordinal))
                        throw CliRefusal.Resolution("plan-host-mismatch", "The committed plan names a different host than the selected host directory.");
                    request = request with
                    {
                        Environment = plan.HostEnvironment,
                        Shell = plan.HostShell,
                        ContextSource = context.Source,
                        ContextVersion = 1,
                        Resource = context.Resource
                    };
                }
                else
                    request = WithHostConfiguration(request, layout, plan.HostEnvironment, plan.HostShell);

                var response = await WorkerProcess.RunAsync(layout, request, cancellationToken);
                if (response.ExitCode != ToolExitCode.Success)
                    return Report.Render(WorkerCommands.Script, response, Console.Out, Console.Error);

                var report = ScriptCheck.Compare(artifact, regenerated.FullName, plan);
                var writer = report.ExitCode == ToolExitCode.Success ? Console.Out : Console.Error;
                writer.WriteLine($"script-check: {report.Headline}");
                foreach (var line in report.Lines)
                    writer.WriteLine($"  {line}");
                return report.ExitCode;
            }
            finally
            {
                regenerated.Delete(recursive: true);
            }
        }, cancellationToken));

        return command;
    }

    /// <summary>
    /// The options every module-selecting command shares — where the host is, which modules to run against,
    /// and which shell configuration to read them from — plus the one place that turns them into a request.
    /// One instance per command, because System.CommandLine binds each <see cref="Option"/> to the command
    /// it was added to.
    /// </summary>
    private sealed class Selectors
    {
        public Option<string> Host { get; } = HostOption();

        public Option<string[]> Packages { get; } = PackagesOption();

        public Option<bool> Restore { get; } = RestoreOption();

        public Option<string[]> Modules { get; } = ModulesOption();

        public Option<bool> All { get; } = AllOption();

        public Option<bool> FromHost { get; } = new("--from-host")
        {
            Description = "Every module this host's own enabled shell features declare a dependency on."
        };

        /// <summary>
        /// Which shell's features are read (FR-028). With none given, every shell the host configures is
        /// read: a disagreement in any of them is reported rather than silently skipped, and naming a shell
        /// is how an operator narrows the check to the one the artifact is for.
        /// </summary>
        public Option<string?> Shell { get; } = new("--shell")
        {
            Description = "The shell whose features are read. Default: every shell this host configures."
        };

        /// <summary>
        /// The environment whose shell-configuration overlay is read, recorded in the manifest either way
        /// (FR-038, FR-047). It defaults to <c>Production</c> — ASP.NET Core's own default when a host sets
        /// none — rather than to "no overlay", and never to this tool's own
        /// <c>ASPNETCORE_ENVIRONMENT</c>/<c>DOTNET_ENVIRONMENT</c>, which are facts about this process and
        /// not about the host it is inspecting.
        /// </summary>
        public Option<string> Environment { get; } = new("--environment")
        {
            Description = "The host environment whose shells.<environment>.json overlay is read. Default: Production.",
            DefaultValueFactory = _ => "Production"
        };

        public Option<string?> ConfigurationContext { get; } = new("--configuration-context")
        {
            Description = "Host-owned configuration source: workbench-json-v1 or workbench-json-environment-v1. Requires --shell."
        };

        public Option<string?> Resource { get; } = new("--resource")
        {
            Description = "A named persistence resource in the selected configuration context."
        };

        /// <summary>A command carrying every shared option, plus whatever else that command takes.</summary>
        public Command Build(string name, string description, params Option[] extra)
        {
            var command = new Command(name, description);
            foreach (var option in new Option[] { Host, Packages, Restore, Modules, All, FromHost, Shell, Environment, ConfigurationContext, Resource }.Concat(extra))
                command.Add(option);
            return command;
        }

        public (HostLayout Layout, WorkerRequest Request) Resolve(string command, ParseResult result, bool selectionRequired)
        {
            var layout = HostLayout.Resolve(result.GetRequiredValue(Host));
            var request = Request(command, layout, result, Packages, Restore) with
            {
                Selection = Selection(result, Modules, All, FromHost, selectionRequired)
            };
            var source = result.GetValue(ConfigurationContext);
            var resource = result.GetValue(Resource);
            var shell = result.GetValue(Shell);
            var environment = result.GetRequiredValue(Environment);
            if (resource is not null && source is null)
                throw CliRefusal.Usage("invalid-selection", "--resource requires --configuration-context.");
            if (source is not null)
            {
                if (!WorkerContextSources.IsSupported(source))
                    throw CliRefusal.Usage("invalid-configuration-context", "The selected configuration context source is not supported.");
                if (string.IsNullOrWhiteSpace(shell))
                    throw CliRefusal.Usage("invalid-selection", "--configuration-context requires exactly one --shell.");
                if (resource is not null && string.IsNullOrWhiteSpace(resource))
                    throw CliRefusal.Usage("invalid-selection", "--resource must name a resource.");
                return (layout, request with
                {
                    Environment = environment,
                    Shell = shell,
                    ContextSource = source,
                    ContextVersion = 1,
                    Resource = resource
                });
            }
            return (layout, WithHostConfiguration(request, layout, environment, shell));
        }
    }

    /// <summary>
    /// Reads the host's shell configuration and attaches what the provider-agreement check needs (FR-035,
    /// FR-038). The check runs whenever that configuration is found, under any selector — <c>--from-host</c>
    /// only makes it the selection as well — so this happens on every command, and its absence is recorded
    /// rather than refused. It is refused only when the command asked for something that configuration alone
    /// can answer: a named shell, or a selection made of the host's own features.
    /// </summary>
    private static WorkerRequest WithHostConfiguration(WorkerRequest request, HostLayout layout, string environment, string? shell)
    {
        var configuration = ShellConfiguration.Read(layout.Directory, environment);
        if (configuration is null)
        {
            var files = $"neither '{ShellConfiguration.BaseFileName}' nor '{ShellConfiguration.OverlayFileName(environment)}' is beside the host at '{layout.Directory}'";
            if (!string.IsNullOrWhiteSpace(shell))
                throw CliRefusal.Resolution("shells-configuration-missing", $"--shell '{shell}' names a shell to read and {files}.");
            if (request.Selection?.Kind == WorkerSelection.FromHostKind)
                throw CliRefusal.Resolution("shells-configuration-missing", $"--from-host selects the modules this host's shell configuration enables, and {files}.");
        }

        return request with
        {
            Environment = environment,
            Shell = shell,
            Shells = configuration?.EnabledFeatures(shell)
        };
    }

    private static Option<string> HostOption() => new("--host")
    {
        Description = "The published output directory of the host to run against.",
        Required = true
    };

    // Deliberately not `AllowMultipleArgumentsPerToken`: that setting makes a single `--packages`/`--modules`
    // occurrence swallow every following token as a value of its own, including one that looks like a flag
    // this build does not define — `--connection`, the one flag `apply`/`validate` must reject as a usage
    // error (D7), among them. Repeated occurrences and comma-separated values inside one still work; only
    // a bare space-separated run of extra tokens does not, and nothing here advertised that it did.
    private static Option<string[]> PackagesOption() => new("--packages")
    {
        Description = "A package root to resolve module assemblies from. Repeatable."
    };

    /// <summary>
    /// The one flag that lets this tool write under the host's directories and reach a feed (ADR 0076 D10's
    /// opt-in exception, FR-083). Off by default and implied by nothing: every other flag leaves the
    /// "never downloads" rule exactly as it was, and this one only acts on a host that records no package
    /// set at all.
    /// </summary>
    private static Option<bool> RestoreOption() => new("--restore")
    {
        Description =
            "Populate this host's package set from its own Nuplane configuration first, when it records none. " +
            "Single-point version pins only. Off by default; no other flag implies it."
    };

    private static Option<string[]> ModulesOption() => new("--modules")
    {
        Description = "Canonical module names, comma-separated or repeated."
    };

    private static Option<bool> AllOption() => new("--all") { Description = "Every module this host declares." };

    private static Option<string> ProviderOption() => new("--provider")
    {
        Description = "Sqlite, SqlServer, PostgreSql or MySql. Authoritative: no command substitutes another.",
        Required = true
    };

    private static Option<string?> SchemaOption() => new("--schema")
    {
        Description = "The schema the artifact targets. Falls back to the ELSA_EF_SCHEMA environment variable."
    };

    /// <summary>
    /// The schema this run targets: the flag when given, otherwise <c>ELSA_EF_SCHEMA</c> (FR-029), which
    /// <c>tools/ef/module-migrate.sh</c> already reads and an operator already sets for a host that lives in
    /// its own schema. Read from this tool's own environment because it is an input to the command, unlike
    /// the host environment <c>--environment</c> names, which is a fact about a host this tool cannot see.
    /// </summary>
    private static string? Schema(ParseResult result, Option<string?> schema) =>
        result.GetValue(schema) is { Length: > 0 } explicitly
            ? explicitly
            : System.Environment.GetEnvironmentVariable("ELSA_EF_SCHEMA") is { Length: > 0 } configured
                ? configured
                : null;

    private static Option<string> ConnectionEnvOption() => new("--connection-env")
    {
        Description = "The environment variable this process's own environment carries the connection string in. Default: ELSA_EF_CONNECTION.",
        DefaultValueFactory = _ => "ELSA_EF_CONNECTION"
    };

    private static Option<bool> ConnectionStdinOption() => new("--connection-stdin")
    {
        Description = "Read the connection string from this process's own stdin instead of an environment variable."
    };

    /// <summary>
    /// The connection the database-opening commands take (FR-030, D7): never a flag value, so it never lands
    /// in a process argument or shell history. <c>--connection-stdin</c> reads the value from this tool's
    /// own stdin — a stream distinct from the worker's stdin, which is a fresh pipe this process opens for
    /// that child, not the console stream read here — and carries it to the worker inside the request that
    /// already travels that pipe. Otherwise, only <c>--connection-env</c>'s NAME travels to the worker; the
    /// value stays in this process's environment and reaches the worker by ordinary process-environment
    /// inheritance, read there rather than here.
    /// </summary>
    private static async Task<(string? Env, string? Value)> ResolveConnection(
        ParseResult result,
        Option<string> connectionEnv,
        Option<bool> connectionStdin,
        CancellationToken cancellationToken)
    {
        if (result.GetValue(connectionStdin))
        {
            var value = (await Console.In.ReadToEndAsync(cancellationToken)).Trim();
            if (value.Length == 0)
                throw CliRefusal.Usage("connection-missing", "--connection-stdin was given but this process's stdin carried no connection string.");
            return (null, value);
        }

        return (result.GetRequiredValue(connectionEnv), null);
    }

    /// <summary>
    /// The fields every command's request carries. <c>Environment</c> starts at the same default
    /// <see cref="Selectors.Environment"/> declares, because the worker needs one on every command and
    /// <see cref="WithHostConfiguration"/> then states the one actually used.
    /// </summary>
    /// <remarks>
    /// Also the one place the <c>--restore</c>/<c>--packages</c> combination is refused, because it is the
    /// one place both are read. A <c>--packages</c> root is an already-assembled set with nothing to
    /// populate it from, so the pair is a usage error rather than a flag that would be quietly ignored —
    /// the same rule the tool applies to a <c>--connection</c> that does not exist (D7).
    /// </remarks>
    private static WorkerRequest Request(
        string command,
        HostLayout layout,
        ParseResult result,
        Option<string[]> packages,
        Option<bool> restore)
    {
        var roots = (result.GetValue(packages) ?? []).Select(Path.GetFullPath).ToArray();
        var wantsRestore = result.GetValue(restore);
        if (wantsRestore && roots.Length > 0)
        {
            throw CliRefusal.Usage(
                "restore-with-packages",
                "--restore and --packages cannot be combined: a --packages root is an already-assembled package set, " +
                "and no command populates one. Give --restore to populate this host's own set, or --packages to read " +
                "one that is already there.",
                [.. roots.Select(root => $"'{root}' was given as a --packages root.")]);
        }

        return new()
        {
            Command = command,
            HostDirectory = layout.Directory,
            HostName = layout.Name,
            DepsFile = layout.DepsFile,
            PackageRoots = roots,
            Restore = wantsRestore,
            Environment = "Production"
        };
    }

    /// <summary>
    /// Exactly one selector, and no default (FR-028). <c>list</c> is the one command that selects nothing by
    /// default, because naming every module a host declares is what it is for.
    /// </summary>
    private static WorkerSelection? Selection(
        ParseResult result,
        Option<string[]> modules,
        Option<bool> all,
        Option<bool> fromHost,
        bool required)
    {
        var names = (result.GetValue(modules) ?? [])
            .SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToArray();
        var everything = result.GetValue(all);
        var host = result.GetValue(fromHost);

        var given = new[] { names.Length > 0, everything, host }.Count(selector => selector);
        if (given > 1)
            throw CliRefusal.Usage("invalid-selection", "--modules, --all and --from-host cannot be combined; give exactly one.");
        if (names.Length > 0)
            return new() { Kind = WorkerSelection.ModulesKind, Modules = names };
        if (everything)
            return new() { Kind = WorkerSelection.AllKind };
        if (host)
            return new() { Kind = WorkerSelection.FromHostKind };
        if (required)
            throw CliRefusal.Usage("invalid-selection", "Exactly one of --modules, --all and --from-host is required; there is no default selection.");

        return null;
    }

    private static async Task<int> Guarded(Func<Task<int>> action, CancellationToken cancellationToken)
    {
        try
        {
            return await action();
        }
        catch (CliRefusal refusal)
        {
            Report.WriteRefusal(Console.Error, refusal.Code, refusal.Message, refusal.Details);
            return refusal.ExitCode;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Interrupted, not refused: nothing claims an artifact was produced or rejected.
            Console.Error.WriteLine("cancelled.");
            return ToolExitCode.Refusal;
        }
    }
}
