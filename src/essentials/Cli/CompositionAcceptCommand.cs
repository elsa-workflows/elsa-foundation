using System.CommandLine;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Cli.Worker;
using Elsa.Modularity.Planning.Catalog;
using Elsa.Modularity.Planning.Json;
using Elsa.Modularity.Planning.Models;
using Elsa.Modularity.Planning.Services;
using static Elsa.Cli.CompositionFileBridgeOutput;

namespace Elsa.Cli;

/// <summary>Reviews edited selection intent and writes a fresh accepted composition.</summary>
internal static class CompositionAcceptCommand
{
    private static readonly JsonSerializerOptions s_json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static readonly HashSet<string> s_blockingFindings = new(StringComparer.Ordinal)
    {
        "catalog-pin-unresolved",
        "accepted-pin-unresolved",
        "definition-pin-unresolved",
        "required-dependency-missing"
    };

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

        var plan = SelectionPlanner.Plan(catalog, authored, workspaceProfiles: profiles);
        ValidateFindings(plan);

        var preview = Preview(plan, authored);
        var previewJson = JsonSerializer.Serialize(preview, s_json);
        if (Console.IsInputRedirected)
            throw CliRefusal.Usage("composition-accept-review-required", "An interactive acceptance review is required.");

        Console.Out.WriteLine(previewJson);
        Console.Error.Write("Type accept to write the accepted composition: ");
        if (!string.Equals(Console.ReadLine(), "accept", StringComparison.Ordinal))
            throw CliRefusal.Usage("composition-accept-review-required", "The composition was not accepted.");

        var retainedIds = plan.SelectedFeatureIds.ToHashSet(StringComparer.Ordinal);
        var compositionJson = JsonNode.Parse(inputs.ReadText(compositionPath))!.AsObject();
        var acceptedJson = compositionJson["accepted"]!.AsObject();
        acceptedJson["featureIds"] = JsonNode.Parse(JsonSerializer.Serialize(plan.SelectedFeatureIds, s_json));
        var originalLocks = acceptedJson["locks"]!.AsArray();
        var retainedLocks = new JsonArray();
        foreach (var lockJson in originalLocks)
        {
            var featureId = lockJson!["featureId"]!.GetValue<string>();
            if (retainedIds.Contains(featureId))
                retainedLocks.Add(lockJson.DeepClone());
        }
        acceptedJson["locks"] = retainedLocks;
        var outputBytes = Encoding.UTF8.GetBytes(compositionJson.ToJsonString(s_json) + Environment.NewLine);
        _ = SelectionJsonReader.ParseComposition(Encoding.UTF8.GetString(outputBytes));
        cancellationToken.ThrowIfCancellationRequested();
        CompositionFilePublisher.PublishReviewedAuthored(outputPath, outputBytes, inputs.VerifyUnchanged, cancellationToken);
        Console.Out.WriteLine("Accepted composition written.");
        return ToolExitCode.Success;
    }

    private static object Preview(SelectionPlan plan, AuthoredComposition authored)
    {
        var candidate = plan.SelectedFeatureIds.Order(StringComparer.Ordinal).ToArray();
        var accepted = authored.Accepted.FeatureIds.Order(StringComparer.Ordinal).ToArray();
        var candidateSet = candidate.ToHashSet(StringComparer.Ordinal);
        var acceptedSet = accepted.ToHashSet(StringComparer.Ordinal);
        foreach (var id in candidate.Concat(accepted)
                     .Concat(authored.Add)
                     .Concat(authored.Remove)
                     .Concat(authored.Accepted.Locks.Select(item => item.FeatureId)))
            RequireSafe(id);

        return new
        {
            CandidateFeatureIds = candidate,
            AcceptedFeatureIds = accepted,
            AddedFeatureIds = candidate.Where(id => !acceptedSet.Contains(id)).ToArray(),
            RemovedFeatureIds = accepted.Where(id => !candidateSet.Contains(id)).ToArray(),
            RetainedLockFeatureIds = authored.Accepted.Locks.Where(item => candidateSet.Contains(item.FeatureId))
                .Select(item => item.FeatureId).Order(StringComparer.Ordinal).ToArray(),
            DroppedLockFeatureIds = authored.Accepted.Locks.Where(item => !candidateSet.Contains(item.FeatureId))
                .Select(item => item.FeatureId).Order(StringComparer.Ordinal).ToArray(),
            Findings = SafeFindings(plan)
        };
    }

    private static void ValidateFindings(SelectionPlan plan)
    {
        if (plan.Findings.Any(finding => finding.Severity == "unresolved" && s_blockingFindings.Contains(finding.Code)))
            throw CliRefusal.Resolution("composition-accept-blocked", "The supplied catalog, accepted digest, definitions and required dependencies must resolve before accepting this composition.");

        foreach (var finding in plan.Findings)
        {
            var supported = finding.Code switch
            {
                "candidate-re-resolution" => finding.Severity == "advisory",
                "inventory-unverified" or "persistence-unverified" => finding.Severity == "unresolved",
                _ => false
            };
            if (!supported)
                throw CliRefusal.Resolution("composition-accept-findings-unsupported", "The planner produced a finding that this accept command does not recognize.");
        }
    }
}
