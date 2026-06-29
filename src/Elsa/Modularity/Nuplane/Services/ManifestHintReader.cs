using System.Collections;
using System.Reflection;
using System.Text.Json;
using Elsa.Modularity.Core.Models;

namespace Elsa.Modularity.Nuplane.Services;

/// <summary>
/// Reflects the source-only <c>Elsa.Platform.PackageManifest.Generator.Hints</c> attributes off a feature CLR type
/// so the runtime feature catalog can surface the same categories and settings that the pack-time manifest generator
/// writes into <c>elsa-package.json</c>. Attributes are matched by full type name (they compile in as internal types),
/// mirroring elsa-platform's <c>FeatureMetadataReader</c>/<c>SettingDiscoveryService</c>.
/// </summary>
public static class ManifestHintReader
{
    private const string HintsNamespace = "Elsa.Platform.PackageManifest.Generator.Hints";
    private const string FeatureCategoryAttribute = HintsNamespace + ".ManifestFeatureCategoryAttribute";
    private const string SettingAttribute = HintsNamespace + ".ManifestSettingAttribute";
    private const string UIOptionAttribute = HintsNamespace + ".ManifestUIOptionAttribute";
    private const string UIOptionsProviderAttribute = HintsNamespace + ".ManifestUIOptionsProviderAttribute";
    private const string IgnoreAttribute = HintsNamespace + ".ManifestIgnoreAttribute";

    public static ManifestHintResult Read(Type featureType)
    {
        var categories = ReadCategories(featureType);
        var settings = ReadSettings(featureType);
        return new ManifestHintResult(categories, settings);
    }

    private static IReadOnlyList<string> ReadCategories(Type featureType) =>
        featureType.GetCustomAttributesData()
            .Where(x => x.AttributeType.FullName == FeatureCategoryAttribute)
            .Select(x => x.ConstructorArguments.Count > 0 ? x.ConstructorArguments[0].Value as string : null)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyList<FeatureSettingDescriptor> ReadSettings(Type featureType)
    {
        var defaultsInstance = TryCreateInstance(featureType);

        var settings = new List<FeatureSettingDescriptor>();
        foreach (var property in featureType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!IsConfigurable(property))
                continue;

            var jsonType = GetJsonType(Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType);
            if (jsonType is null)
                continue; // Unsupported type — omitted, matching the generator.

            settings.Add(CreateSetting(property, jsonType, defaultsInstance));
        }

