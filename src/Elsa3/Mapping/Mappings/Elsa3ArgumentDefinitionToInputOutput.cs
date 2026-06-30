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
       MapTypeReference(input.Type) ?? throw new ArgumentException("Input object does not have the required 'type' property"),
       MapAlias(input.StorageDriverType),
       $"{input.DisplayName}",
       input.Category,
       Description: input.Description,
       UiHint: input.UiHint
   );

    public OutputDefinition MapOutput(Elsa3WorkflowArgumentDefinition input) => new(
       input.Name,
       input.Name,
       MapTypeReference(input.Type) ?? throw new ArgumentException("Input object does not have the required 'type' property"),
       MapAlias(input.StorageDriverType),
       $"{input.DisplayName}",
       input.Category,
       Description: input.Description,
       UiHint: input.UiHint
   );

    private TypeReference? MapTypeReference(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var type = LegacyClrTypeResolver.Resolve(wellKnownTypeRegistry, value);
        return TypeReferenceFactory.FromClrType(type, wellKnownTypeRegistry.GetAliasOrDefault);
    }

    private string? MapAlias(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var type = LegacyClrTypeResolver.Resolve(wellKnownTypeRegistry, value);
        return wellKnownTypeRegistry.GetAliasOrDefault(type);
    }
}
