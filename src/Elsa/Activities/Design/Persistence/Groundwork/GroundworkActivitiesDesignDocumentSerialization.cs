using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Entities;
using Elsa.Serialization.Core;

namespace Elsa.Activities.Design.Persistence.Groundwork;

/// <summary>Current activity-design JSON options, sourced from Elsa's public payload serializer.</summary>
/// <remarks>
/// Options are derived from each <see cref="IPayloadSerializer.GetOptions"/> instance and cached per instance, the
/// same way <c>WorkflowArtifactClosureSerializer</c> does. The payload serializer only gains its registered
/// converters (<see cref="JsonStringEnumConverter"/> among them) when the converter startup task runs, and it
/// rebuilds its options when that happens. A copy taken earlier, for example by a store that startup-task
/// resolution constructs before any task runs, would write and read enums differently from every later caller.
/// Resolve the options at the point of use; never hold them in a field.
/// </remarks>
public static class GroundworkActivitiesDesignDocumentSerialization
{
    private static readonly ConditionalWeakTable<IPayloadSerializer, DerivedOptions> Cache = new();

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

    /// <summary>Returns read-only document options derived from <paramref name="payloadSerializer"/>'s current options.</summary>
    public static JsonSerializerOptions Get(IPayloadSerializer payloadSerializer)
    {
        ArgumentNullException.ThrowIfNull(payloadSerializer);
        var source = payloadSerializer.GetOptions();
        if (Cache.TryGetValue(payloadSerializer, out var cached) && ReferenceEquals(cached.Source, source))
            return cached.Options;

        var derived = new DerivedOptions(source, Derive(payloadSerializer, source));
        Cache.AddOrUpdate(payloadSerializer, derived);
        return derived.Options;
    }

    private static JsonSerializerOptions Derive(IPayloadSerializer payloadSerializer, JsonSerializerOptions source)
    {
        var options = new JsonSerializerOptions(source)
        {
            TypeInfoResolver = new ExcludingTypeInfoResolver(
                source.TypeInfoResolver,
                new HashSet<string>(ExcludedMembers, StringComparer.OrdinalIgnoreCase))
        };
        options.Converters.Add(new PayloadDelegatingConverterFactory(
            payloadSerializer,
            new HashSet<Type>(PayloadDelegatedTypes)));
        options.MakeReadOnly();
        return options;
    }

    private sealed record DerivedOptions(JsonSerializerOptions Source, JsonSerializerOptions Options);

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
