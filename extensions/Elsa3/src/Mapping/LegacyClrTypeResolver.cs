using Elsa.Serialization.Core;

namespace Elsa3.Mapping;

/// <summary>
/// Resolves an Elsa-3 legacy type token (a well-known alias OR a raw CLR type name) to a CLR <see cref="Type"/>,
/// at the import boundary only. Elsa-3 stored type identities as alias-or-CLR-name; the importer must interpret
/// that legacy format, then the caller normalises the result to an Elsa-4 alias (via the shared
/// <c>TypeAliasConvention</c>) for storage — so nothing CLR-named is ever persisted (FR-004).
/// </summary>
/// <remarks>
/// This is the one sanctioned place for legacy <see cref="Type.GetType(string)"/> resolution: it is a one-time
/// migration translation of legacy input, NOT a runtime type-token resolver. The runtime registry stays
/// fallback-free (FR-004a). Order: registered alias first, then legacy CLR-name resolution, then
/// <see cref="object"/> when the type's assembly is not present.
/// </remarks>
public static class LegacyClrTypeResolver
{
    public static Type Resolve(IWellKnownTypeRegistry wellKnownTypeRegistry, string legacyTypeToken) =>
        wellKnownTypeRegistry.TryGetTypeOrDefault(legacyTypeToken, out var type)
            ? type
            : Type.GetType(legacyTypeToken) ?? typeof(object);
}
