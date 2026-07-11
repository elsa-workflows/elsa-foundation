namespace Elsa.Activities.Runtime.Core.Attributes;

/// <summary>
/// Declares design-time presentation metadata for an activity input property.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class ActivityInputAttribute : Attribute
{
    /// <summary>The input's relative presentation order within the activity property list.</summary>
    public float Order { get; init; }

    /// <summary>The optional category used to group the input in design-time property editors.</summary>
    public string? Category { get; init; }

    /// <summary>
    /// Optional literal default value, parsed by CLR activity reconciliation according to the input value type.
    /// </summary>
    public string? DefaultValue { get; init; }

    /// <summary>The expression syntax to use when the input has no authored value.</summary>
    public string? DefaultSyntax { get; init; }
}
