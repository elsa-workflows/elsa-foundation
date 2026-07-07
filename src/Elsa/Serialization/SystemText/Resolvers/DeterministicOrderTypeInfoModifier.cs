using System.Text.Json.Serialization.Metadata;

namespace Elsa.Serialization.SystemText.Resolvers;

/// <summary>
/// A <see cref="DefaultJsonTypeInfoResolver"/> modifier that fixes object member order to a stable
/// ordinal-by-serialized-name ordering. Reflection member order (<c>Type.GetProperties()</c>) is not a
/// guaranteed contract, so equal graphs could otherwise serialize members in a host-dependent order.
/// Sorting the property list makes the member order — and therefore the bytes — deterministic across
/// processes and hosts (spec 086 FR-002/FR-003; ADR 0034 D3/D8).
/// </summary>
/// <remarks>
/// Property write order does not affect deserialization (matching is by name, case-insensitively), so this
/// is a pure byte-ordering normalization and stays round-trip lossless (FR-004). Dictionary key order is a
/// separate concern handled by <see cref="JsonConverters.DeterministicDictionaryConverterFactory"/> and the
/// polymorphic converters — the contract resolver cannot reorder dictionary entries.
/// </remarks>
public static class DeterministicOrderTypeInfoModifier
{
    /// <summary>
    /// Sorts the serialized members of every object contract by ordinal name.
    /// </summary>
    public static void SortObjectMembers(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object || typeInfo.Properties.Count < 2)
            return;

        var ordered = typeInfo.Properties
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToArray();

        typeInfo.Properties.Clear();
        foreach (var property in ordered)
            typeInfo.Properties.Add(property);
    }
}
