namespace Elsa.Activities.Runtime.Core.Attributes;

/// <summary>
/// Declares design-time presentation metadata, and the sensitivity of the value, for an activity input property.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class ActivityInputAttribute : Attribute
{
    /// <summary>The stable contract key. Defaults to the CLR property name when omitted.</summary>
    public string? Key { get; init; }

    /// <summary>
    /// The human-readable label shown for this input in the designer. When omitted, reconciliation derives a
    /// humanized label from the CLR property name (e.g. <c>ExpectedStatusCodes</c> → <c>Expected Status Codes</c>).
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>Optional help text shown alongside the input in the designer.</summary>
    public string? Description { get; init; }

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

    /// <summary>
    /// The input carries data that must not appear in logs, evidence or inspection output. Every binding of the input
    /// is compiled as sensitive, and an authored binding that marks it not sensitive is refused with <c>VF-ACT-005</c>.
    /// </summary>
    public bool IsSensitive { get; init; }

    /// <summary>
    /// The input carries a credential: it accepts only a secret reference or no binding. Implies
    /// <see cref="IsSensitive"/>, and every binding of the input requires encryption. CLR activity reconciliation refuses
    /// the declaration on an input that could never be bound to a secret reference: one with a
    /// <see cref="DefaultValue"/>, one whose type is not <see cref="string"/>, one the activity type names in
    /// <see cref="RefusesSecretBindingAttribute"/>, and any input of a checkpoint participant.
    /// </summary>
    public bool IsCredential { get; init; }
}
