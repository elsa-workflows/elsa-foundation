using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa3.Mapping.Conversion;
using Elsa3.Models;

namespace Elsa3.Mapping.Mappings;

/// <summary>
/// Converts an Elsa-3 workflow argument definition to an Elsa-4 input/output definition. Every Elsa 3 storage driver
/// becomes Elsa 4's default storage (no storage driver, so elsa.json); conversion reports a Memory or custom driver as a
/// behaviour warning (<see cref="Elsa3StorageDriverCorrections"/>).
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
            StorageDriverType: null,
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
            StorageDriverType: null,
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
