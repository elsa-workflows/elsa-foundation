using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Models;
using Elsa3.Mapping.Conversion;

namespace Elsa3.Mapping.Mappings;

/// <summary>
/// Maps a normalized and lowered Elsa 3 definition to an Elsa-4 <see cref="WorkflowDefinitionState"/>. Reached through
/// <see cref="IElsa3DefinitionConverter"/>.
/// </summary>
public sealed class Elsa3WorkflowDefinitionToState(
    IWellKnownTypeRegistry wellKnownTypeRegistry,
    IObjectConverter objectConverter,
    Elsa3ActivityToState activityMapper,
    Elsa3ArgumentDefinitionToInputOutput argumentMapper
)
{
    internal WorkflowDefinitionState Map(Elsa3ConversionPlan plan)
    {
        var definition = plan.Definition;
        var variables = definition.Variables.Select(variable => Elsa3VariableMapper.Map(wellKnownTypeRegistry, objectConverter, variable)).ToList();
        var rootActivity = definition.Root is null ? null : activityMapper.Map(plan, definition.Root);
        var inputs = (definition.Inputs ?? []).Select(argumentMapper.MapInput).ToList();
        var outputs = (definition.Outputs ?? []).Select(argumentMapper.MapOutput).ToList();
        return new(
            variables,
            rootActivity!,
            inputs,
            outputs,
            StrategyOptions: null
        );
    }
}
