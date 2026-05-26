using Elsa.Activities.Design.Core.Models;
using Elsa.Mapping.Core.Contracts;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa3.Models;

namespace Elsa3.Mapping.Mappings;

public sealed class Elsa3ArgumentDefinitionToInputOutput(IWellKnownTypeRegistry wellKnownTypeRegistry)
    : IObjectMapping<Elsa3WorkflowArgumentDefinition, InputDefinition>, IObjectMapping<Elsa3WorkflowArgumentDefinition, OutputDefinition>
{
    public ValueTask<InputDefinition> Map(Elsa3WorkflowArgumentDefinition source, CancellationToken cancellationToken)
        => new(MapInput(source));

    ValueTask<OutputDefinition> IObjectMapping<Elsa3WorkflowArgumentDefinition, OutputDefinition>.Map(Elsa3WorkflowArgumentDefinition source, CancellationToken cancellationToken)
        => new(MapOutput(source));

    private InputDefinition MapInput(Elsa3WorkflowArgumentDefinition input) => new(
       input.Name,
       input.Name,
       MapTypeInfo(input.Type) ?? throw new ArgumentException("Input object does not have the required 'type' property"),
       MapTypeInfo(input.StorageDriverType),
       $"{input.DisplayName}",
       input.Category,
       Description: input.Description,
       UiHint: input.UiHint
   );

    private OutputDefinition MapOutput(Elsa3WorkflowArgumentDefinition input) => new(
       input.Name,
       input.Name,
       MapTypeInfo(input.Type) ?? throw new ArgumentException("Input object does not have the required 'type' property"),
       MapTypeInfo(input.StorageDriverType),
       $"{input.DisplayName}",
       input.Category,
       Description: input.Description,
       UiHint: input.UiHint
   );

    private TypeInformation? MapTypeInfo(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var type = wellKnownTypeRegistry.GetTypeOrDefault(value);
        return TypeInformation.FromType(type);
    }
}
