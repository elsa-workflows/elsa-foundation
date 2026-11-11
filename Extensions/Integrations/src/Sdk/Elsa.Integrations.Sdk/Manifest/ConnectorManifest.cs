using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;

namespace Elsa.Integrations.Sdk.Manifest;

/// <summary>
/// Build-time packaging copy of activity attributes. CLR reconciliation already discovers
/// <c>IActivity</c> types from <c>[Activity]</c>, <c>[ActivityInput]</c>, <c>[Version]</c> and
/// <c>[TriggerActivity]</c>; this type is never a second catalog (#2726 rule 1, constitution §E2.8).
/// Do not read <c>connector.json</c> at runtime for activity metadata.
/// </summary>
public sealed class ConnectorManifest
{
    public const string SchemaVersionValue = "1";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string SchemaVersion { get; init; } = SchemaVersionValue;

    public IReadOnlyList<ConnectorActivityEntry> Activities { get; init; } = [];

    public static ConnectorManifest FromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var activities = LoadableTypes(assembly)
            .Where(IsActivityType)
            .Select(ToEntry)
            .OrderBy(entry => entry.TypeName, StringComparer.Ordinal)
            .ToArray();

        return new ConnectorManifest { Activities = activities };
    }

    public string ToJson() => JsonSerializer.Serialize(this, SerializerOptions);

    public void WriteTo(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, ToJson());
    }

    /// <summary>
    /// Types that resolved when <see cref="Assembly.GetTypes"/> threw
    /// <see cref="ReflectionTypeLoadException"/>, matching CLR reconciliation's partial-load scan.
    /// </summary>
    internal static Type[] LoadedTypes(ReflectionTypeLoadException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return [..exception.Types.OfType<Type>()];
    }

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return LoadedTypes(exception);
        }
    }

    /// <summary>
    /// Same shape as <c>ClrAssemblyScanner.IsActivityType</c>: concrete, closed, and an
    /// <see cref="IActivity"/>. Open generics cannot be activated.
    /// </summary>
    private static bool IsActivityType(Type type) =>
        type is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false }
        && typeof(IActivity).IsAssignableFrom(type);

    private static readonly string ActivityAttributeFullName = typeof(ActivityAttribute).FullName!;

    private static ConnectorActivityEntry ToEntry(Type type)
    {
        var inputs = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => (Property: property, Attribute: property.GetCustomAttribute<ActivityInputAttribute>(inherit: true)))
            .Where(pair => pair.Attribute is not null)
            .Select(pair => ToInput(pair.Property, pair.Attribute!))
            .ToArray();

        return new ConnectorActivityEntry
        {
            TypeName = type.FullName ?? type.Name,
            Version = ConnectorActivityVersion.Resolve(type),
            Icon = ReadIconUpTypeChain(type),
            IsTrigger = type.GetCustomAttribute<TriggerActivityAttribute>(inherit: false) is not null,
            Inputs = inputs
        };
    }

    /// <summary>
    /// Same walk as <c>ClrAssemblyScanner.BuildIconFacet</c>: keep going until a non-empty
    /// <c>[Activity(Icon = …)]</c> is found. A derived <c>[Activity]</c> with no icon must not hide
    /// the base icon.
    /// </summary>
    private static string? ReadIconUpTypeChain(Type type)
    {
        for (var current = (Type?)type; current is not null; current = current.BaseType)
        {
            var data = current.GetCustomAttributesData()
                .FirstOrDefault(attribute => attribute.AttributeType.FullName == ActivityAttributeFullName);
            if (data is null)
                continue;

            var icon = data.NamedArguments
                .FirstOrDefault(argument => argument.MemberName == nameof(ActivityAttribute.Icon))
                .TypedValue.Value as string;
            if (!string.IsNullOrWhiteSpace(icon))
                return icon;
        }

        return null;
    }

    private static ConnectorInputEntry ToInput(PropertyInfo property, ActivityInputAttribute attribute)
    {
        var tier = attribute.Tier;
        return new ConnectorInputEntry
        {
            Key = string.IsNullOrWhiteSpace(attribute.Key) ? property.Name : attribute.Key,
            DisplayName = attribute.DisplayName,
            Tier = Enum.IsDefined(tier) && (int)tier != 0
                ? char.ToLowerInvariant(tier.ToString()[0]) + tier.ToString()[1..]
                : null,
            IsCredential = attribute.IsCredential,
            IsSensitive = attribute.IsSensitive || attribute.IsCredential
        };
    }
}
