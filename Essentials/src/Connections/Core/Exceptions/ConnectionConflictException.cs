namespace Elsa.Connections.Core.Exceptions;

/// <summary>
/// Thrown when a create would take a connection name that is already in use, or when a save
/// loses a compare-and-swap against a concurrent change.
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
