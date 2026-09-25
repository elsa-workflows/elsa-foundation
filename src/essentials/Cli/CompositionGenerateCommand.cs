using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Cli.Worker;
using Elsa.Modularity.Planning.Catalog;
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
    private static readonly JsonSerializerOptions s_handoffJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static Command Build()
    {
        var host = Required("--host-dir", "Local Workbench-style host directory.");
        var shell = Required("--shell", "Selected shell ID.");
        var environment = Required("--environment", "Selected environment name.");
        var catalog = new Option<string>("--catalog") { Description = "Optional pinned selection catalog JSON. Defaults to the bundled Foundation catalog." };
        var composition = Required("--composition", "Accepted authored composition JSON.");
        var review = new Option<string>("--setting-review") { Description = "Optional local setting safety review JSON." };
        var output = Required("--output-dir", "Fresh candidate host-file directory.");
        var handoffHost = new Option<string>("--handoff-host") { Description = "Safe host alias for an optional candidate handoff. Does not deploy or activate." };
        var command = new Command("generate", "Review and publish a fresh candidate host-file directory.")
        {
            host, shell, environment, catalog, composition, review, output, handoffHost
        };

        command.SetAction((result, cancellationToken) => Task.FromResult(Guarded(() => Run(
            result.GetRequiredValue(host),
            result.GetRequiredValue(shell),
            result.GetRequiredValue(environment),
            result.GetValue(catalog),
            result.GetRequiredValue(composition),
            result.GetValue(review),
            result.GetRequiredValue(output),
            result.GetValue(handoffHost),
            cancellationToken), "generated")));
        return command;
    }

    private static int Run(
        string hostDirectory,
        string shellId,
        string environment,
        string? catalogPath,
        string compositionPath,
        string? reviewPath,
        string outputDirectory,
        string? handoffHost,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var source = CompositionFileSource.Open(hostDirectory, shellId, environment);
        var catalog = catalogPath is null
            ? FoundationSelectionCatalog.Load()
            : SelectionJsonReader.ParseCatalog(ReadInput(catalogPath));
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

        var handoff = handoffHost is null ? null : CompositionHandoff.Create(
            handoffHost,
            source.Snapshot,
            authored.Catalog,
            authored.Accepted.FeatureIds,
            candidate.Files,
            candidate.Plan.Findings.Select(finding => finding.Code));
        cancellationToken.ThrowIfCancellationRequested();
        CompositionFilePublisher.PublishCandidate(
            outputDirectory,
            hostDirectory,
            candidate.Files,
            source.VerifyUnchanged,
            cancellationToken);
        if (handoff is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            VerifyHandoffSource(source);
            CompositionHandoffFileVerifier.Verify(outputDirectory, candidate.Files);
            VerifyHandoffSource(source);
            cancellationToken.ThrowIfCancellationRequested();
            Console.Out.WriteLine(JsonSerializer.Serialize(new { Handoff = handoff }, s_handoffJson));
        }
        Console.Out.WriteLine("Candidate host files generated.");
        return ToolExitCode.Success;
    }

    private static void VerifyHandoffSource(CompositionFileSource source)
    {
        try
        {
            source.VerifyUnchanged();
        }
        catch (CliRefusal refusal) when (refusal.Code == "bridge-source-changed")
        {
            throw CliRefusal.Resolution("candidate-changed", "The candidate source changed after review; generate a fresh candidate.");
        }
    }
}
