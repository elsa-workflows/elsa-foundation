namespace Elsa.Connections.Core.Permissions;

/// <summary>
/// The Connections administration rights. There is no Connections read or use right: listing connections is implied
/// by <c>workflows/design:read</c> or <see cref="Manage"/>, and binding one in a workflow by
/// <c>workflows/design:manage</c>.
/// </summary>
public static class ConnectionsPermissions
{
    public const string Manage = "connections:manage";
    public const string BindSecret = "connections:bind-secret";
    public const string Test = "connections:test";
}
