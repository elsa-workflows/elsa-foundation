namespace Elsa.Integrations.Sdk.Design;

/// <summary>
/// Design-time field on a connection type or auth scheme. Runtime execution never loads this type
/// (#2726 rule 8). Sensitive fields are stored as secret references, not in the connection row.
/// </summary>
public sealed class ConnectionFieldDescriptor
{
    public required string Name { get; init; }

    public required string DisplayName { get; init; }

    public string? Description { get; init; }

    public bool IsSensitive { get; init; }
}
