using Elsa.Secrets.Core.Models;

namespace Elsa.Integrations.Sdk.Connections;

/// <summary>
/// Runtime view of a service connection instance. This is not the persistence schema: that lives
/// on <c>Elsa.Connections</c> (plan D6, <c>docs/plans/connections-and-secrets-model.md</c>). The
/// SDK consumes the fields an activity needs at resolve time.
/// </summary>
public sealed record ConnectionSnapshot
{
    public required string TenantId { get; init; }

    public required string Name { get; init; }

    public required string TypeName { get; init; }

    public required string AuthScheme { get; init; }

    public IReadOnlyDictionary<string, string> Settings { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, SecretReference> SecretFields { get; init; } =
        new Dictionary<string, SecretReference>(StringComparer.Ordinal);

    /// <summary>Hosts this connection may attach material to. Empty means no extra restriction beyond the scheme.</summary>
    public IReadOnlyList<string> EgressAllowlist { get; init; } = [];

    /// <summary>v1: always <see cref="ConnectionOwners.Tenant"/>.</summary>
    public string Owner { get; init; } = ConnectionOwners.Tenant;

    /// <summary>v1: always <see cref="ConnectionManagedBy.Studio"/>.</summary>
    public string ManagedBy { get; init; } = ConnectionManagedBy.Studio;
}
