using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Elsa3.Migration.Tooling;

/// <summary>The two commands (spec 1237 FR-001).</summary>
internal enum MigrationCommand
{
    /// <summary>The migration host's committed composition plus the Elsa 3 addition list.</summary>
    Inventory,

    /// <summary>As <see cref="Inventory"/>, plus the host's local configuration sources.</summary>
    Assess
}

/// <summary>
/// How the CLI reaches the migration host without the tooling referencing it (spec 1237 research R-5, T008): the host
/// assembly, the host and CLI project folders written into the CLI's assembly metadata at build time, and the host's two
/// wiring methods (T004a) as delegates. Tests pass their own through the internal seam.
/// </summary>
/// <param name="AddConfiguration">
/// <c>MigrationHost.AddHostConfiguration</c> bound to the host assembly and an empty argument list:
/// (builder, host folder, environment, include local sources).
/// </param>
/// <param name="AddServices"><c>MigrationHost.AddHostServices</c>.</param>
internal sealed record HostWiring(
    Assembly HostAssembly,
    string HostProjectFolder,
    string CliProjectFolder,
    Action<IConfigurationBuilder, string, string, bool> AddConfiguration,
    Action<IHostApplicationBuilder> AddServices);

/// <summary>One run of the migration CLI, as the command line gave it.</summary>
/// <param name="OutputPath">The output folder as it was given; the CLI prints it back unchanged (FR-005).</param>
internal sealed record MigrationRequest(
    MigrationCommand Command,
    string ExportPath,
    string OutputPath,
    bool AllVersions,
    string ToolVersion)
{
    /// <summary>The output folder when <c>--out</c> is not given (contracts/cli.md).</summary>
    public const string DefaultOutputPath = "elsa3-migration-out";

    /// <summary>The report's <c>command</c> value.</summary>
    public string CommandName => Command == MigrationCommand.Assess ? "assess" : "inventory";

    /// <summary>
    /// Checks the arguments that need no file system beyond the paths themselves; returns the <c>arguments</c> refusal,
    /// or null. Whether the export exists, is a folder or a zip, and holds a definition is the export reader's to decide.
    /// </summary>
    public MigrationRefusal? Validate()
    {
        if (!Enum.IsDefined(Command))
            return new(MigrationRefusal.Arguments, "The command must be 'workflows inventory' or 'workflows assess'.");
        if (string.IsNullOrWhiteSpace(ExportPath))
            return new(MigrationRefusal.Arguments, "An export path is required.");
        if (string.IsNullOrWhiteSpace(OutputPath))
            return new(MigrationRefusal.Arguments, "The --out folder must not be empty.");
        if (string.IsNullOrWhiteSpace(ToolVersion))
            return new(MigrationRefusal.Arguments, "The tool version is missing.");
        if (ExportPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return new(MigrationRefusal.Arguments, "The export path is not a valid path.");
        if (OutputPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return new(MigrationRefusal.Arguments, "The --out folder is not a valid path.");
        // An existing file, the export zip included, cannot be the output folder.
        if (File.Exists(OutputPath))
            return new(MigrationRefusal.Arguments, "The --out path names a file; it must be a folder.");
        for (var parent = Path.GetDirectoryName(Path.GetFullPath(OutputPath)); !string.IsNullOrEmpty(parent); parent = Path.GetDirectoryName(parent))
        {
            if (File.Exists(parent))
                return new(MigrationRefusal.Arguments, "The --out path lies inside a file; it must be a folder.");
        }

        return null;
    }
}
