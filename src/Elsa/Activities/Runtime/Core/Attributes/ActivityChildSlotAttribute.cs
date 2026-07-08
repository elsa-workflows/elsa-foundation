namespace Elsa.Activities.Runtime.Core.Attributes;

/// <summary>
/// Declares one authored child slot in an activity's stable structure contract.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ActivityChildSlotAttribute : Attribute
{
    public ActivityChildSlotAttribute(string name, string property, string displayName, string cardinality)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A child slot name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(property))
            throw new ArgumentException("A child slot payload property is required.", nameof(property));
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("A child slot display name is required.", nameof(displayName));
        if (string.IsNullOrWhiteSpace(cardinality))
            throw new ArgumentException("A child slot cardinality is required.", nameof(cardinality));

        Name = name;
        Property = property;
        DisplayName = displayName;
        Cardinality = cardinality;
    }

    /// <summary>The runtime slot name used by the compiled executable structure.</summary>
    public string Name { get; }

    /// <summary>The top-level authored payload property that contains this slot.</summary>
    public string Property { get; }

    /// <summary>The display label design surfaces should use for this slot.</summary>
    public string DisplayName { get; }

    /// <summary>Whether the slot contains a single child or many children.</summary>
    public string Cardinality { get; }

    /// <summary>The collection property for repeatable single-child slots such as switch cases.</summary>
    public string? CollectionProperty { get; init; }

    /// <summary>The child activity property inside a repeatable slot item.</summary>
    public string? ChildProperty { get; init; }

    /// <summary>The item property used to label a repeatable slot instance.</summary>
    public string? LabelProperty { get; init; }

    /// <summary>The template used to derive a runtime slot name from a repeatable slot item.</summary>
    public string? SlotNameTemplate { get; init; }
}
