using Elsa.Expressions.Core.Contracts;
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
    private readonly IVariableTypeDescriptorCatalog? _typeDescriptors;
    public Elsa3WorkflowDefinitionToState(IWellKnownTypeRegistry wellKnownTypeRegistry, IObjectConverter objectConverter,
        Elsa3ActivityToState activityMapper, Elsa3ArgumentDefinitionToInputOutput argumentMapper, IVariableTypeDescriptorCatalog typeDescriptors)
        : this(wellKnownTypeRegistry, objectConverter, activityMapper, argumentMapper) => _typeDescriptors = typeDescriptors;

    internal WorkflowDefinitionState Map(Elsa3ConversionPlan plan)
    {
        var definition = plan.Definition;
        var variables = definition.Variables.Select(variable => plan.WorkflowVariables.TryGetValue(variable, out var mapped)
            ? mapped : Elsa3VariableMapper.Map(wellKnownTypeRegistry, objectConverter, variable, _typeDescriptors)).ToList();
        var rootActivity = definition.Root is null ? null : activityMapper.Map(plan, definition.Root);
        var inputs = (definition.Inputs ?? []).Select(argumentMapper.MapInput).ToList();
        var outputs = (definition.Outputs ?? []).Select(argumentMapper.MapOutput).ToList();
        return new(
            variables,
            rootActivity!,
            inputs,
            outputs,
            StrategyOptions: plan.ValuePolicies.DefaultOnNull || plan.ValuePolicies.UndefinedAsNull
                ? new WorkflowStrategyOptions { NullHandling = new WorkflowNullHandling { DefaultOnNull = plan.ValuePolicies.DefaultOnNull, UndefinedAsNull = plan.ValuePolicies.UndefinedAsNull } }
                : null
        );
    }
}
