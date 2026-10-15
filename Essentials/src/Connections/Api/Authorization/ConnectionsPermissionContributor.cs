using Elsa.Connections.Core.Permissions;
using Elsa.Foundation.Identity.Core.Authorization;

namespace Elsa.Connections.Api.Authorization;

public sealed class ConnectionsPermissionContributor : IPermissionContributor
{
    public const string Owner = "Elsa.Connections.Api";

    public string OwnerId => Owner;

    public string ContributorType => typeof(ConnectionsPermissionContributor).FullName!;

    public IEnumerable<Permission> Contribute() =>
    [
        Permission(ConnectionsPermissions.Read, "Read connections", "Read connection metadata and descriptors."),
        Permission(
            ConnectionsPermissions.Use,
            "Use connections",
            "Bind connections in workflows and publish definitions that reference them.",
            new HashSet<string>(StringComparer.Ordinal) { ConnectionsPermissions.Read }),
        Permission(
            ConnectionsPermissions.Manage,
            "Manage connections",
            "Create, update, revoke and delete connections.",
            new HashSet<string>(StringComparer.Ordinal) { ConnectionsPermissions.Read, ConnectionsPermissions.Use }),
        Permission(ConnectionsPermissions.Test, "Test connections", "Test a connection without disclosing credentials.")
    ];

    private Permission Permission(string key, string displayName, string description, IReadOnlySet<string>? implies = null) =>
        new(key, displayName, "Connections", description, implies)
        {
            OwnerId = OwnerId,
            ContributorType = ContributorType
        };
}
