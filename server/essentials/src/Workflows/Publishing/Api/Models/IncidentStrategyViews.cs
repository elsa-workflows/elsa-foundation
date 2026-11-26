namespace Elsa.Workflows.Publishing.Api.Models;

/// <summary>Stable incident-strategy identity exposed to publishing clients.</summary>
public sealed record IncidentStrategyReferenceView(string Alias, string Version);

/// <summary>Safe descriptor metadata for an incident strategy a workflow author may select.</summary>
public sealed record IncidentStrategyDescriptorView(
    string Alias,
    string Version,
    string DisplayName,
    string? Description);

/// <summary>Registered incident strategies and the exact default publishing will pin when authors inherit it.</summary>
public sealed record IncidentStrategiesResponse(
    IReadOnlyCollection<IncidentStrategyDescriptorView> Items,
    IncidentStrategyReferenceView DefaultStrategy);