        return settings
            .OrderBy(x => x.Category ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsConfigurable(PropertyInfo property) =>
        property.GetIndexParameters().Length == 0 &&
        property.GetSetMethod() is { IsPublic: true } &&
        property.GetCustomAttributesData().All(x => x.AttributeType.FullName != IgnoreAttribute);

    private static FeatureSettingDescriptor CreateSetting(PropertyInfo property, string jsonType, object? defaultsInstance)
    {
        var hint = property.GetCustomAttributesData().FirstOrDefault(x => x.AttributeType.FullName == SettingAttribute);
        var underlyingType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        var nullable = !property.PropertyType.IsValueType || Nullable.GetUnderlyingType(property.PropertyType) is not null;

        var displayName = ReadNamedString(hint, "DisplayName") ?? ToDisplayName(property.Name);
        var defaultValue = ResolveDefaultValue(property, jsonType, ReadNamedString(hint, "DefaultValue"), defaultsInstance);
        var required = ReadNamedBool(hint, "Required") || (!nullable && defaultValue is null);

        var enumNames = underlyingType.IsEnum
            ? Enum.GetNames(underlyingType).Order(StringComparer.Ordinal).ToArray()
            : [];
        var optionsProvider = ReadUIOptionsProvider(property);
        var uiHint = ReadNamedString(hint, "UIHint") ?? ReadNamedString(hint, "UiHint")
            ?? (enumNames.Length > 0 || optionsProvider is not null ? "select-list" : null);
        var options = ReadOptions(property, enumNames);

        return new FeatureSettingDescriptor(
            property.Name,
            displayName,
            ReadNamedString(hint, "Description"),
            ReadNamedString(hint, "Category"),
            ReadNamedString(hint, "Group"),
            property.PropertyType.FullName,
            jsonType,
            required,
            defaultValue,
            ReadNamedBool(hint, "Secret"),
            ReadNamedBool(hint, "Sensitive"),
            ReadNamedBool(hint, "RestartRequired"),
            ReadNamedBool(hint, "Advanced"),
            ReadNamedBool(hint, "Experimental"),
            uiHint,
            optionsProvider,
            options);
    }

    private static JsonElement? ResolveDefaultValue(PropertyInfo property, string jsonType, string? explicitDefault, object? instance)
    {
        // An explicit attribute default takes precedence over the C# default.
        if (!string.IsNullOrWhiteSpace(explicitDefault))
            return ParseDefault(explicitDefault, jsonType);

        if (instance is null)
            return null;

        try
        {
            var value = property.GetValue(instance);
            return value is null ? null : JsonSerializer.SerializeToElement(value);
        }
        catch
        {
            return null;
        }
    }

    private static JsonElement ParseDefault(string value, string jsonType) =>
        jsonType switch
        {
            "boolean" when bool.TryParse(value, out var b) => JsonSerializer.SerializeToElement(b),
            "integer" when long.TryParse(value, out var l) => JsonSerializer.SerializeToElement(l),
            "number" when double.TryParse(value, out var d) => JsonSerializer.SerializeToElement(d),
            _ => JsonSerializer.SerializeToElement(value)
        };

    private static IReadOnlyList<FeatureSettingOptionDescriptor> ReadOptions(PropertyInfo property, IReadOnlyList<string> enumNames)
    {
        var explicitOptions = property.GetCustomAttributesData()
            .Where(x => x.AttributeType.FullName == UIOptionAttribute)
            .Select(x => new FeatureSettingOptionDescriptor(
                ReadNamedString(x, "Label") ?? GetConstructorString(x) ?? "",
                JsonSerializer.SerializeToElement(GetConstructorString(x) ?? ""),
                ReadNamedString(x, "Description")))
            .Where(x => x.Value.ValueKind != JsonValueKind.String || !string.IsNullOrWhiteSpace(x.Value.GetString()))
            .ToArray();

        if (explicitOptions.Length > 0)
            return explicitOptions;

        return enumNames
            .Select(x => new FeatureSettingOptionDescriptor(ToDisplayName(x), JsonSerializer.SerializeToElement(x), null))
            .ToArray();
    }

    private static string? ReadUIOptionsProvider(PropertyInfo property)
    {
        var attribute = property.GetCustomAttributesData().FirstOrDefault(x => x.AttributeType.FullName == UIOptionsProviderAttribute);
        var provider = GetConstructorString(attribute);
        return string.IsNullOrWhiteSpace(provider) ? null : provider;
    }

    private static object? TryCreateInstance(Type featureType)
    {
        if (featureType.IsAbstract || featureType.GetConstructor(Type.EmptyTypes) is null)
            return null;

        try
        {
            return Activator.CreateInstance(featureType);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Mirrors the generator's JSON type mapping; returns null for unsupported types (which are omitted).</summary>
    private static string? GetJsonType(Type type)
    {
        if (type == typeof(string) || type == typeof(Guid) || type == typeof(TimeSpan) ||
            type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(Uri))
            return "string";
        if (type == typeof(bool))
            return "boolean";
        if (type.IsEnum)
            return "string";
        if (type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort) ||
            type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong))
            return "integer";
        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
            return "number";
        if (ImplementsGeneric(type, typeof(IDictionary<,>)) || ImplementsGeneric(type, typeof(IReadOnlyDictionary<,>)))
            return "object";
        if (type.IsArray || (type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type)))
            return "array";

        return null;
    }

    private static bool ImplementsGeneric(Type type, Type genericDefinition) =>
        (type.IsGenericType && type.GetGenericTypeDefinition() == genericDefinition) ||
        type.GetInterfaces().Any(x => x.IsGenericType && x.GetGenericTypeDefinition() == genericDefinition);

    private static string ToDisplayName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        var chars = new List<char>(value.Length + 8);
        for (var i = 0; i < value.Length; i++)
        {
            if (i > 0 && char.IsUpper(value[i]) && !char.IsWhiteSpace(value[i - 1]))
                chars.Add(' ');
            chars.Add(value[i]);
        }

        return new string(chars.ToArray());
    }

    private static string? GetConstructorString(CustomAttributeData? attribute) =>
        attribute is { ConstructorArguments.Count: > 0 } ? attribute.ConstructorArguments[0].Value as string : null;

    private static string? ReadNamedString(CustomAttributeData? attribute, string name)
    {
        var value = ReadNamedArgument(attribute, name);
        return value as string;
    }

    private static bool ReadNamedBool(CustomAttributeData? attribute, string name) =>
        ReadNamedArgument(attribute, name) is true;

    private static object? ReadNamedArgument(CustomAttributeData? attribute, string name)
    {
        if (attribute is null)
            return null;

        foreach (var argument in attribute.NamedArguments)
        {
            if (string.Equals(argument.MemberName, name, StringComparison.Ordinal))
                return argument.TypedValue.Value;
        }

        return null;
    }
}

/// <summary>Categories and settings reflected from a feature type's manifest hint attributes.</summary>
public sealed record ManifestHintResult(
    IReadOnlyList<string> Categories,
    IReadOnlyList<FeatureSettingDescriptor> Settings);
