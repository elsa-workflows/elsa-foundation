namespace Elsa.Api.AspNetCore;

public enum EndpointOwnerKind
{
    Host,
    Module,
    DynamicShell
}

/// <summary>Identifies the stable module or host that owns an endpoint.</summary>
public sealed record EndpointOwnershipMetadata
{
    public EndpointOwnershipMetadata(
        EndpointOwnerKind kind,
        string ownerId,
        string? shellId = null,
        int? generation = null)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "An endpoint owner kind must be defined.");
        if (string.IsNullOrWhiteSpace(ownerId))
            throw new ArgumentException("An endpoint owner is required.", nameof(ownerId));

        var normalizedShellId = string.IsNullOrWhiteSpace(shellId) ? null : shellId.Trim();
        if (kind == EndpointOwnerKind.DynamicShell)
        {
            if (normalizedShellId is null)
                throw new ArgumentException("Dynamic-shell endpoint ownership requires a shell identifier.", nameof(shellId));
            if (generation is null or < 0)
                throw new ArgumentOutOfRangeException(nameof(generation), "Dynamic-shell endpoint ownership requires a non-negative generation.");
        }
        else if (normalizedShellId is not null || generation is not null)
        {
            throw new ArgumentException("Only dynamic-shell endpoint ownership can carry a shell identifier or generation.");
        }

        Kind = kind;
        OwnerId = ownerId.Trim();
        ShellId = normalizedShellId;
        Generation = generation;
    }

    public EndpointOwnershipMetadata(string ownerId) : this(EndpointOwnerKind.Module, ownerId)
    {
    }

    public EndpointOwnerKind Kind { get; }
    public string OwnerId { get; }
    public string? ShellId { get; }
    public int? Generation { get; }

    // Retained as the concise inventory-facing name while OwnerId is the ADR's conceptual field.
    public string Owner => OwnerId;

    public static EndpointOwnershipMetadata Host(string ownerId) => new(EndpointOwnerKind.Host, ownerId);
    public static EndpointOwnershipMetadata Module(string ownerId) => new(EndpointOwnerKind.Module, ownerId);
    public static EndpointOwnershipMetadata DynamicShell(string ownerId, string shellId, int generation) =>
        new(EndpointOwnerKind.DynamicShell, ownerId, shellId, generation);
}
