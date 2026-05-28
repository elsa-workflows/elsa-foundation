using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Mapping.Core.Contracts;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Models;
using Elsa3.Models;
using ArgumentValue = Elsa.Expressions.Core.Models.ArgumentValue;

namespace Elsa3.Mapping.Mappings;

public sealed class Elsa3WorkflowDefinitionToState(
    IWellKnownTypeRegistry wellKnownTypeRegistry,
    IObjectMapping<Elsa3Activity, ActivityNode> activityMapper,
    IObjectMapping<Elsa3WorkflowArgumentDefinition, InputDefinition> inputMapper,
    IObjectMapping<Elsa3WorkflowArgumentDefinition, OutputDefinition> outputMapper
)

    : IObjectMapping<Elsa3WorkflowDefinition, WorkflowDefinitionState>

{
    public async ValueTask<WorkflowDefinitionState> Map(Elsa3WorkflowDefinition definition, CancellationToken cancellationToken)
    {
        var variables = definition.Variables.Select(MapVariable);
        var activityConnections = definition.Root.Connections?.Select(MapConnection) ?? [];
        var activities = await MapActivities(definition, cancellationToken);
        var inputs = await MapInputs(definition, cancellationToken);
        var outputs = await MapOutputs(definition, cancellationToken);
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
    private async ValueTask<IEnumerable<InputDefinition>> MapInputs(Elsa3WorkflowDefinition definition, CancellationToken cancellationToken)
    {
        var result = new List<InputDefinition>(definition.Inputs?.Count ?? 0);
        foreach (var input in definition.Inputs ?? [])
        {
            result.Add(
                await inputMapper.Map(input, cancellationToken)
            );
        }
        return result;
    }

    private async ValueTask<IEnumerable<OutputDefinition>> MapOutputs(Elsa3WorkflowDefinition definition, CancellationToken cancellationToken)
    {
        var result = new List<OutputDefinition>(definition.Inputs?.Count ?? 0);
        foreach (var input in definition.Outputs ?? [])
        {
            result.Add(
                await outputMapper.Map(input, cancellationToken)
            );
        }
        return result;
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
