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
        Permission(
            ConnectionsPermissions.Manage,
            "Manage connections",
            "Create, update, revoke and delete connections. Does not grant secret binding."),
        Permission(
            ConnectionsPermissions.BindSecret,
            "Bind connection secrets",
            "Bind an existing tenant secret or a named credential to a connection field."),
        Permission(ConnectionsPermissions.Test, "Test connections", "Test a connection without disclosing credentials.")
    ];

    private Permission Permission(string key, string displayName, string description) =>
        new(key, displayName, "Connections", description)
        {
            OwnerId = OwnerId,
            ContributorType = ContributorType
        };
}
