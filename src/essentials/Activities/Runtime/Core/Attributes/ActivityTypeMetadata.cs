using System.Reflection;

namespace Elsa.Activities.Runtime.Core.Attributes;

/// <summary>
/// Reads the CLR authoring metadata shared by design-time reconciliation and publish-time compilation.
/// </summary>
public static class ActivityTypeMetadata
{
    private static readonly string TriggerAttributeName = typeof(TriggerActivityAttribute).FullName!;

    public static string? GetDeclaredActivityType(Type type)
    {
        var field = type.GetField("ActivityType", BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        return field is { IsLiteral: true, IsInitOnly: false } &&
               field.GetRawConstantValue() is string activityType &&
               !string.IsNullOrWhiteSpace(activityType)
            ? activityType
            : null;
    }

    public static bool IsTrigger(Type type) =>
        type.GetCustomAttributesData().Any(attribute => attribute.AttributeType.FullName == TriggerAttributeName);
}
