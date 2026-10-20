namespace Elsa.Connections.Core.Permissions;

/// <summary>
/// The Connections administration rights. Listing connections and binding one in a workflow are not Connections
/// rights: <c>workflows/design:read</c> implies the first and <c>workflows/design:manage</c> the second.
/// </summary>
public static class ConnectionsPermissions
{
    public const string Manage = "connections:manage";
    public const string BindSecret = "connections:bind-secret";
    public const string Test = "connections:test";
}
