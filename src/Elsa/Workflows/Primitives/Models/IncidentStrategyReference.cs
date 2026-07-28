namespace Elsa.Workflows.Primitives.Models;

/// <summary>
/// Identifies one exact incident-strategy implementation without coupling durable state to a CLR type.
/// </summary>
public sealed class IncidentStrategyReference : IEquatable<IncidentStrategyReference>
{
    public IncidentStrategyReference(string alias, string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        Alias = alias;
        Version = version;
    }

    public string Alias { get; }
    public string Version { get; }

    public bool Equals(IncidentStrategyReference? other) =>
        other is not null &&
        StringComparer.OrdinalIgnoreCase.Equals(Alias, other.Alias) &&
        StringComparer.Ordinal.Equals(Version, other.Version);

    public override bool Equals(object? obj) => Equals(obj as IncidentStrategyReference);

    public override int GetHashCode() =>
        HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(Alias),
            StringComparer.Ordinal.GetHashCode(Version));

    public override string ToString() => $"{Alias}/{Version}";
}
