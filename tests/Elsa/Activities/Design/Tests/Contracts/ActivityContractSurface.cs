using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Reconciliation.Core.Models;
using Elsa.Primitives.Models;

namespace Elsa.Activities.Design.Tests.Contracts;

/// <summary>
/// Deterministic projection of the CLR activity catalog produced by <c>ClrAssemblyScanner</c>, shaped for
/// snapshot comparison against a committed baseline (#1119). Only members that describe the *contract* an
/// author or a published workflow binds against are carried — inputs, outputs, outcome ports and the
/// container structure. Descriptive metadata that no consumer binds to (descriptions, display names,
/// provider/consumer schema stamps) is deliberately dropped so the baseline stays a contract guard rather
/// than a prose diff.
/// </summary>
/// <remarks>
/// <para>
/// Ordering is total and ordinal: activities by alias, inputs and outputs by reference key, outcome ports
/// by name. Declaration order is therefore <em>not</em> preserved. That is intentional — reordering
/// <c>[ActivityOutcome]</c> attributes is not a contract change, and sorting keeps a real add/remove from
/// being buried in a reshuffle.
/// </para>
/// <para>
/// The document is serialized through <see cref="JsonNode"/> with sorted object members so a round trip is
/// byte-stable regardless of the reflection order the scanner happened to walk members in.
/// </para>
/// </remarks>
public static class ActivityContractSurface
{
    /// <summary>The document schema id. Bump when the projection shape changes so a stale baseline fails loudly.</summary>
    public const string SchemaId = "elsa.activity-contract-surface/1";

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        // The committed baseline is read by humans in review; escaping '+' and '&' to \uXXXX would make a
        // version or a display string unreadable for no safety gain in a file that is never served.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>Projects scanner models into the normalized, fully ordered snapshot document.</summary>
    public static JsonObject Build(IEnumerable<ActivityVersionReconciliationModel> models)
    {
        var activities = new JsonArray();

        foreach (var model in models.OrderBy(Alias, StringComparer.Ordinal).ThenBy(model => model.ActivityTypeKey, StringComparer.Ordinal))
            activities.Add(BuildActivity(model));

        return new JsonObject
        {
            ["schema"] = SchemaId,
            ["activities"] = activities
        };
    }

    /// <summary>Renders a snapshot document to the exact text form committed as the baseline.</summary>
    public static string Serialize(JsonObject document) => document.ToJsonString(WriteOptions) + Environment.NewLine;

    /// <summary>
    /// Drops the SemVer build-metadata segment (<c>1.0.0+&lt;git-sha&gt;</c>). No activity declares an explicit
    /// <c>[Version]</c>, so the scanner falls back to the assembly's informational version, which the SDK
    /// stamps with the source revision id — a baseline that recorded it would churn on every single commit.
    /// Build metadata is excluded from SemVer precedence by definition, so dropping it loses no contract.
    /// </summary>
    private static string StripBuildMetadata(string version)
    {
        var plus = version.IndexOf('+');
        return plus < 0 ? version : version[..plus];
    }

    private static string Alias(ActivityVersionReconciliationModel model) =>
        model.Descriptor is ClrActivityDescriptor descriptor ? descriptor.TypeAlias : model.ActivityTypeKey;

    private static JsonObject BuildActivity(ActivityVersionReconciliationModel model)
    {
        var activity = new JsonObject
        {
            ["alias"] = Alias(model),
            ["activityTypeKey"] = model.ActivityTypeKey,
            ["version"] = StripBuildMetadata(model.Version),
            ["category"] = model.Category,
            ["executionType"] = model.ExecutionType.ToString(),
            ["inputs"] = BuildInputs(model),
            ["outputs"] = BuildOutputs(model)
        };

        var facets = model.DesignFacets.ToArray();
        activity["outcomes"] = BuildOutcomes(facets);
        activity["structure"] = BuildStructure(facets);
        return activity;
    }

    private static JsonArray BuildInputs(ActivityVersionReconciliationModel model)
    {
        var inputs = new JsonArray();

        foreach (var input in model.Inputs.OrderBy(input => input.ReferenceKey, StringComparer.Ordinal))
            inputs.Add(new JsonObject
            {
                ["key"] = input.ReferenceKey,
                ["name"] = input.Name,
                ["type"] = input.Type.Alias,
                ["collectionKind"] = input.Type.CollectionKind.ToString(),
                ["isRequired"] = input.IsRequired,
                ["isNullable"] = input.IsNullable,
                ["uiHint"] = input.UiHint,
                ["defaultValue"] = ToNode(input.DefaultValue),
                ["hasStaticDefault"] = input.HasStaticDefault,
                ["defaultSyntax"] = input.DefaultSyntax,
                ["uiSpecifications"] = ToNode(input.UISpecifications)
            });

        return inputs;
    }

