namespace Elsa.Activities.Runtime.Core.Attributes;

/// <summary>
/// Declares design-time presentation metadata for an activity input property.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class ActivityInputAttribute : Attribute
{
    /// <summary>The stable contract key. Defaults to the CLR property name when omitted.</summary>
    public string? Key { get; init; }

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

    /// <summary>The design-time editor hint used to present this input.</summary>
    public string? UIHint { get; init; }

    /// <summary>Ordered string options. Each value is used as both label and authored value.</summary>
    public string[]? Options { get; init; }

    /// <summary>The case-sensitive key of a design-side dynamic options provider.</summary>
    public string? OptionsProvider { get; init; }

    /// <summary>Sibling inputs whose changes invalidate dynamically provided options.</summary>
    public string[]? OptionsProviderDependencies { get; init; }
}
