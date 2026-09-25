using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Cli.Worker;
using Elsa.Modularity.Planning.Bridge;
using Elsa.Modularity.Planning.Json;
using static Elsa.Cli.CompositionFileBridgeOutput;

namespace Elsa.Cli;

/// <summary>Reviews a source-preserving local host-file candidate before publishing it.</summary>
internal static class CompositionGenerateCommand
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
        var composition = Required("--composition", "Accepted authored composition JSON.");
        var review = new Option<string>("--setting-review") { Description = "Optional local setting safety review JSON." };
        var output = Required("--output-dir", "Fresh candidate host-file directory.");
        var command = new Command("generate", "Review and publish a fresh candidate host-file directory.")
        {
            host, shell, environment, catalog, composition, review, output
        };

        command.SetAction((result, cancellationToken) => Task.FromResult(Guarded(() => Run(
            result.GetRequiredValue(host),
            result.GetRequiredValue(shell),
            result.GetRequiredValue(environment),
            result.GetRequiredValue(catalog),
            result.GetRequiredValue(composition),
            result.GetValue(review),
            result.GetRequiredValue(output),
            cancellationToken), "generated")));
        return command;
    }

    private static int Run(
        string hostDirectory,
        string shellId,
        string environment,
        string catalogPath,
        string compositionPath,
        string? reviewPath,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var source = CompositionFileSource.Open(hostDirectory, shellId, environment);
        var catalog = SelectionJsonReader.ParseCatalog(ReadInput(catalogPath));
        var authored = SelectionJsonReader.ParseComposition(ReadInput(compositionPath));
        var settingReview = reviewPath is null ? null : SettingReviewReader.Parse(ReadInput(reviewPath));
        var candidate = CompositionCandidateBuilder.Build(source.Snapshot, catalog, authored, settingReview);

        var findings = SafeFindings(candidate.Plan);
        Console.Out.WriteLine(JsonSerializer.Serialize(new { Changes = SafeChanges(candidate.Changes).ToArray(), Findings = findings }, s_json));

        if (Console.IsInputRedirected)
            throw CliRefusal.Usage("bridge-review-required", "An interactive diff approval is required.");

        Console.Error.Write("Type generate to write the candidate: ");
        if (!string.Equals(Console.ReadLine(), "generate", StringComparison.Ordinal))
            throw CliRefusal.Usage("bridge-review-required", "The candidate diff was not approved.");

        cancellationToken.ThrowIfCancellationRequested();
        CompositionFilePublisher.PublishCandidate(
            outputDirectory,
            hostDirectory,
            candidate.Files,
            source.VerifyUnchanged,
            cancellationToken);
        Console.Out.WriteLine("Candidate host files generated.");
        return ToolExitCode.Success;
    }

}
