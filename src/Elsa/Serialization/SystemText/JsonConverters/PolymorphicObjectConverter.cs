using Elsa.Primitives.Extensions;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.Extensions;
using System.Collections;
using System.Dynamic;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Elsa.Serialization.SystemText.JsonConverters;

/// <summary>
/// Reads objects as primitive types rather than <see cref="JsonElement"/> values while also maintaining the .NET type name for reconstructing the actual type.
/// </summary>
public sealed class PolymorphicObjectConverter(IEnumerable<IJsonIslandTypeHandler> jsonIslandTypeHandlers) : JsonConverter<object>
{
    private const string TypePropertyName = "_type";
    private const string ItemsPropertyName = "_items";
    private const string IslandPropertyName = "_island";
    private const string RefPropertyName = "$ref";
    private const string ValuesPropertyName = "$values";

    /// <inheritdoc />
    public override object Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var newOptions = options.Clone();

        if (reader.TokenType != JsonTokenType.StartObject && reader.TokenType != JsonTokenType.StartArray)
            return ReadPrimitive(ref reader, newOptions);

        var targetType = ReadType(reader, options);
        if (targetType == null)
            return ReadObject(ref reader, newOptions);

        // If the target type is not an IEnumerable, or is a dictionary, deserialize the object directly.
        var isEnumerable = typeof(IEnumerable).IsAssignableFrom(targetType);

        if (!isEnumerable)
        {
            try
            {
                return JsonSerializer.Deserialize(ref reader, targetType, newOptions)!;
            }
            catch (Exception e) when (e is NotSupportedException or TargetException)
            {
                throw new JsonException($"Failed to deserialize polymorphic value into target type '{targetType}'.", e);
            }
        }

        if (jsonIslandTypeHandlers.FirstOrDefault(x => x.CanHandle(targetType)) is { } jsonIslandTypeHandler)
        {
            var parsedModel = JsonElement.ParseValue(ref reader);
            var json = parsedModel.GetProperty(IslandPropertyName).GetString();
            return jsonIslandTypeHandler.Read(json);
        }

        // If the target type is a System.Text.JsonObject, parse the JSON island.
        var isJsonObject = targetType == typeof(JsonObject);

        if (isJsonObject)
        {
            var parsedModel = JsonElement.ParseValue(ref reader);
            var systemTextJson = parsedModel.GetProperty(IslandPropertyName).GetString();
            return !string.IsNullOrWhiteSpace(systemTextJson) ? JsonNode.Parse(systemTextJson)! : new JsonObject();
        }

        var isJsonArray = targetType == typeof(JsonArray);

        if (isJsonArray)
        {
            var parsedModel = JsonElement.ParseValue(ref reader);
            var systemTextJson = parsedModel.GetProperty(IslandPropertyName).GetString();
            return !string.IsNullOrWhiteSpace(systemTextJson) ? JsonNode.Parse(systemTextJson)! : new JsonArray();
        }

        var isDictionary = typeof(IDictionary).IsAssignableFrom(targetType);
        if (isDictionary)
        {
            // Remove the _type property name from the JSON, if any.
            var parsedNode = JsonNode.Parse(ref reader)!;
            if (parsedNode is JsonObject parsedModel) parsedModel.Remove(TypePropertyName);
            return parsedNode.Deserialize(targetType, newOptions)!;
        }

        var isCollection = typeof(ICollection).IsAssignableFrom(targetType);

        // Otherwise, deserialize the object as an array.
        var elementType = targetType.IsArray
            ? targetType.GetElementType()
            : targetType.GenericTypeArguments.FirstOrDefault() ??
              (isCollection // Could be a class derived from Collection<T> or List<T>.
                  ? targetType.BaseType?.GenericTypeArguments[0]
                  : targetType.GenericTypeArguments.FirstOrDefault()
                    ?? typeof(object));
        if (elementType == null)
            throw new InvalidOperationException($"Cannot determine the element type of array '{targetType}'.");

        var model = JsonElement.ParseValue(ref reader);

        // Reference metadata is not reconstructed: cyclic/shared references are not supported by this
        // converter, so a wrapper carrying only a $ref yields null (#409 — the former
        // CrossScopedReferenceHandler machinery was never wired up and has been removed).
        if (model.TryGetProperty(RefPropertyName, out _))
            return null!;

