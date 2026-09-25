using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Cli.Worker;
using Elsa.Modularity.Planning.Bridge;
using Elsa.Modularity.Planning.Json;

namespace Elsa.Cli;

/// <summary>Reviews existing local CShells files and writes an accepted portable baseline.</summary>
internal static class CompositionImportCommand
{
    private static readonly JsonSerializerOptions s_json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static Command Build()
    {
        var host = Required("--host-dir", "Local Workbench-style host directory.");
        var shell = Required("--shell", "Selected shell ID.");
        var environment = Required("--environment", "Selected environment name.");
        var catalog = Required("--catalog", "Pinned selection catalog JSON.");
        var review = new Option<string>("--setting-review") { Description = "Optional local setting safety review JSON." };
        var output = Required("--output", "Fresh authored composition file.");
        var command = new Command("import", "Review existing local feature configuration and accept a portable baseline.")
        {
            host, shell, environment, catalog, review, output
        };

        command.SetAction((result, cancellationToken) => Task.FromResult(Guarded(() => Run(
            result.GetRequiredValue(host),
            result.GetRequiredValue(shell),
            result.GetRequiredValue(environment),
            result.GetRequiredValue(catalog),
            result.GetValue(review),
            result.GetRequiredValue(output),
            cancellationToken))));
        return command;
    }

    private static Option<string> Required(string name, string description) =>
        new(name) { Description = description, Required = true };

    private static int Run(
        string hostDirectory,
        string shellId,
        string environment,
        string catalogPath,
        string? reviewPath,
        string outputPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var source = CompositionFileSource.Open(hostDirectory, shellId, environment);
        var snapshot = source.Snapshot;
        var catalog = SelectionJsonReader.ParseCatalog(ReadInput(catalogPath));
        var settingReview = reviewPath is null ? null : SettingReviewReader.Parse(ReadInput(reviewPath));
        var imported = CompositionImporter.Import(
            snapshot.ReadText("shells.json"),
            snapshot.ReadText(snapshot.Selection.ShellOverlayFileName),
            snapshot.ReadText("appsettings.json"),
            snapshot.Selection.AppsettingsOverlayFileName is { } appOverlay ? snapshot.ReadText(appOverlay) : null,
            shellId,
            environment,
            catalog,
            settingReview);

        var findings = imported.Plan.Findings.Select(finding =>
        {
            RequireSafe(finding.Code);
            RequireSafe(finding.Severity);
            if (finding.FeatureId is not null)
                RequireSafe(finding.FeatureId);
            if (finding.DependencyId is not null)
                RequireSafe(finding.DependencyId);
            return new { finding.Code, finding.Severity, finding.FeatureId, finding.DependencyId };
        }).ToArray();
        var preview = JsonSerializer.Serialize(new { imported.Preview, Findings = findings }, s_json);
        Console.Out.WriteLine(preview);

        if (Console.IsInputRedirected)
            throw CliRefusal.Usage("bridge-review-required", "An interactive review decision is required.");

        Console.Error.Write("Type accept to write the authored composition: ");
        if (!string.Equals(Console.ReadLine(), "accept", StringComparison.Ordinal))
            throw CliRefusal.Usage("bridge-review-required", "The composition preview was not accepted.");

        cancellationToken.ThrowIfCancellationRequested();
        var authoredJson = JsonSerializer.Serialize(imported.Authored, s_json);
        _ = SelectionJsonReader.ParseComposition(authoredJson);
        CompositionFilePublisher.PublishAuthored(
            outputPath,
            hostDirectory,
            System.Text.Encoding.UTF8.GetBytes(authoredJson),
            source.VerifyUnchanged,
            cancellationToken);
        Console.Out.WriteLine("Authored composition accepted.");
        return ToolExitCode.Success;
    }

    private static void RequireSafe(string value)
    {
        if (!SelectionValueRules.IsSafeReference(value))
            throw CliRefusal.Usage("bridge-source-invalid", "The source produced an unsafe preview identity.");
    }

    private static string ReadInput(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw CliRefusal.Resolution("bridge-source-unreadable", "A required local input file could not be read.");
        }
    }

    private static int Guarded(Func<int> action)
    {
        try
        {
            return action();
        }
        catch (CliRefusal refusal)
        {
            Report.WriteRefusal(Console.Error, refusal.Code, refusal.Message, refusal.Details);
            return refusal.ExitCode;
        }
        catch (CshellsSourceException refusal)
        {
            Report.WriteRefusal(Console.Error, refusal.Code, "The selected CShells source cannot be imported.", []);
            return ToolExitCode.Refusal;
        }
        catch (CompositionImportException refusal)
        {
            Report.WriteRefusal(Console.Error, refusal.Code, "The selected local composition cannot be imported safely.", []);
            return ToolExitCode.Refusal;
        }
        catch (SettingReviewException refusal)
        {
            Report.WriteRefusal(Console.Error, refusal.Code, "The local setting review cannot be used.", []);
            return ToolExitCode.Refusal;
        }
        catch (SelectionDocumentException)
        {
            Report.WriteRefusal(Console.Error, "bridge-source-invalid", "The supplied catalog or authored projection is invalid.", []);
            return ToolExitCode.Refusal;
        }
        catch (OperationCanceledException)
        {
            Report.WriteRefusal(Console.Error, "bridge-review-required", "The composition preview was not accepted.", []);
            return ToolExitCode.Refusal;
        }
        catch (Exception)
        {
            Report.WriteRefusal(Console.Error, "bridge-output-failed", "The composition could not be imported.", []);
            return ToolExitCode.ResolutionFailure;
        }
    }
}
