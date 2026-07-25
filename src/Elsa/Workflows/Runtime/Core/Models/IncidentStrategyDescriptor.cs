using Elsa.Workflows.Primitives.Models;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Immutable presentation and identity metadata for an incident strategy.</summary>
public sealed class IncidentStrategyDescriptor
{
    public IncidentStrategyDescriptor(IncidentStrategyReference reference, string displayName, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        if (description is { Length: 0 })
            throw new ArgumentException("Description cannot be empty when supplied.", nameof(description));

        Reference = reference;
        DisplayName = displayName;
        Description = description;
    }

    public IncidentStrategyReference Reference { get; }
    public string DisplayName { get; }
    public string? Description { get; }

    internal bool IsEquivalentTo(IncidentStrategyDescriptor other) =>
        Reference.Equals(other.Reference) &&
        StringComparer.Ordinal.Equals(DisplayName, other.DisplayName) &&
        StringComparer.Ordinal.Equals(Description, other.Description);
}
