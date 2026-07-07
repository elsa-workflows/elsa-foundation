using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Serialization.SystemText.JsonConverters;

/// <summary>
/// Emits string-keyed dictionary entries in a stable ordinal-by-key order so equal maps serialize to
/// byte-identical JSON regardless of insertion order or per-process hash-seed randomization of
/// <see cref="string"/> iteration (spec 086 FR-001/FR-003; ADR 0034 D3/D8). System.Text.Json's built-in
/// dictionary writer emits entries in enumeration order, which is non-deterministic across processes.
/// </summary>
/// <remarks>
/// Scope is deliberately narrow and precise: the exact generic <see cref="Dictionary{TKey,TValue}"/>,
/// <see cref="IDictionary{TKey,TValue}"/> and <see cref="IReadOnlyDictionary{TKey,TValue}"/> shapes with a
/// <see cref="string"/> key and a non-<see cref="object"/> value. Object-valued dictionaries stay with
/// <see cref="PolymorphicObjectConverterFactory"/> / <see cref="PolymorphicDictionaryConverter"/> (which
/// sort on their own path), so the two factories never contend for the same type. Non-string keys and
/// bespoke dictionary implementations fall through to the built-in converter — none appear in serialized
/// workflow state, and claiming them would add key-conversion surface without a canonical-scope payoff.
/// </remarks>
/// <remarks>
/// Because this claims the dictionary shape by <em>type</em> (not by member), a few System.Text.Json member
/// roles it cannot see are unsupported for a claimed shape — none occur in serialized workflow state today,
/// but wiring these options into a new read path could surface them:
/// <list type="bullet">
/// <item>A <c>[JsonExtensionData]</c> member typed <see cref="IDictionary{TKey,TValue}"/> of
/// <c>JsonElement</c> — STJ forbids a custom converter on extension data and throws when building the
/// contract.</item>
/// <item>A get-only (setter-less, populate-in-place) dictionary member — the converter returns a fresh
/// <see cref="Dictionary{TKey,TValue}"/> STJ cannot assign back, so entries would be lost.</item>
/// <item>A caller that casts a deserialized value to a stricter concrete read-only type
/// (<c>FrozenDictionary</c>/<c>ImmutableDictionary</c>) — read always materializes a mutable
/// <see cref="Dictionary{TKey,TValue}"/>.</item>
/// </list>
/// </remarks>
public sealed class DeterministicDictionaryConverterFactory : JsonConverterFactory
{
    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert)
    {
        return TryGetValueType(typeToConvert, out _);
    }

    /// <inheritdoc />
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        if (!TryGetValueType(typeToConvert, out var valueType))
            throw new NotSupportedException($"'{typeToConvert}' is not a supported deterministic dictionary shape.");

        // Instantiate the converter over the exact requested dictionary type so its declared type always
        // matches (Dictionary<>, IDictionary<>, and IReadOnlyDictionary<> are otherwise mutually incompatible
        // from System.Text.Json's converter-compatibility check).
        var converterType = typeof(DeterministicDictionaryConverter<,>).MakeGenericType(typeToConvert, valueType);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }

    private static bool TryGetValueType(Type type, out Type valueType)
    {
        valueType = null!;

        if (!type.IsGenericType)
            return false;

        var definition = type.GetGenericTypeDefinition();
        if (definition != typeof(Dictionary<,>)
            && definition != typeof(IDictionary<,>)
            && definition != typeof(IReadOnlyDictionary<,>))
            return false;

        var arguments = type.GetGenericArguments();
        if (arguments[0] != typeof(string) || arguments[1] == typeof(object))
            return false;

        valueType = arguments[1];
        return true;
    }
}
