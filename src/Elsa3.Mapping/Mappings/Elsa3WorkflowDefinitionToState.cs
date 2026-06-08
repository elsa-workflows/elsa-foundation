using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Models;
using Elsa3.Models;
using ArgumentValue = Elsa.Expressions.Core.Models.ArgumentValue;

namespace Elsa3.Mapping.Mappings;

/// <summary>Converts an Elsa-3 workflow definition to an Elsa-4 <see cref="WorkflowDefinitionState"/>.</summary>
public sealed class Elsa3WorkflowDefinitionToState(
    IWellKnownTypeRegistry wellKnownTypeRegistry,
    Elsa3ActivityToState activityMapper,
    Elsa3ArgumentDefinitionToInputOutput argumentMapper
)
{
    public async ValueTask<WorkflowDefinitionState> Map(Elsa3WorkflowDefinition definition, CancellationToken cancellationToken)
    {
        var variables = definition.Variables.Select(MapVariable);
        var activityConnections = definition.Root.Connections?.Select(MapConnection) ?? [];
        var activities = await MapActivities(definition, cancellationToken);
        var inputs = (definition.Inputs ?? []).Select(argumentMapper.MapInput).ToList();
        var outputs = (definition.Outputs ?? []).Select(argumentMapper.MapOutput).ToList();
        var activityOptions = MapActivityOptions(definition);

        return new(
            variables,
            activityConnections,
            activities,
            inputs,
            outputs,
            activityOptions,
            StrategyOptions: null
        );
    }

    private async ValueTask<IEnumerable<ActivityNode>> MapActivities(Elsa3WorkflowDefinition definition, CancellationToken cancellationToken)
    {
        var result = new List<ActivityNode>(definition.Root.Activities?.Count ?? 0);
        foreach (var activity in definition.Root.Activities ?? [])
        {
            result.Add(
                await activityMapper.Map(activity, cancellationToken)
            );
        }
        return result;
    }

    private static WorkflowActivityOptions? MapActivityOptions(Elsa3WorkflowDefinition definition)
    {
        return new WorkflowActivityOptions(
            definition.Options.UsableAsActivity,
            definition.Options.AutoUpdateConsumingWorkflows,
            null,
            definition.Outcomes
        );
    }

    private VariableDefinition MapVariable(Elsa3Variable source)
    {
        var varType = TypeInformation.FromType(wellKnownTypeRegistry.GetTypeOrDefault(source.TypeName));

        var storageDriverType = !string.IsNullOrWhiteSpace(source.StorageDriverTypeName)
            ? TypeInformation.FromType(wellKnownTypeRegistry.GetTypeOrDefault(source.StorageDriverTypeName))
            : null;

        return new VariableDefinition(source.Id, source.Name, varType, storageDriverType, new ArgumentValue(source.Value));
    }

    private static ActivityConnection MapConnection(Elsa3Connection connection)
    {
        var source = MapPort(connection.Source);
        var target = MapPort(connection.Target);
        return new(source, target);
    }

    private static ActivityPortConnection MapPort(Elsa3Endpoint source) => new(source.Activity, source.Port);
}
