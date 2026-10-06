using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Modularity.Planning.Json;
using Elsa.Modularity.Planning.Models;
using Elsa.Modularity.Planning.Services;
using static Elsa.Cli.CompositionFileBridgeOutput;

namespace Elsa.Cli;

/// <summary>Resolves selection review and updates accepted features while preserving authored content.</summary>
internal static class CompositionAcceptance
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

    public static SelectionPlan Resolve(
        SelectionCatalog catalog,
        AuthoredComposition authored,
        IReadOnlyList<WorkspaceProfile> workspaceProfiles)
    {
        var plan = SelectionPlanner.Plan(catalog, authored, workspaceProfiles: workspaceProfiles);
        ValidateFindings(plan);
        return plan;
    }

    public static byte[] WriteAccepted(string authoredJson, SelectionPlan plan)
    {
        var retainedIds = plan.SelectedFeatureIds.ToHashSet(StringComparer.Ordinal);
        var compositionJson = JsonNode.Parse(authoredJson)!.AsObject();
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
        return outputBytes;
    }

    public static JsonElement Preview(SelectionPlan plan, AuthoredComposition authored)
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

        return JsonSerializer.SerializeToElement(new
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
        }, s_json);
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
