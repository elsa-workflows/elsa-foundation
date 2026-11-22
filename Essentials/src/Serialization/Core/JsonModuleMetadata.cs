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
        }
        return info;
    }

    private static JsonTypeInfo ValueInfo<T>(JsonSerializerOptions options, JsonConverter converter) => JsonMetadataServices.CreateValueInfo<T>(options, converter);
    private static bool IsFramework(Type type) => type.Namespace is { } name && (name == "System" || name.StartsWith("System.", StringComparison.Ordinal));
}
