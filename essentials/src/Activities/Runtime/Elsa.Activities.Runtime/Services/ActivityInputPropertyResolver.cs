using System.Reflection;
using Elsa.Activities.Runtime.Core.Attributes;

namespace Elsa.Activities.Runtime.Services;

/// <summary>
/// Resolves input metadata from a public activity property or the same-named declaration on a base
/// class while keeping the originally discovered (most-derived) property as the value target.
/// </summary>
internal static class ActivityInputPropertyResolver
{
    public static ActivityInputAttribute? FindAttribute(PropertyInfo property)
    {
        for (var declaringType = property.DeclaringType; declaringType is not null; declaringType = declaringType.BaseType)
        {
            var declaredProperty = declaringType.GetProperty(
                property.Name,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            var attribute = declaredProperty?.GetCustomAttribute<ActivityInputAttribute>(inherit: false);
            if (attribute is not null)
                return attribute;
        }

        return null;
    }
}