    private static JsonArray BuildOutputs(ActivityVersionReconciliationModel model)
    {
        var outputs = new JsonArray();

        foreach (var output in model.Outputs.OrderBy(output => output.ReferenceKey, StringComparer.Ordinal))
            outputs.Add(new JsonObject
            {
                ["key"] = output.ReferenceKey,
                ["name"] = output.Name,
                ["type"] = output.Type.Alias,
                ["collectionKind"] = output.Type.CollectionKind.ToString(),
                ["isRequired"] = output.IsRequired,
                ["isNullable"] = output.IsNullable,
                ["sourceRepresentation"] = output.SourceRepresentation?.ToString()
            });

        return outputs;
    }

    /// <summary>
    /// Projects the <c>elsa.outcomes</c> facet. Static ports sort ordinally; the dynamic descriptor
    /// (<c>ActivityValueOutcomes</c>, e.g. <c>SendHttpRequest.ExpectedStatusCodes</c>) is carried verbatim —
    /// it is the declaration the publish compiler pins per-node status ports from, so losing it would hide
    /// exactly the gap class this guard exists for.
    /// </summary>
    private static JsonNode BuildOutcomes(IReadOnlyCollection<ActivityDesignFacet> facets)
    {
        var facet = facets.FirstOrDefault(facet => facet.Kind == "elsa.outcomes");
        var ports = new JsonArray();
        JsonNode? dynamicOutcomes = null;

        if (facet is not null)
        {
            var payload = JsonNode.Parse(facet.Payload.GetRawText())!.AsObject();

            if (payload["ports"] is JsonArray declared)
                foreach (var name in declared
                             .Select(port => port?["name"]?.GetValue<string>())
                             .Where(name => name is not null)
                             .OrderBy(name => name, StringComparer.Ordinal))
                    ports.Add(name);

            dynamicOutcomes = Sort(payload["dynamicOutcomes"]);
        }

        return new JsonObject
        {
            // No facet at all means the activity declares nothing and the studio applies its own "Done"
            // default. Record that as an explicit flag so "declared no outcomes" reads as a decision in the
            // baseline rather than an empty list that could equally mean "facet dropped".
            ["declared"] = facet is not null,
            ["ports"] = ports,
            ["dynamic"] = dynamicOutcomes
        };
    }

    private static JsonNode? BuildStructure(IReadOnlyCollection<ActivityDesignFacet> facets)
    {
        var facet = facets.FirstOrDefault(facet => facet.Kind != "elsa.outcomes");
        if (facet is null)
            return null;

        var payload = JsonNode.Parse(facet.Payload.GetRawText())!.AsObject();
        var slots = new JsonArray();

        if (payload["slots"] is JsonArray declared)
            foreach (var slot in declared
                         .OfType<JsonNode>()
                         .OrderBy(slot => slot["name"]?.GetValue<string>(), StringComparer.Ordinal))
                slots.Add(Sort(slot));

        return new JsonObject
        {
            ["kind"] = facet.Kind,
            ["schemaVersion"] = facet.SchemaVersion,
            ["mode"] = payload["mode"]?.DeepClone(),
            ["supportsScopedVariables"] = payload["supportsScopedVariables"]?.DeepClone(),
            ["slots"] = slots
        };
    }

    private static JsonNode? ToNode(JsonElement? element) =>
        element is { } value && value.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null)
            ? Sort(JsonNode.Parse(value.GetRawText()))
            : null;

    /// <summary>Recursively re-emits an arbitrary payload with object members in ordinal key order.</summary>
    private static JsonNode? Sort(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject o:
            {
                var sorted = new JsonObject();
                foreach (var (key, value) in o.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                    sorted[key] = Sort(value);
                return sorted;
            }
            case JsonArray a:
            {
                var copy = new JsonArray();
                foreach (var item in a)
                    copy.Add(Sort(item));
                return copy;
            }
            default:
                return node?.DeepClone();
        }
    }
}
