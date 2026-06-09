using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa3.Models;

namespace Elsa3.Mapping.Mappings;

/// <summary>Converts an Elsa-3 workflow argument definition to an Elsa-4 input/output definition.</summary>
public sealed class Elsa3ArgumentDefinitionToInputOutput(IWellKnownTypeRegistry wellKnownTypeRegistry)
{
    public InputDefinition MapInput(Elsa3WorkflowArgumentDefinition input) => new(
       input.Name,
       input.Name,
       MapTypeInfo(input.Type) ?? throw new ArgumentException("Input object does not have the required 'type' property"),
       MapTypeInfo(input.StorageDriverType),
       $"{input.DisplayName}",
       input.Category,
       Description: input.Description,
       UiHint: input.UiHint
   );

    public OutputDefinition MapOutput(Elsa3WorkflowArgumentDefinition input) => new(
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
