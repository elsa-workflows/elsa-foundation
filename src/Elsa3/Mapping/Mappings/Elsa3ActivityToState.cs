using Elsa.Activities.Design.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa3.Models;
using System.Text.Json;
using ArgumentState = Elsa.Workflows.Design.Core.Models.ArgumentState;
using ArgumentValue = Elsa.Expressions.Core.Models.ArgumentValue;

namespace Elsa3.Mapping.Mappings;

/// <summary>Converts an Elsa-3 activity to an Elsa-4 <see cref="ActivityNode"/>.</summary>
public sealed class Elsa3ActivityToState(IActivityDefinitionLookup activityLookup)
{
    private const string ImportedStructureKind = "elsa3.imported-activity.structure";
    private const string ImportedStructureSchemaVersion = "1.0.0";

    public async ValueTask<ActivityNode> Map(Elsa3Activity source, CancellationToken cancellationToken)
    {
        var version = await GetVersion(source, cancellationToken);

        var inputs = new List<ArgumentState>();
        var outputs = new List<ArgumentState>();
        ExtractInputsAndOutputs(inputs, outputs, version, source.AdditionalProperties);

        var childActivities = (await GetChildActivities(source, cancellationToken)).ToArray();
        if (source.Connections?.Any() == true)
            throw new NotSupportedException("Elsa 3 activity graph connections require a Flowchart-owned importer module.");

        var structure = childActivities.Length == 0
            ? null
            : new ActivityNodeStructure(
                ImportedStructureKind,
                ImportedStructureSchemaVersion,
                JsonSerializer.SerializeToElement(new { activities = childActivities }));

        return new ActivityNode(
            source.NodeId,
            version.Id,                 // FR-011: single ActivityVersionId : string (Unit B catalog row id)
            inputs,
            outputs,
            structure
        );
        // NOTE (Unit C, 2026-05-28): Elsa3 per-activity designer position/size in
        // source.Metadata.Designer is no longer carried into ActivityNode — display metadata
        // now lives on WorkflowDefinitionVersionLayout sibling as DesignMetadataRecord (§E2.9.2).
        // Wiring the importer to populate the layout sibling alongside the version is a
        // separate task — flagged in the Unit C follow-up as Elsa3-import layout-carryover.
    }

    private async ValueTask<IEnumerable<ActivityNode>> GetChildActivities(Elsa3Activity source, CancellationToken cancellationToken)
    {
        var result = new List<ActivityNode>();
        foreach (var activity in source?.Activities ?? [])
        {
            var child = await Map(activity, cancellationToken);
            result.Add(child);
        }

        return result;
    }

    private static void ExtractInputsAndOutputs(
        List<ArgumentState> inputs,
        List<ArgumentState> outputs,
        IActivityDefinitionVersion version,
        IDictionary<string, JsonElement> properties
    )
    {
        foreach (var (propertyName, value) in properties)
        {
            var parsed = TryGetArgument(propertyName, value, out var argument);

            // Skip only properties that carry a value but do not parse as an argument. Null-valued
            // properties fall through and are recorded as ArgumentState.Null below. (The original
            // guard used `||`, which — by De Morgan's law — skipped every property and made this
            // whole loop body dead code, silently dropping all imported inputs/outputs. See #378.)
            if (value.ValueKind != JsonValueKind.Null && !parsed)
            {
                continue;
            }

            var isInput = version.Inputs.Any(i => i.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase));
            var isOutput = version.Outputs.Any(i => i.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase));

            if (isInput && value.ValueKind != JsonValueKind.Null)
                inputs.Add(argument);
            else if (isInput)
                inputs.Add(ArgumentState.Null(propertyName));

            else if (isOutput && value.ValueKind != JsonValueKind.Null)
                outputs.Add(argument);
            else if (isOutput)
                outputs.Add(ArgumentState.Null(propertyName));
        }
    }

    private async Task<IActivityDefinitionVersion> GetVersion(Elsa3Activity source, CancellationToken cancellationToken)
    {
        var activity = await activityLookup.GetDefinition(source.Type, cancellationToken);
        var versions = await activityLookup.ListVersions(activity.Id, cancellationToken);
        // Elsa-3 carries an int version; map it onto a semver string (FR-007: n → "n.0.0").
        var sourceSemVer = $"{source.Version ?? 0}.0.0";
        var version = versions.FirstOrDefault(x => x.Version == sourceSemVer)
            ?? throw new ArgumentException($"Activity '{source.Type}' does not have version '{sourceSemVer}'");

        return await activityLookup.GetVersion(version.Id, cancellationToken);
    }

    private static bool TryGetArgument(string objectKey, JsonElement jsonElement, out ArgumentState argument)
    {
        argument = null!;

        if (jsonElement.ValueKind != JsonValueKind.Object)
            return false;
        if (!jsonElement.TryGetProperty("expression", out var expression))
            return false;
        if (expression.ValueKind is not JsonValueKind.Object and not JsonValueKind.Null)
            return false;
        if (!expression.TryGetProperty("type", out var expressionType) || expressionType.ValueKind != JsonValueKind.String)
            return false;
        if (!expression.TryGetProperty("value", out var expressionValue) ||
            expressionValue.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return false;

        object value = expressionValue.ValueKind == JsonValueKind.String
            ? expressionValue.GetString()!
            : expressionValue.Clone();

        argument = new ArgumentState(
            objectKey,
            new ArgumentValue(value, expressionType.GetString()),
            null,
            null,
            null,
            null
        );

        return true;
    }
}
