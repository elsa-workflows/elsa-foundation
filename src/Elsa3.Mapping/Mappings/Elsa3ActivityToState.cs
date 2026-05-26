using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Mapping.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa3.Models;
using System.Text.Json;

namespace Elsa3.Mapping.Mappings;

public sealed class Elsa3ActivityToState(IActivityDefinitionLookup activityLookup)
    : IObjectMapping<Elsa3Activity, ActivityNode>
{
    public async ValueTask<ActivityNode> Map(Elsa3Activity source, CancellationToken cancellationToken)
    {
        var version = await GetVersion(source, cancellationToken);

        var inputs = new List<ArgumentState>();
        var outputs = new List<ArgumentState>();
        ExtractInputsAndOutputs(inputs, outputs, version, source.AdditionalProperties);

        var childActivities = await GetChildActivities(source, cancellationToken);

        return new ActivityNode(
            source.NodeId,
            version.Definition.Id,
            version.Version,
            source.Name,
            inputs,
            outputs,
            IsContainer: false,
            IsStart: source.CustomProperties?.CanStartWorkflow ?? false,
            IsTerminal: false,
            DisplayInfo: MapMetaData(source.Metadata),
            ChildActivities: childActivities ?? []
        );
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

    private static ActivityDisplayInfo? MapMetaData(Elsa3ActivityMetadata? metaData)
    {
        if (metaData is null)
            return null;

        return new(
            metaData.Designer.Position.X,
            metaData.Designer.Position.Y,
            metaData.Designer.Size.Width,
            metaData.Designer.Size.Height
        );
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
            if (value.ValueKind != JsonValueKind.Null || !TryGetArgument(propertyName, value, out var argument))
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
        var version = versions.FirstOrDefault(x => x.Version == source.Version)
            ?? throw new ArgumentException($"Activity '{source.Type}' does not have version '{source.Version}'");

        return await activityLookup.GetVersion(version.Id, cancellationToken);
    }

    private static bool TryGetArgument(string objectKey, JsonElement jsonElement, out ArgumentState argument)
    {
        argument = null!;

        if (jsonElement.ValueKind != JsonValueKind.Object)
            return false;
        if (!jsonElement.TryGetProperty("typeName", out var typeName))
            return false;
        if (!jsonElement.TryGetProperty("expression", out var expression))
            return false;
        if (expression.ValueKind is not JsonValueKind.Object and not JsonValueKind.Null)
            return false;
        if (!expression.TryGetProperty("type", out var expressionType) || expressionType.ValueKind != JsonValueKind.String)
            return false;
        if (!expression.TryGetProperty("value", out var expressionValue) || expressionValue.ValueKind != JsonValueKind.String)
            return false;

        argument = new ArgumentState(
            objectKey,
            new ArgumentValue(expressionValue.GetString(), expressionType.GetString()),
            null,
            null,
            null,
            null
        );

        return true;
    }
}
