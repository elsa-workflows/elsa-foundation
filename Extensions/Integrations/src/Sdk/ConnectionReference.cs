namespace Elsa.Integrations.Sdk;

/// <summary>
/// The only connection value that may appear in workflow JSON: the connection's technical name
/// and, optionally, the connection type it must match. Same shape philosophy as
/// <c>Elsa.Secrets.Core.Models.SecretReference</c>. Persistence of connection definitions is
/// <c>Elsa.Connections</c> (plan D6), not this SDK.
/// </summary>
public sealed record ConnectionReference(string Name, string? TypeName = null)
{
    public string Name { get; } = string.IsNullOrWhiteSpace(Name)
        ? throw new ArgumentException("A connection reference needs a name.", nameof(Name))
        : Name.Trim();

    public string? TypeName { get; } = string.IsNullOrWhiteSpace(TypeName) ? null : TypeName.Trim();

    public override string ToString() => TypeName is null ? Name : $"{Name} ({TypeName})";
}
