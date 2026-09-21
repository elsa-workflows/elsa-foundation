using Elsa.Cli.Worker;
using System.CommandLine;

namespace Elsa.Cli;

/// <summary>
/// The <c>dotnet elsa</c> command surface. This slice ships <c>persistence list</c>, <c>plan</c>,
/// <c>script</c> and <c>script-check</c>; <c>apply</c>, <c>validate</c> and <c>post-migrate</c> arrive with
/// the slices that implement them, so an operator asking for one today is told it is unrecognized rather
/// than handed a command that does nothing.
/// </summary>
internal static class ElsaCli
{
    public static RootCommand Build()
    {
        var persistence = new Command("persistence", "Inspect and script a host's EF persistence modules.")
        {
            List(),
            Plan(),
            Script(),
            ScriptCheckCommand()
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

    private static Option<string[]> PackagesOption() => new("--packages")
    {
        Description = "A package root to resolve module assemblies from. Repeatable.",
        AllowMultipleArgumentsPerToken = true
    };

    private static Option<string[]> ModulesOption() => new("--modules")
    {
        Description = "Canonical module names, comma-separated or repeated.",
        AllowMultipleArgumentsPerToken = true
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
