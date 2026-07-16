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
    public ValueTask<WorkflowDefinitionState> Map(Elsa3WorkflowDefinition definition, CancellationToken cancellationToken) =>
        Map(definition, new Dictionary<string, Elsa3ActivityExactReplacement>(StringComparer.Ordinal), cancellationToken);

    public async ValueTask<WorkflowDefinitionState> Map(
        Elsa3WorkflowDefinition definition,
        IReadOnlyDictionary<string, Elsa3ActivityExactReplacement> replacements,
        CancellationToken cancellationToken)
    {
        var variables = definition.Variables.Select(MapVariable);
        var rootActivity = await activityMapper.Map(definition.Root, replacements, cancellationToken);
        var inputs = (definition.Inputs ?? []).Select(argumentMapper.MapInput).ToList();
        var outputs = (definition.Outputs ?? []).Select(argumentMapper.MapOutput).ToList();
        return new(
            variables,
            rootActivity,
            inputs,
            outputs,
            StrategyOptions: null
        );
    }

    private VariableDefinition MapVariable(Elsa3Variable source)
    {
        var varType = TypeReferenceFactory.FromClrType(LegacyClrTypeResolver.Resolve(wellKnownTypeRegistry, source.TypeName), wellKnownTypeRegistry.GetAliasOrDefault);

        var storageDriverType = !string.IsNullOrWhiteSpace(source.StorageDriverTypeName)
            ? wellKnownTypeRegistry.GetAliasOrDefault(LegacyClrTypeResolver.Resolve(wellKnownTypeRegistry, source.StorageDriverTypeName))
            : null;

        return new VariableDefinition(source.Id, source.Name, varType, storageDriverType, new ArgumentValue(source.Value));
    }

}
