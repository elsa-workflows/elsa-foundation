namespace Elsa.Http.Core.Models;

/// <summary>The owner classification for a workflow HTTP route.</summary>
public enum HttpRouteOwnerKind
{
    Host,
    Module,
    DynamicShell
}

/// <summary>
/// Immutable ownership identity carried by a dynamic route. The route table uses the dynamic-shell form for
/// workflow-authored routes; host/module forms are accepted by the manifest validator so one collision policy can
/// compare all route owners.
/// </summary>
public sealed record HttpRouteOwnershipMetadata
{
    public HttpRouteOwnershipMetadata(
        HttpRouteOwnerKind ownerKind,
        string ownerId,
        string? shellId = null,
        long? generation = null)
    {
        if (!Enum.IsDefined(ownerKind))
            throw new ArgumentOutOfRangeException(nameof(ownerKind), ownerKind, "A route owner kind must be defined.");
        if (string.IsNullOrWhiteSpace(ownerId))
            throw new ArgumentException("A route owner is required.", nameof(ownerId));

        var normalizedShellId = string.IsNullOrWhiteSpace(shellId) ? null : shellId.Trim();
        if (ownerKind == HttpRouteOwnerKind.DynamicShell)
        {
            if (normalizedShellId is null)
                throw new ArgumentException("Dynamic-shell routes require a shell identifier.", nameof(shellId));
            if (generation is null or < 0)
                throw new ArgumentOutOfRangeException(nameof(generation), "Dynamic-shell routes require a non-negative generation.");
        }
        else if (normalizedShellId is not null || generation is not null)
        {
            throw new ArgumentException("Shell and generation identity are valid only for dynamic-shell routes.");
        }

        OwnerKind = ownerKind;
        OwnerId = ownerId.Trim();
        ShellId = normalizedShellId;
        Generation = generation;
    }

    public HttpRouteOwnerKind OwnerKind { get; }
    public string OwnerId { get; }
    public string? ShellId { get; }
    public long? Generation { get; }

    public static HttpRouteOwnershipMetadata Host(string ownerId) => new(HttpRouteOwnerKind.Host, ownerId);
    public static HttpRouteOwnershipMetadata Module(string ownerId) => new(HttpRouteOwnerKind.Module, ownerId);
    public static HttpRouteOwnershipMetadata DynamicShell(string ownerId, string shellId, long generation) =>
        new(HttpRouteOwnerKind.DynamicShell, ownerId, shellId, generation);

    public override string ToString() => OwnerKind == HttpRouteOwnerKind.DynamicShell
        ? $"{OwnerId} ({OwnerKind}, shell={ShellId}, generation={Generation})"
        : $"{OwnerId} ({OwnerKind})";
}
