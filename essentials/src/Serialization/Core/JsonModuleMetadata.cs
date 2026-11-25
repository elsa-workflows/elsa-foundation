using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Elsa.Serialization.Core;

/// <summary>Scopes a module's generated resolver and local wire converters to contracts that module owns.</summary>
public static class JsonModuleMetadata
{
    /// <summary>Owned contracts and BCL containers whose non-BCL arguments are all owned belong to this module.</summary>
    public static bool Owns(Type type, string moduleNamespace)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleNamespace);
        if (type.IsArray) return Owns(type.GetElementType()!, moduleNamespace);
        var name = type.Namespace;
        if (name == moduleNamespace || name?.StartsWith(moduleNamespace + ".", StringComparison.Ordinal) == true) return true;
        if (!type.IsConstructedGenericType || !IsFramework(type.GetGenericTypeDefinition())) return false;
        var arguments = type.GetGenericArguments();
        return arguments.Any(argument => Owns(argument, moduleNamespace)) && arguments.All(argument => IsFramework(argument) || Owns(argument, moduleNamespace));
    }

    /// <summary>Builds metadata with the caller's options; no module mutates the host's converter or policy collections.</summary>
    public static JsonTypeInfo? Resolve(JsonSerializerContext context, Type type, JsonSerializerOptions hostOptions, string moduleNamespace)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(hostOptions);
        if (!Owns(type, moduleNamespace)) return null;
        var ownConverter = context.Options.Converters.FirstOrDefault(converter => converter.CanConvert(type));
        if (type.IsEnum && ownConverter is not null)
        {
            var converter = ownConverter is JsonConverterFactory factory ? factory.CreateConverter(type, hostOptions) : ownConverter;
            return (JsonTypeInfo)typeof(JsonModuleMetadata).GetMethod(nameof(ValueInfo), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(type).Invoke(null, [hostOptions, converter])!;
        }
        var info = ((IJsonTypeInfoResolver)context).GetTypeInfo(type, hostOptions);
        if (info?.Kind != JsonTypeInfoKind.Object) return info;
        foreach (var property in info.Properties)
        {
            if (property.CustomConverter is not null) continue;
            var selected = context.Options.Converters.FirstOrDefault(converter => converter.CanConvert(property.PropertyType));
            if (selected is not null) property.CustomConverter = selected;
            else if (HasTemporalElement(property.PropertyType))
                property.CustomConverter = (JsonConverter)Activator.CreateInstance(
                    typeof(WireMemberConverter<>).MakeGenericType(property.PropertyType), context.GetTypeInfo(property.PropertyType)!)!;
        }
        return info;
    }

    private static JsonTypeInfo ValueInfo<T>(JsonSerializerOptions options, JsonConverter converter) => JsonMetadataServices.CreateValueInfo<T>(options, converter);

    // Only framework containers of a known temporal leaf need their owner's local metadata. Do not
    // walk object members: their own resolver must retain ownership, including foreign DTOs in lists.
    private static bool HasTemporalElement(Type type)
    {
        var visited = new HashSet<Type>();
        while (visited.Add(type))
        {
            if (Nullable.GetUnderlyingType(type) is { } underlying) { type = underlying; continue; }
            if (type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(DateOnly) ||
                type == typeof(TimeOnly) || type == typeof(TimeSpan)) return true;
            if (type.IsArray) { type = type.GetElementType()!; continue; }
            if (!type.IsConstructedGenericType || !IsFramework(type.GetGenericTypeDefinition())) return false;
            var shapes = type.GetInterfaces().Append(type).Where(candidate => candidate.IsGenericType).ToArray();
            var dictionary = shapes.FirstOrDefault(candidate => candidate.GetGenericTypeDefinition() is var definition &&
                (definition == typeof(IDictionary<,>) || definition == typeof(IReadOnlyDictionary<,>)));
            if (dictionary is not null) { type = dictionary.GetGenericArguments()[1]; continue; }
            var enumerable = shapes.FirstOrDefault(candidate => candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>));
            if (enumerable is null) return false;
            type = enumerable.GetGenericArguments()[0];
        }
        return false;
    }

    private sealed class WireMemberConverter<T>(JsonTypeInfo info) : JsonConverter<T>
    {
        private readonly JsonTypeInfo<T> metadata = (JsonTypeInfo<T>)info;
        public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            try { return JsonSerializer.Deserialize(ref reader, metadata); }
            catch (JsonException error) { throw new WireMemberJsonException(error); }
        }
        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) => JsonSerializer.Serialize(writer, value, metadata);
    }

    // A nested serializer starts its path at $. Let the outer serializer supply the property's path,
    // then append the retained local suffix in Message, which the API body binder returns to its caller.
    private sealed class WireMemberJsonException(JsonException inner) : JsonException(Detail(inner), inner)
    {
        private readonly string suffix = inner.Path is { Length: > 0 } path && path[0] == '$' ? path[1..] : "";
        public override string Message => Path is { Length: > 0 } outer ? $"{base.Message} Path: {outer}{suffix}." : base.Message;

        private static string Detail(JsonException error)
        {
            var message = error.Message;
            var path = $" Path: {error.Path}.";
            return error is JsonConversionException { AppendPathToMessage: true } && message.EndsWith(path, StringComparison.Ordinal)
                ? message[..^path.Length] : message;
        }
    }

    private static bool IsFramework(Type type) => type.Namespace is { } name && (name == "System" || name.StartsWith("System.", StringComparison.Ordinal));
}
