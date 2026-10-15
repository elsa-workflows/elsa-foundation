using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa3.Models;

namespace Elsa3.Mapping.Conversion;

/// <summary>Maps an Elsa 3 variable declaration to an Elsa 4 <see cref="VariableDefinition"/>.</summary>
internal static class Elsa3VariableMapper
{
    public static VariableDefinition Map(IWellKnownTypeRegistry wellKnownTypeRegistry, Elsa3Variable source)
    {
        var type = TypeReferenceFactory.FromClrType(
            LegacyClrTypeResolver.Resolve(wellKnownTypeRegistry, source.TypeName ?? "Object"),
            wellKnownTypeRegistry.GetAliasOrDefault);
        if (source.IsArray && type.CollectionKind == CollectionKind.Single)
            type = type with { CollectionKind = CollectionKind.Array };

        var storageDriverType = !string.IsNullOrWhiteSpace(source.StorageDriverTypeName)
            ? wellKnownTypeRegistry.GetAliasOrDefault(LegacyClrTypeResolver.Resolve(wellKnownTypeRegistry, source.StorageDriverTypeName))
            : null;

        return new VariableDefinition(source.Id, source.Name, type, storageDriverType, new ArgumentValue(source.Value));
    }
}
