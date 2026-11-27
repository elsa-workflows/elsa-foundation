using System.CommandLine;
using System.Reflection;
using Elsa3.Migration.Tooling;

namespace Elsa3.Migration.Cli;

/// <summary>
/// The command line of contracts/cli.md: <c>workflows inventory</c> and <c>workflows assess</c>, their argument rules,
/// the exit codes (FR-004), and the printed output path or refusal line (FR-005).
/// </summary>
internal static class MigrationCommandLine
{
    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, HostWiring wiring, CancellationToken cancellationToken)
    {
        var export = new Argument<string>("export") { Description = "An Elsa 3 export: a folder (searched recursively for *.json) or a .zip." };
        var outPath = new Option<string>("--out")
        {
            Description = "The output folder; only report.json, report.html and migrated/ are replaced.",
            DefaultValueFactory = _ => MigrationRequest.DefaultOutputPath
        };
        var allVersions = new Option<bool>("--all-versions") { Description = "Converts every version instead of the latest published and the pinned ones." };

        MigrationRequest? request = null;
        Command Subcommand(string name, MigrationCommand command, string description)
        {
            var subcommand = new Command(name, description) { export, outPath, allVersions };
            subcommand.SetAction(parse => request = new MigrationRequest(
                command,
                parse.GetValue(export) ?? string.Empty,
                parse.GetValue(outPath) ?? string.Empty,
                parse.GetValue(allVersions),
                ToolVersion()));
            return subcommand;
        }

        var workflows = new Command("workflows", "Elsa 3 workflow definitions.")
        {
            Subcommand("inventory", MigrationCommand.Inventory, "Test-migrates an export on the migration host's committed composition."),
            Subcommand("assess", MigrationCommand.Assess, "As inventory, with the migration host's local configuration sources and features.")
        };
        var root = new RootCommand("The Elsa 3 workflows migration CLI.") { workflows };

        var parsed = root.Parse(args);
        if (parsed.Errors.Count > 0)
            return Refuse(error, new(MigrationRefusal.Arguments, parsed.Errors[0].Message));

        // Help and version print and end here; every other valid line sets the request.
        var invoked = await parsed.InvokeAsync(new InvocationConfiguration { Output = output, Error = error }, cancellationToken);
        if (request is null)
            return invoked;

        var result = await Elsa3MigrationRun.RunAsync(request, wiring, cancellationToken);
        if (result.Refusal is { } refusal)
            return Refuse(error, refusal);

        output.WriteLine(result.OutputPath);
        return result.ExitCode;
    }

    private static int Refuse(TextWriter error, MigrationRefusal refusal)
    {
        error.WriteLine(refusal.ToLine());
        return Elsa3MigrationRun.RefusedExitCode;
    }

    /// <summary>The CLI's informational version, written into the report's <c>tool.version</c>.</summary>
    private static string ToolVersion() =>
        typeof(MigrationCommandLine).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(MigrationCommandLine).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";
}
