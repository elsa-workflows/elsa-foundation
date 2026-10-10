using System.CommandLine;
using System.Text;
using System.Text.Json;
using Elsa.Cli.Worker;
using Elsa.Modularity.Planning.Catalog;
using Elsa.Modularity.Planning.Json;
using Elsa.Modularity.Planning.Models;
using Elsa.Modularity.Planning.Services;
using static Elsa.Cli.CompositionFileBridgeOutput;

namespace Elsa.Cli;

/// <summary>Creates an authored composition pinned to a Foundation profile in the bundled catalog.</summary>
internal static class CompositionInitCommand
{
    private static readonly JsonSerializerOptions s_json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static Command Build()
    {
        var profile = Required("--profile", "Foundation profile ID and version, such as embedded-runtime@1.");
        var groups = new Option<string[]>("--group") { Description = "Foundation feature-group ID and version. Repeatable." };
        var output = Required("--output", "Fresh authored composition file.");
        var command = new Command("init", "Create a pinned composition from a bundled Foundation profile and optional groups.")
        {
            profile, groups, output
        };

        command.SetAction((result, cancellationToken) => Task.FromResult(Guarded(() => Run(
            result.GetRequiredValue(profile), result.GetValue(groups) ?? [], result.GetRequiredValue(output), cancellationToken), "initialized")));
        return command;
    }

    private static int Run(string profilePin, IReadOnlyList<string> groupPins, string outputPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var catalog = FoundationSelectionCatalog.Load();
        var definition = FindDefinition(profilePin, "profile", catalog.Profiles);
        var groupDefinitions = groupPins.Select(pin => FindDefinition(pin, "group", catalog.Groups)).ToArray();
        if (groupDefinitions.Select(item => (item.Id, item.Version)).Distinct().Count() != groupDefinitions.Length)
            throw CliRefusal.Usage("composition-group-duplicate", "Each Foundation group may be selected only once.");

        var authored = new AuthoredComposition(
            "1",
            new CatalogPin(catalog.Id, catalog.Version, catalog.Digest),
            new DefinitionReference("foundation", "profile", definition.Id, definition.Version, definition.Digest),
            [.. groupDefinitions.OrderBy(item => item.Id, StringComparer.Ordinal)
                .ThenBy(item => item.Version, StringComparer.Ordinal)
                .Select(item => new DefinitionReference("foundation", "group", item.Id, item.Version, item.Digest))],
            [],
            [],
            new AcceptedSelection(catalog.Digest, [], []),
            null,
            null);
        var plan = SelectionPlanner.Plan(catalog, authored);
        authored = authored with
        {
            Accepted = plan.Accepted with
            {
                FeatureIds = plan.SelectedFeatureIds,
                Locks = plan.ObservedLocks
            }
        };

        var json = JsonSerializer.Serialize(authored, s_json);
        _ = SelectionJsonReader.ParseComposition(json);
        cancellationToken.ThrowIfCancellationRequested();
        WriteNewFile(outputPath, json);
        Console.Out.WriteLine($"Initialized {definition.Id}@{definition.Version} with {plan.SelectedFeatureIds.Length} exact feature IDs.");
        return ToolExitCode.Success;
    }

    private static SelectionDefinition FindDefinition(
        string pin, string kind, IEnumerable<SelectionDefinition> definitions)
    {
        var separator = pin.IndexOf('@');
        if (separator <= 0 || separator == pin.Length - 1 || pin.IndexOf('@', separator + 1) >= 0)
            throw CliRefusal.Usage($"composition-{kind}-invalid", $"Use a Foundation {kind} ID and version in the form <id>@<version>.");

        return definitions.FirstOrDefault(item => item.Id == pin[..separator] && item.Version == pin[(separator + 1)..])
            ?? throw CliRefusal.Usage($"composition-{kind}-unknown", $"The requested {kind} is not present in the bundled Foundation catalog.");
    }

    private static void WriteNewFile(string outputPath, string contents)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(outputPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw CliRefusal.Resolution("composition-output-unavailable", "The authored composition output path is unavailable.");
        }

        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            throw CliRefusal.Resolution("composition-output-unavailable", "The authored composition output location is unavailable.");
        if (File.Exists(fullPath) || Directory.Exists(fullPath))
            throw CliRefusal.Resolution("composition-output-exists", "The authored composition output already exists.");

        var fileName = Path.GetFileName(fullPath);
        var stagingFileName = $".{fileName}.{Guid.NewGuid():N}.tmp";
        var stagingPath = Path.Join(directory, stagingFileName);
        try
        {
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.WriteThrough
            };
            if (!OperatingSystem.IsWindows())
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

            using (var stream = new FileStream(stagingPath, options))
            {
                var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(contents);
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(stagingPath, fullPath);
        }
        catch (CliRefusal)
        {
            throw;
        }
        catch (IOException)
        {
            if (File.Exists(fullPath) || Directory.Exists(fullPath))
                throw CliRefusal.Resolution("composition-output-exists", "The authored composition output already exists.");
            throw CliRefusal.Resolution("composition-output-failed", "The authored composition could not be written.");
        }
        catch (UnauthorizedAccessException)
        {
            throw CliRefusal.Resolution("composition-output-failed", "The authored composition could not be written.");
        }
        finally
        {
            try
            {
                File.Delete(stagingPath);
            }
            catch (IOException)
            {
                // Preserve the primary safe refusal or success result.
            }
            catch (UnauthorizedAccessException)
            {
                // Preserve the primary safe refusal or success result.
            }
        }
    }
}
