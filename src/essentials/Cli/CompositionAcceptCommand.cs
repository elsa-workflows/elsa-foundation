using System.CommandLine;
using Elsa.Cli.Worker;
using Elsa.Modularity.Planning.Catalog;
using Elsa.Modularity.Planning.Json;
using static Elsa.Cli.CompositionFileBridgeOutput;

namespace Elsa.Cli;

/// <summary>Reviews edited selection intent and writes a fresh accepted composition.</summary>
internal static class CompositionAcceptCommand
{
    public static Command Build()
    {
        var composition = Required("--composition", "Edited authored composition JSON.");
        var output = Required("--output", "Fresh accepted composition file.");
        var catalog = new Option<string>("--catalog") { Description = "Optional pinned selection catalog JSON. Defaults to the bundled Foundation catalog." };
        var workspaceProfiles = new Option<string[]>("--workspace-profile") { Description = "Optional workspace-profile JSON file. Repeatable." };
        var command = new Command("accept", "Review and accept the exact expansion of an edited composition.")
        {
            composition, output, catalog, workspaceProfiles
        };

        command.SetAction((result, cancellationToken) => Task.FromResult(Guarded(() => Run(
            result.GetRequiredValue(composition),
            result.GetRequiredValue(output),
            result.GetValue(catalog),
            result.GetValue(workspaceProfiles) ?? [],
            cancellationToken), "accepted")));
        return command;
    }

    private static int Run(
        string compositionPath,
        string outputPath,
        string? catalogPath,
        IReadOnlyList<string> workspaceProfilePaths,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var suppliedPaths = new List<string> { compositionPath };
        if (catalogPath is not null)
            suppliedPaths.Add(catalogPath);
        suppliedPaths.AddRange(workspaceProfilePaths);
        var inputs = CompositionInputSnapshot.Open(suppliedPaths);

        var authored = SelectionJsonReader.ParseComposition(inputs.ReadText(compositionPath));
        var catalog = catalogPath is null
            ? FoundationSelectionCatalog.LoadFor(authored.Catalog)
            : SelectionJsonReader.ParseCatalog(inputs.ReadText(catalogPath));
        var profiles = workspaceProfilePaths
            .Select(path => SelectionJsonReader.ParseWorkspaceProfile(inputs.ReadText(path)))
            .ToArray();
        cancellationToken.ThrowIfCancellationRequested();

        var plan = CompositionAcceptance.Resolve(catalog, authored, profiles);

        var previewJson = CompositionAcceptance.Preview(plan, authored).GetRawText();
        if (Console.IsInputRedirected)
            throw CliRefusal.Usage("composition-accept-review-required", "An interactive acceptance review is required.");

        Console.Out.WriteLine(previewJson);
        Console.Error.Write("Type accept to write the accepted composition: ");
        if (!string.Equals(Console.ReadLine(), "accept", StringComparison.Ordinal))
            throw CliRefusal.Usage("composition-accept-review-required", "The composition was not accepted.");

        var outputBytes = CompositionAcceptance.WriteAccepted(inputs.ReadText(compositionPath), plan);
        cancellationToken.ThrowIfCancellationRequested();
        CompositionFilePublisher.PublishReviewedAuthored(outputPath, outputBytes, inputs.VerifyUnchanged, cancellationToken);
        Console.Out.WriteLine("Accepted composition written.");
        return ToolExitCode.Success;
    }
}
