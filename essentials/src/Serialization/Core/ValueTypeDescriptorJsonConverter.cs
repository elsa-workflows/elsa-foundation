using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Elsa.Primitives.Models;

namespace Elsa.Serialization.Core;

// Schema is optional JSON, so omission and a present JSON null are different descriptor identities.
// This member override avoids changing the nullable-placeholder semantics of other runtime contracts.
internal sealed class ValueTypeDescriptorJsonConverter : JsonConverter<ValueTypeDescriptor>
{
    private static readonly ConditionalWeakTable<JsonSerializerOptions, JsonSerializerOptions> DescriptorOptions = new();

    public override ValueTypeDescriptor Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        JsonSerializer.Deserialize<ValueTypeDescriptor>(ref reader, options: ForDescriptor(options))!;

    public override void Write(Utf8JsonWriter writer, ValueTypeDescriptor value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, options: ForDescriptor(options));

    private static JsonSerializerOptions ForDescriptor(JsonSerializerOptions options) => DescriptorOptions.GetValue(options, selected =>
    {
        // Reuse the actual descriptor metadata, including generated enum/nested contracts, owner property
        // settings and callbacks. Remove only this adapter so resolving the same descriptor cannot recurse.
        var copy = new JsonSerializerOptions(selected);
        for (var index = copy.Converters.Count - 1; index >= 0; index--)
            if (copy.Converters[index] is ValueTypeDescriptorJsonConverter) copy.Converters.RemoveAt(index);
        copy.TypeInfoResolver = new SchemaResolver(selected.TypeInfoResolver);
        copy.MakeReadOnly();
        return copy;
    });

    private sealed class SchemaResolver(IJsonTypeInfoResolver? active) : IJsonTypeInfoResolver
    {
        private readonly DefaultJsonTypeInfoResolver fallback = new();

        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
        {
            var info = active?.GetTypeInfo(type, options);
            if (type != typeof(ValueTypeDescriptor)) return info;
            info ??= fallback.GetTypeInfo(type, options);
            if (info.Kind != JsonTypeInfoKind.Object) return info;
            // Schema is the descriptor's sole nullable JSON member, independent of an owner's wire rename.
            var schema = info.Properties.SingleOrDefault(property => property.PropertyType == typeof(JsonElement?));
            if (schema is not null) schema.CustomConverter = new NullableJsonElementConverter();
            return info;
        }
    }
}
