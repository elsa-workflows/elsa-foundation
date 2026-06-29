using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Elsa.Serialization.Core;

namespace Elsa.Persistence.Groundwork.Querying;

/// <summary>
/// Builds the JSON serialization settings for Groundwork document payloads, implementing the
/// domain-projection model shared by every persistence lane: serialize the logical entity, delegate the
/// authored-content members to Elsa's canonical <see cref="IPayloadSerializer"/> (so polymorphic graphs such
/// as <c>ActivityNode</c> and expressions round-trip with the same type-discriminator metadata the relational
/// provider uses), and exclude relational-storage artifacts (EF shadow <c>*Source</c> strings, the
/// <c>RowNumber</c> identity column) and cross-aggregate navigation properties (fetched via an explicit second
/// read instead of being embedded).
/// <para>
/// Exclusion is driven by a <see cref="DefaultJsonTypeInfoResolver"/> modifier rather than <c>[JsonIgnore]</c>
/// on the domain types, so the core entities stay free of persistence concerns. Excluded members are only
/// suppressed from <b>output</b> — they remain in the contract so an entity's parameterized constructor can
/// still bind on read (the member is simply absent from the document and falls back to its default).
/// </para>
/// </summary>
public static class GroundworkDocumentSerialization
{
    /// <param name="payloadSerializer">The host's canonical payload serializer the delegated members route through.</param>
    /// <param name="excludedMembers">Member names (case-insensitive) suppressed from the document — shadows, identity columns, navigation.</param>
    /// <param name="payloadDelegatedTypes">Member types whose JSON is produced/consumed by <paramref name="payloadSerializer"/>.</param>
    public static JsonSerializerOptions Create(
        IPayloadSerializer payloadSerializer,
        IReadOnlyCollection<string> excludedMembers,
        IReadOnlyCollection<Type> payloadDelegatedTypes)
    {
        var excluded = new HashSet<string>(excludedMembers, StringComparer.OrdinalIgnoreCase);
        var delegated = new HashSet<Type>(payloadDelegatedTypes);

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver
            {
                Modifiers = { typeInfo => SuppressExcludedMembers(typeInfo, excluded) }
            }
        };

        options.Converters.Add(new PayloadDelegatingConverterFactory(payloadSerializer, delegated));
        return options;
    }

    /// <summary>
    /// Creates options for entities whose projected members are all plain JSON (no payload-delegated authored
    /// content), excluding the supplied relational-storage / navigation members.
    /// </summary>
    /// <param name="excludedMembers">Member names (case-insensitive) suppressed from the document.</param>
    public static JsonSerializerOptions Create(IReadOnlyCollection<string> excludedMembers)
    {
        var excluded = new HashSet<string>(excludedMembers, StringComparer.OrdinalIgnoreCase);

        return new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver
            {
                Modifiers = { typeInfo => SuppressExcludedMembers(typeInfo, excluded) }
            }
        };
    }

    private static void SuppressExcludedMembers(JsonTypeInfo typeInfo, HashSet<string> excluded)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object)
            return;

        foreach (var property in typeInfo.Properties)
        {
            if (excluded.Contains(property.Name))
                property.ShouldSerialize = static (_, _) => false;
        }
    }

    private sealed class PayloadDelegatingConverterFactory(IPayloadSerializer payloadSerializer, HashSet<Type> delegatedTypes) : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) => delegatedTypes.Contains(typeToConvert);

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
            (JsonConverter)Activator.CreateInstance(
                typeof(PayloadDelegatingConverter<>).MakeGenericType(typeToConvert), payloadSerializer)!;
    }

    // Routes a member's JSON through the canonical payload serializer.
    private sealed class PayloadDelegatingConverter<T>(IPayloadSerializer payloadSerializer) : JsonConverter<T>
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            return payloadSerializer.Deserialize<T>(document.RootElement);
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            if (value is null)
            {
                writer.WriteNullValue();
                return;
            }

            payloadSerializer.SerializeToElement(value).WriteTo(writer);
        }
    }
}
