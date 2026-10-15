namespace Elsa.Connections.Core.Exceptions;

/// <summary>
/// Thrown when a create would take a connection name that is already in use in the tenant.
/// </summary>
public sealed class ConnectionConflictException : InvalidOperationException
{
    public ConnectionConflictException(string name)
        : base($"A connection named '{name}' already exists in this tenant.")
    {
        Name = name;
    }

    public string Name { get; }
}
