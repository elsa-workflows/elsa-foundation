using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Elsa.Serialization.Core;

/// <summary>
/// Wraps an <see cref="IJsonTypeInfoResolver"/> and marks named members as non-serializable.
/// Matching is case-insensitive against either the JSON property name or the CLR
/// <see cref="PropertyInfo.Name"/>. Excluded properties stay on the type info;
/// they are not removed.
/// </summary>
public sealed class ExcludingJsonTypeInfoResolver : IJsonTypeInfoResolver
{
    private readonly HashSet<string> excludedMembers;
    private readonly IJsonTypeInfoResolver inner;

    public ExcludingJsonTypeInfoResolver(IEnumerable<string> excludedMembers, IJsonTypeInfoResolver? inner = null)
    {
        ArgumentNullException.ThrowIfNull(excludedMembers);
        this.excludedMembers = new HashSet<string>(excludedMembers, StringComparer.OrdinalIgnoreCase);
        this.inner = inner ?? new DefaultJsonTypeInfoResolver();
    }

    public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
    {
        var typeInfo = inner.GetTypeInfo(type, options);
        if (typeInfo?.Kind != JsonTypeInfoKind.Object)
            return typeInfo;

        foreach (var property in typeInfo.Properties)
        {
            if (excludedMembers.Contains(property.Name) ||
                property.AttributeProvider is PropertyInfo member &&
                excludedMembers.Contains(member.Name))
            {
                property.ShouldSerialize = static (_, _) => false;
            }
        }

        return typeInfo;
    }
}
