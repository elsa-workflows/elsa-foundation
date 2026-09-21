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

        return new RootCommand("Elsa command-line tool.") { persistence };
    }

    private static Command List()
    {
        var host = HostOption();
        var packages = PackagesOption();
        var modules = ModulesOption();
        var all = AllOption();
        var command = new Command("list", "List the EF modules this host declares.") { host, packages, modules, all };

        command.SetAction((result, cancellationToken) => Guarded(async () =>
        {
            var layout = HostLayout.Resolve(result.GetRequiredValue(host));
            var request = Request(WorkerCommands.List, layout, result, packages) with
            {
                Selection = Selection(result, modules, all, required: false)
            };
            return Report.Render(WorkerCommands.List, await WorkerProcess.RunAsync(layout, request, cancellationToken), Console.Out, Console.Error);
        }, cancellationToken));

        return command;
    }

    private static Command Plan()
    {
        var host = HostOption();
        var packages = PackagesOption();
        var modules = ModulesOption();
        var all = AllOption();
        var provider = ProviderOption();
        var schema = SchemaOption();
        var command = new Command("plan", "Report the migrations each selected module would apply, without writing anything.")
        {
            host, packages, modules, all, provider, schema
        };

        command.SetAction((result, cancellationToken) => Guarded(async () =>
        {
            var layout = HostLayout.Resolve(result.GetRequiredValue(host));
            var request = Request(WorkerCommands.Plan, layout, result, packages) with
            {
                Selection = Selection(result, modules, all, required: true),
                Provider = result.GetRequiredValue(provider),
                Schema = Schema(result, schema)
            };
            return Report.Render(WorkerCommands.Plan, await WorkerProcess.RunAsync(layout, request, cancellationToken), Console.Out, Console.Error);
        }, cancellationToken));

        return command;
    }

    private static Command Script()
    {
        var host = HostOption();
        var packages = PackagesOption();
        var modules = ModulesOption();
        var all = AllOption();
        var provider = ProviderOption();
        var schema = SchemaOption();
        var environment = EnvironmentOption();
        var output = new Option<string>("--output") { Description = "The directory the artifact is written to.", Required = true };
        // Accepted for compatibility with the surface the issue proposed. Scripting is always idempotent —
        // a non-idempotent file would sit in the same directory looking identical while being unsafe to
        // re-run — so the flag states what already holds rather than selecting between two behaviours.
        var idempotent = new Option<bool>("--idempotent") { Description = "Accepted and implied: every script is idempotent." };
        var command = new Command("script", "Write per-module idempotent SQL and a migration plan for this host.")
        {
            host, packages, modules, all, provider, schema, output, environment, idempotent
        };

        command.SetAction((result, cancellationToken) => Guarded(async () =>
        {
            var layout = HostLayout.Resolve(result.GetRequiredValue(host));
            var request = Request(WorkerCommands.Script, layout, result, packages) with
            {
                Selection = Selection(result, modules, all, required: true),
                Provider = result.GetRequiredValue(provider),
                Schema = Schema(result, schema),
                Output = Path.GetFullPath(result.GetRequiredValue(output)),
                Environment = result.GetRequiredValue(environment)
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
        var host = HostOption();
        var packages = PackagesOption();
        var modules = ModulesOption();
        var all = AllOption();
        var provider = ProviderOption();
        var schema = SchemaOption();
        var connectionEnv = ConnectionEnvOption();
        var connectionStdin = ConnectionStdinOption();
        var command = new Command(name, description)
        {
            host, packages, modules, all, provider, schema, connectionEnv, connectionStdin
        };

        command.SetAction((result, cancellationToken) => Guarded(async () =>
        {
            var layout = HostLayout.Resolve(result.GetRequiredValue(host));
            var connection = await ResolveConnection(result, connectionEnv, connectionStdin, cancellationToken);
            var request = Request(name, layout, result, packages) with
            {
                Selection = Selection(result, modules, all, required: true),
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
        var directory = new Argument<string>("directory") { Description = "The committed artifact directory to check." };
        var command = new Command("script-check", "Regenerate a committed artifact from its own plan and report any difference.")
        {
            directory, host, packages
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
                // plan is what the artifact claims to be, so it is what the check has to hold it to.
                var request = Request(WorkerCommands.Script, layout, result, packages) with
                {
                    Selection = new() { Kind = WorkerSelection.ModulesKind, Modules = [.. plan.Modules.Select(module => module.Module)] },
                    Provider = plan.Provider,
                    Schema = plan.Schema,
                    Output = regenerated.FullName,
                    HostName = plan.HostName,
                    Shell = plan.HostShell,
                    Environment = plan.HostEnvironment
                };

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
    /// The environment whose shell-configuration overlay the provider-agreement check reads, recorded in
    /// the manifest either way (FR-038, FR-047). It defaults to <c>Production</c> — ASP.NET Core's own
    /// default when a host sets none — rather than to "no overlay", and never to this tool's own
    /// environment variables, which are not the host's.
    /// </summary>
    private static Option<string> EnvironmentOption() => new("--environment")
    {
        Description = "The host environment the manifest records. Default: Production.",
        DefaultValueFactory = _ => "Production"
    };

    /// <summary>
    /// The fields every command's request carries. <c>Environment</c> starts at the same default
    /// <see cref="EnvironmentOption"/> declares, because the worker needs one on every command and only
    /// <c>script</c> takes the flag that changes it.
    /// </summary>
    private static WorkerRequest Request(string command, HostLayout layout, ParseResult result, Option<string[]> packages) => new()
    {
        Command = command,
        HostDirectory = layout.Directory,
        HostName = layout.Name,
        DepsFile = layout.DepsFile,
        PackageRoots = [.. (result.GetValue(packages) ?? []).Select(Path.GetFullPath)],
        Environment = "Production"
    };

    /// <summary>
    /// Exactly one selector, and no default (FR-028). <c>list</c> is the one command that selects nothing by
    /// default, because naming every module a host declares is what it is for.
    /// </summary>
    private static WorkerSelection? Selection(ParseResult result, Option<string[]> modules, Option<bool> all, bool required)
    {
        var names = (result.GetValue(modules) ?? [])
            .SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToArray();
        var everything = result.GetValue(all);

        if (names.Length > 0 && everything)
            throw CliRefusal.Usage("invalid-selection", "--modules and --all cannot be given together; give exactly one.");
        if (names.Length > 0)
            return new() { Kind = WorkerSelection.ModulesKind, Modules = names };
        if (everything)
            return new() { Kind = WorkerSelection.AllKind };
        if (required)
            throw CliRefusal.Usage("invalid-selection", "Exactly one of --modules and --all is required; there is no default selection.");

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