        var values = model.TryGetProperty(ItemsPropertyName, out var itemsProp) ? itemsProp.EnumerateArray().ToList() : model.GetProperty(ValuesPropertyName).EnumerateArray().ToList();
        var collection = targetType.IsArray ? Array.CreateInstance(elementType, values.Count) : Activator.CreateInstance(targetType)!;
        var index = 0;

        var isHashSet = targetType.GenericTypeArguments.Length == 1 && typeof(ISet<>).MakeGenericType(targetType.GenericTypeArguments[0]).IsAssignableFrom(targetType);
        var addSetMethod = targetType.GetMethod("Add", [elementType])!;

        foreach (var element in values)
        {
            var deserializedElement = JsonSerializer.Deserialize(JsonSerializer.Serialize(element), elementType, newOptions)!;
            if (collection is Array array)
            {
                array.SetValue(deserializedElement, index++);
            }
            else if (isHashSet)
            {
                addSetMethod.Invoke(collection, [
                    deserializedElement
                ]);
            }
            else if (collection is IList list)
            {
                list.Add(deserializedElement);
            }
        }

        return collection;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
    {
        if (value == null!)
        {
            writer.WriteNullValue();
            return;
        }

        var newOptions = options.Clone();
        var type = value.GetType();

        // If the type is a primitive type or an enumerable of a primitive type, serialize the value directly.
        bool IsPrimitive(Type valueType)
        {
            return type.IsPrimitive
                   || valueType == typeof(string)
                   || valueType == typeof(decimal)
                   || valueType == typeof(DateTimeOffset)
                   || valueType == typeof(DateTime)
                   || valueType == typeof(DateOnly)
                   || valueType == typeof(TimeOnly)
                   || valueType == typeof(JsonElement)
                   || valueType == typeof(Guid)
                   || valueType == typeof(TimeSpan)
                   || valueType == typeof(Uri)
                   || valueType == typeof(Version)
                   || valueType.IsEnum;
        }

        if (IsPrimitive(type))
        {
            // Remove the converter so that we don't end up in an infinite loop.
            newOptions.Converters.RemoveWhere(x => x is PolymorphicObjectConverterFactory);

            // Serialize the value directly.
            JsonSerializer.Serialize(writer, value, newOptions);
            return;
        }

        // JSON island types are written as a string with metadata so they can be rehydrated later.
        if (jsonIslandTypeHandlers.FirstOrDefault(x => x.CanHandle(type)) is { } jsonIslandTypeHandler)
        {
            writer.WriteStartObject();
            writer.WriteString(IslandPropertyName, jsonIslandTypeHandler.Write(value));
            writer.WriteString(TypePropertyName, type.GetSimpleAssemblyQualifiedName());
            writer.WriteEndObject();
            return;
        }

        // Before we serialize the value, check to see if it's an ExpandoObject.
        // If it is, we need to sanitize its property names, because they can contain invalid characters.
        if (value is ExpandoObject)
        {
            var sanitized = new ExpandoObject();
            var dictionary = (IDictionary<string, object?>)sanitized;
            var expando = (IDictionary<string, object?>)value;

            foreach (var kvp in expando)
            {
                var key = EscapeKey(kvp.Key);
                dictionary[key] = kvp.Value;
            }

            value = sanitized;
        }

        var jsonElement = JsonDocument.Parse(JsonSerializer.Serialize(value, type, newOptions)).RootElement;

        // If the value is a string, serialize it directly.
        if (jsonElement.ValueKind == JsonValueKind.String)
        {
            // Serialize the value directly.
            JsonSerializer.Serialize(writer, jsonElement, newOptions);
            return;
        }

        // If the value was serialized as a primitive by another converter,
        // write it directly instead of assuming an object structure.
        if (jsonElement.ValueKind != JsonValueKind.Object &&
            jsonElement.ValueKind != JsonValueKind.Array)
        {
            jsonElement.WriteTo(writer);
            return;
        }

        writer.WriteStartObject();

        if (jsonElement.ValueKind == JsonValueKind.Array)
        {
            writer.WritePropertyName(ItemsPropertyName);
            jsonElement.WriteTo(writer);
        }
        else
        {
            // Ordinal by member/key name so equal graphs (dictionaries filled in any order, host-dependent
            // reflection order) serialize identically; the injected _type discriminator is written after
            // this loop, giving it a fixed final position (spec 086 FR-001/FR-002; ADR 0034 D3/D8).
            foreach (var property in jsonElement.EnumerateObject()
                         .Where(property => !property.NameEquals(TypePropertyName))
                         .InCanonicalOrder(property => property.Name))
            {
                writer.WritePropertyName(property.Name);
                property.Value.WriteTo(writer);
            }
        }

        // Write the type discriminator so the actual type can be reconstructed when deserializing. Identity is
        // a registry alias via TypeJsonConverter — never an assembly-qualified name (ADR 0035 D1/D4). Without a
        // TypeJsonConverter there is no registry-backed writer, so the discriminator is omitted rather than
        // falling back to an AQN (the removed gadget path — see the symmetric read in ReadType).
        if (type != typeof(ExpandoObject)
            && newOptions.Converters.OfType<TypeJsonConverter>().FirstOrDefault() is { } typeJsonConverter)
        {
            writer.WritePropertyName(TypePropertyName);
            typeJsonConverter.Write(writer, type, newOptions);
        }

        writer.WriteEndObject();
    }

