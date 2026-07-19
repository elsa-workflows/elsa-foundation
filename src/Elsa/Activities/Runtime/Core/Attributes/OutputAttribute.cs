namespace Elsa.Activities.Runtime.Core.Attributes;

using Elsa.Primitives.Models;

/// <summary>Declares a stable read-only projection of an activity's atomic result record.</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class OutputAttribute : Attribute
{
    public string? Key { get; init; }
    public string? Path { get; init; }
    public bool IsRequired { get; init; } = true;
    public bool HasSourceRepresentation { get; init; }
    public ValueRepresentation SourceRepresentation { get; init; }
}
