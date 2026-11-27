namespace Elsa.Connections.Core.Exceptions;

/// <summary>
/// Thrown when a connection write loses to a concurrent writer. The causes:
/// <list type="bullet">
/// <item><description>A create would take a connection name that is already in use in the tenant.</description></item>
/// <item><description>
/// A save (create, update, revoke or recording a test result) loses a compare-and-swap against a concurrent change, or
/// the connection was deleted meanwhile; nothing was saved.
/// </description></item>
/// <item><description>
/// An update was saved, but a credential's secret was rotated, deleted or created under the same name by another
/// request before the update wrote its new value, so that value was not written. The message names the fields.
/// </description></item>
/// </list>
/// </summary>
public sealed class ConnectionConflictException : InvalidOperationException
{
    public ConnectionConflictException(string name)
        : this(name, $"A connection named '{name}' already exists in this tenant.")
    {
    }

    public ConnectionConflictException(string name, string message)
        : base(message)
    {
        Name = name;
    }

    public string Name { get; }
}
