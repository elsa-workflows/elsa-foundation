using System.Reflection;

namespace Elsa.Activities.Design.Reconciliation.Clr.Services;

/// <summary>
/// Reflection-only attribute readers that manually walk the inheritance chain (issue #417 item 3).
/// The CLR scanner reads metadata through a <see cref="MetadataLoadContext"/>, where
/// <c>GetCustomAttributes(inherit: true)</c> is unavailable — so an attribute declared on a base
/// class or a base-class property is silently ignored unless the base chain is walked by hand.
/// These helpers mirror the scanner's existing type-chain <c>DerivesFrom</c> pattern.
/// </summary>
public static class ReflectionOnlyAttributes
{
    /// <summary>
    /// Walks <paramref name="type"/> and its base classes, returning the first string constructor
    /// argument of the first occurrence of the attribute named <paramref name="attributeFullName"/>,
    /// or <see langword="null"/> if no base in the chain carries it.
    /// </summary>
    public static string? ReadStringArgumentUpTypeChain(Type type, string attributeFullName)
    {
        for (var current = (Type?)type; current is not null; current = current.BaseType)
        {
            var value = ReadStringArgument(current.GetCustomAttributesData(), attributeFullName);
            if (value is not null)
                return value;
        }

        return null;
    }

    /// <summary>
    /// Returns the first string constructor argument of the attribute named
    /// <paramref name="attributeFullName"/> in <paramref name="attributes"/>, or <see langword="null"/>.
    /// For metadata that does not inherit (e.g. an assembly-level attribute), read directly with this.
    /// </summary>
    public static string? ReadStringArgument(IReadOnlyList<CustomAttributeData> attributes, string attributeFullName) =>
        ReadStringArgument((IEnumerable<CustomAttributeData>)attributes, attributeFullName);

    /// <summary>
    /// Reports whether <paramref name="property"/> — or the same-named property declared on any base
    /// class in its inheritance chain — carries the attribute named <paramref name="attributeFullName"/>.
    /// </summary>
    public static bool HasAttributeUpPropertyChain(PropertyInfo property, string attributeFullName)
    {
        for (var current = (PropertyInfo?)property; current is not null; current = FindBaseProperty(current))
        {
            if (current.GetCustomAttributesData().Any(a => a.AttributeType.FullName == attributeFullName))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Resolves the same-named, same-signature property declared on the immediate base class of
    /// <paramref name="property"/>'s declaring type, or <see langword="null"/> when the base chain is
    /// exhausted. <see cref="BindingFlags.DeclaredOnly"/> keeps the walk moving one level at a time.
    /// </summary>
    private static PropertyInfo? FindBaseProperty(PropertyInfo property)
    {
        var baseType = property.DeclaringType?.BaseType;

        return baseType?.GetProperty(
            property.Name,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
    }

    /// <summary>
    /// Extracts the first string constructor argument of the first attribute matching
    /// <paramref name="attributeFullName"/> in <paramref name="attributes"/>, or <see langword="null"/>.
    /// </summary>
    private static string? ReadStringArgument(IEnumerable<CustomAttributeData> attributes, string attributeFullName)
    {
        var data = attributes.FirstOrDefault(a => a.AttributeType.FullName == attributeFullName);
        return data?.ConstructorArguments is [{ Value: string value }, ..] ? value : null;
    }
}
