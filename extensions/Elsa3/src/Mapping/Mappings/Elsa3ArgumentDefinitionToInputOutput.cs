using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa3.Mapping.Conversion;
using Elsa3.Models;

namespace Elsa3.Mapping.Mappings;

/// <summary>
/// Converts an Elsa-3 workflow argument definition to an Elsa-4 input/output definition. Elsa 3's default storage driver
/// kept the value with the workflow instance, as Elsa 4 does without a storage driver. Any other driver keeps its Elsa 3
/// type's full name as its Elsa 4 key (#5683); conversion has already refused one Elsa 4 cannot use there.
/// </summary>
public sealed class Elsa3ArgumentDefinitionToInputOutput(IWellKnownTypeRegistry wellKnownTypeRegistry)
{
    public InputDefinition MapInput(Elsa3WorkflowArgumentDefinition input)
    {
        var type = ResolveType(input.Type) ?? throw new ArgumentException("Input object does not have the required 'type' property");
        return new(
            input.Name,
            input.Name,
            MapTypeReference(type),
            Elsa3VariableMapper.StorageDriverKey(input.StorageDriverType),
            $"{input.DisplayName}",
            input.Category,
            IsNullable: SupportsNull(type),
            IsRequired: false,
            Description: input.Description,
            UiHint: input.UiHint);
    }

    public OutputDefinition MapOutput(Elsa3WorkflowArgumentDefinition input)
    {
        var type = ResolveType(input.Type) ?? throw new ArgumentException("Input object does not have the required 'type' property");
        return new(
            input.Name,
            input.Name,
            MapTypeReference(type),
            Elsa3VariableMapper.StorageDriverKey(input.StorageDriverType),
            $"{input.DisplayName}",
            input.Category,
            IsNullable: SupportsNull(type),
            Description: input.Description,
            UiHint: input.UiHint);
    }

    private Type? ResolveType(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : LegacyClrTypeResolver.Resolve(wellKnownTypeRegistry, value);

    private TypeReference MapTypeReference(Type type) =>
        TypeReferenceFactory.FromClrType(type, wellKnownTypeRegistry.GetAliasOrDefault);

    private static bool SupportsNull(Type type) =>
        !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;
}
