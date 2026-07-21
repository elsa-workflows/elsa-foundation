namespace Elsa.Activities.Runtime.Core.Attributes;

using Elsa.Primitives.Models;

/// <summary>Declares a stable read-only projection of an activity's atomic result record.</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class OutputAttribute : Attribute
{
    public string? Key { get; init; }
    public string? Path { get; init; }

    /// <summary>
    /// The human-readable label shown for this output in the designer. When omitted, reconciliation derives a
    /// humanized label from the CLR property name.
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>Optional help text shown alongside the output in the designer.</summary>
    public string? Description { get; init; }

    public bool IsRequired { get; init; } = true;
    public bool HasSourceRepresentation { get; init; }
    public ValueRepresentation SourceRepresentation { get; init; }
}
