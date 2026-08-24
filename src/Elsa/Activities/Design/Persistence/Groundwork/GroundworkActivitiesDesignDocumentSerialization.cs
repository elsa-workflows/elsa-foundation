using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Entities;
using Elsa.Serialization.Core;

namespace Elsa.Activities.Design.Persistence.Groundwork;

/// <summary>Current activity-design JSON options, sourced from Elsa's public payload serializer.</summary>
public static class GroundworkActivitiesDesignDocumentSerialization
{
    private static readonly string[] ExcludedMembers =
    [
        nameof(Entity.RowNumber),
        "DescriptorPayloadSource",
        "InputsSource",
        "OutputsSource",
        "DesignFacetsSource",
        "Definition"
    ];

    private static readonly Type[] PayloadDelegatedTypes =
    [
        typeof(IEnumerable<InputDefinition>),
        typeof(IEnumerable<OutputDefinition>),
        typeof(IEnumerable<ActivityDesignFacet>)
    ];

    public static JsonSerializerOptions Create(IPayloadSerializer payloadSerializer)
    {
        ArgumentNullException.ThrowIfNull(payloadSerializer);
        var options = new JsonSerializerOptions(payloadSerializer.GetOptions())
        {
            TypeInfoResolver = new ExcludingTypeInfoResolver(
                payloadSerializer.GetOptions().TypeInfoResolver,
                new HashSet<string>(ExcludedMembers, StringComparer.OrdinalIgnoreCase))
        };
        options.Converters.Add(new PayloadDelegatingConverterFactory(
            payloadSerializer,
            new HashSet<Type>(PayloadDelegatedTypes)));
        return options;
    }

    private sealed class ExcludingTypeInfoResolver(
        IJsonTypeInfoResolver? source,
        HashSet<string> excluded) : IJsonTypeInfoResolver
    {
        private readonly IJsonTypeInfoResolver inner = source ?? new DefaultJsonTypeInfoResolver();

        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
        {
            var typeInfo = inner.GetTypeInfo(type, options);
            if (typeInfo?.Kind != JsonTypeInfoKind.Object)
                return typeInfo;

            foreach (var property in typeInfo.Properties)
            {
                if (excluded.Contains(property.Name) ||
                    property.AttributeProvider is PropertyInfo member && excluded.Contains(member.Name))
                    property.ShouldSerialize = static (_, _) => false;
            }

            return typeInfo;
        }
    }

    private sealed class PayloadDelegatingConverterFactory(
        IPayloadSerializer payloadSerializer,
        HashSet<Type> delegatedTypes) : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) => delegatedTypes.Contains(typeToConvert);

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
            (JsonConverter)Activator.CreateInstance(
                typeof(PayloadDelegatingConverter<>).MakeGenericType(typeToConvert),
                payloadSerializer)!;
    }

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