    private Type? ReadType(Utf8JsonReader reader, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            return null;

        reader.Read(); // Move to the first token inside the object.

        // Read while we haven't reached the end of the object.
        while (reader.TokenType != JsonTokenType.EndObject)
        {
            // If we find the _type property, resolve it through the registry-backed TypeJsonConverter. There is
            // no assembly-qualified-name fallback: without a TypeJsonConverter the type stays unresolved (the
            // removed Type.GetType gadget path — ADR 0035 D1/D4), so the value is read as untyped.
            if (reader.TokenType == JsonTokenType.PropertyName && reader.ValueTextEquals(TypePropertyName))
            {
                reader.Read(); // Move to the value of the _type property
                return options.Converters.OfType<TypeJsonConverter>().FirstOrDefault() is { } typeJsonConverter
                    ? typeJsonConverter.Read(ref reader, typeof(Type), options)
                    : null;
            }

            // Skip through nested objects and arrays.
            if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
            {
                var depth = 1;

                while (depth > 0 && reader.Read())
                {
                    switch (reader.TokenType)
                    {
                        case JsonTokenType.StartObject:
                        case JsonTokenType.StartArray:
                            depth++;
                            break;

                        case JsonTokenType.EndObject:
                        case JsonTokenType.EndArray:
                            depth--;
                            break;
                    }
                }
            }

            reader.Read(); // Move to the next token
        }

        // No _type discriminator present: the value is untyped.
        return null;
    }

    private static object ReadPrimitive(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        return (reader.TokenType switch
        {
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            JsonTokenType.Number when reader.TryGetInt64(out var l) => l,
            JsonTokenType.Number => reader.GetDouble(),
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Null => null,
            _ => throw new JsonException("Not a primitive type.")
        })!;
    }

    private object ReadObject(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.StartArray:
            {
                var list = new List<object>();
                while (reader.Read())
                {
                    switch (reader.TokenType)
                    {
                        default:
                            list.Add(Read(ref reader, typeof(object), options));
                            break;

                        case JsonTokenType.EndArray:
                            return list;
                    }
                }

                throw new JsonException();
            }
            case JsonTokenType.StartObject:
                var dict = new ExpandoObject() as IDictionary<string, object>;
                while (reader.Read())
                {
                    switch (reader.TokenType)
                    {
                        case JsonTokenType.EndObject:
                            // Reference metadata is not reconstructed (#409): a $ref-only object resolves
                            // to the raw reference id read below; $id keys are kept as ordinary data.
                            if (dict.Count == 1 && dict.TryGetValue(RefPropertyName, out var referencedObject))
                                return referencedObject;
                            return dict;

                        case JsonTokenType.PropertyName:
                            var key = reader.GetString()!;
                            reader.Read();
                            var value = Read(ref reader, typeof(object), options);
                            var unescapedKey = UnescapeKey(key);
                            dict.Add(unescapedKey, value);
                            break;

                        default:
                            throw new JsonException();
                    }
                }

                throw new JsonException();
            default:
                throw new JsonException($"Unknown token {reader.TokenType}");
        }
    }

    private static string EscapeKey(string key)
    {
        return key.Replace("$", @"\\$");
    }

    private static string UnescapeKey(string key)
    {
        return key.Replace(@"\\$", "$");
    }
}