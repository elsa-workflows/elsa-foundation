namespace Elsa.Activities.Runtime.Core.Attributes;

/// <summary>Declares that an activity body accesses running-workflow outputs or identity.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class AccessesWorkflowStateAttribute : Attribute
{
    /// <summary>Recognizes the declaration during ordinary and metadata-only reflection.</summary>
    public static bool IsDeclaredBy(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        for (var current = type; current is not null; current = current.BaseType)
            if (current.GetCustomAttributesData().Any(attribute => attribute.AttributeType.FullName == typeof(AccessesWorkflowStateAttribute).FullName))
                return true;
        return false;
    }

    /// <summary>Recognizes native declarations and engine script consumers independently of variable access.</summary>
    public static bool IsAccessedBy(Type type) => IsDeclaredBy(type) ||
        type.GetInterfaces().Any(contract => contract.FullName == typeof(Contracts.IStepWorkflowStateConsumer).FullName);
}
