using System.Buffers.Binary;
using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Elsa.Serialization.Core;

public static partial class JsonHashInput
{
    /// <summary>Encodes opaque CLR identity strings losslessly, while carried JSON remains JSON. The owner supplies explicit projections for custom converter-backed contracts.</summary>
    public static JsonElement ProjectOpaqueClr<T>(T value, Func<object, object?> ownerProjection)
    {
        ArgumentNullException.ThrowIfNull(ownerProjection);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { MaxDepth = 1000 }))
            new OpaqueProjector(ownerProjection).Write(writer, value, typeof(T), 0, tagObject: false);
        using var document = JsonDocument.Parse(stream.ToArray(), new JsonDocumentOptions { MaxDepth = 1000 });
        return document.RootElement.Clone();
    }

    /// <summary>Encodes each UTF-16 code unit, including unpaired surrogates, without a text encoder's replacement fallback.</summary>
    public static string OpaqueString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var bytes = new byte[checked(value.Length * sizeof(char))];
        for (var index = 0; index < value.Length; index++) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(index * sizeof(char), sizeof(char)), value[index]);
        return Convert.ToBase64String(bytes);
    }

    private sealed class OpaqueProjector(Func<object, object?> ownerProjection)
    {
        private readonly HashSet<object> active = new(ReferenceEqualityComparer.Instance);

        internal void Write(Utf8JsonWriter writer, object? value, Type declaredType, int depth, bool tagObject = true)
        {
            if (depth > 1000) throw new JsonException("Opaque hash projection exceeded its depth limit.");
            if (value is null) { writer.WriteNullValue(); return; }
            if (declaredType == typeof(object) && tagObject)
            {
                // An opaque CLR string in an object slot and carried JSON resembling its Base64 spelling
                // are different inputs. The tags contain no physical CLR/assembly identity.
                writer.WriteStartObject();
                writer.WriteString("representation", value is JsonElement or System.Text.Json.Nodes.JsonNode ? "json" : "clr");
                writer.WritePropertyName("value");
                Write(writer, value, value.GetType(), depth + 1, tagObject: false);
                writer.WriteEndObject();
                return;
            }
            if (value is JsonElement json) { writer.WriteRawValue(json.GetRawText(), skipInputValidation: true); return; }
            if (value is System.Text.Json.Nodes.JsonNode node) { JsonSerializer.Serialize(writer, node, Options); return; }
            if (value is string text) { writer.WriteStringValue(OpaqueString(text)); return; }
            var runtimeType = value.GetType();
            var type = Nullable.GetUnderlyingType(declaredType) ?? declaredType;
            if (type == typeof(object)) type = runtimeType;
            if (IsScalar(runtimeType)) { JsonSerializer.Serialize(writer, value, runtimeType, Options); return; }
            var tracked = !runtimeType.IsValueType;
            if (tracked && !active.Add(value)) throw new JsonException("Opaque hash projection contains a reference cycle.");
            try
            {
                if (ownerProjection(value) is { } projection)
                {
                    if (ReferenceEquals(projection, value)) throw new JsonException("An opaque owner projection must not return its source object.");
                    Write(writer, projection, projection.GetType(), depth + 1, tagObject: false);
                    return;
                }
                var info = Options.GetTypeInfo(type);
                JsonDerivedType? derived = null;
                if (type != runtimeType && info.PolymorphismOptions is { } polymorphism)
                {
                    derived = polymorphism.DerivedTypes.FirstOrDefault(candidate => candidate.DerivedType == runtimeType);
                    if (derived.Value.DerivedType is null) throw new JsonException($"Opaque hash projection has no declared derived contract for {runtimeType}.");
                    info = Options.GetTypeInfo(runtimeType);
                }
                if (info.Kind == JsonTypeInfoKind.Dictionary)
                {
                    var shape = type.GetInterfaces().Append(type).First(candidate => candidate.IsGenericType &&
                        candidate.GetGenericTypeDefinition() is var definition && (definition == typeof(IDictionary<,>) || definition == typeof(IReadOnlyDictionary<,>)));
                    var arguments = shape.GetGenericArguments();
                    if (arguments[0] != typeof(string)) throw new JsonException("Opaque hash dictionaries require string keys and an explicit owner projection for other keys.");
                    writer.WriteStartObject();
                    foreach (var entry in (IEnumerable)value)
                    {
                        var entryType = entry.GetType();
                        var key = (string)entryType.GetProperty("Key")!.GetValue(entry)!;
                        writer.WritePropertyName(OpaqueString(key));
                        Write(writer, entryType.GetProperty("Value")!.GetValue(entry), arguments[1], depth + 1);
                    }
                    writer.WriteEndObject();
                    return;
                }
                if (info.Kind == JsonTypeInfoKind.Enumerable)
                {
                    var element = type.IsArray ? type.GetElementType()! : type.GetInterfaces().Append(type)
                        .FirstOrDefault(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))?.GetGenericArguments()[0]
                        ?? typeof(object);
                    writer.WriteStartArray();
                    foreach (var item in (IEnumerable)value) Write(writer, item, element, depth + 1);
                    writer.WriteEndArray();
                    return;
                }
                if (info.Kind != JsonTypeInfoKind.Object) throw new JsonException($"The converter-backed hash contract {type} needs an explicit owner projection.");
                writer.WriteStartObject();
                if (derived?.TypeDiscriminator is { } discriminator)
                {
                    var property = Options.GetTypeInfo(type).PolymorphismOptions!.TypeDiscriminatorPropertyName;
                    if (discriminator is string name) writer.WriteString(property, name);
                    else writer.WriteNumber(property, (int)discriminator);
                }
                foreach (var property in info.Properties)
                {
                    if (property.Get is null) continue;
                    if (property.IsExtensionData || property.CustomConverter is not null)
                        throw new JsonException($"Hash member {type}.{property.Name} needs an explicit owner projection for its converter or extension data.");
                    var memberValue = property.Get(value);
                    var ignore = property.AttributeProvider?.GetCustomAttributes(typeof(JsonIgnoreAttribute), inherit: true).OfType<JsonIgnoreAttribute>().SingleOrDefault()?.Condition;
                    if (ignore == JsonIgnoreCondition.Always || ignore == JsonIgnoreCondition.WhenWritingNull && memberValue is null ||
                        ignore == JsonIgnoreCondition.WhenWritingDefault && IsDefault(memberValue, property.PropertyType) ||
                        property.ShouldSerialize?.Invoke(value, memberValue) == false) continue;
                    writer.WritePropertyName(property.Name);
                    Write(writer, memberValue, property.PropertyType, depth + 1);
                }
                writer.WriteEndObject();
            }
            finally { if (tracked) active.Remove(value); }
        }

        private static bool IsScalar(Type type) => type.IsPrimitive || type.IsEnum || type == typeof(decimal) || type == typeof(Guid) ||
            type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(DateOnly) || type == typeof(TimeOnly) ||
            type == typeof(TimeSpan) || type == typeof(Uri) || type == typeof(byte[]);
        private static bool IsDefault(object? value, Type type) => value is null || type.IsValueType && value.Equals(Activator.CreateInstance(type));
    }
}
