namespace Elsa.Cluster.Core.Models;

/// <summary>
/// The identity of one run of a host (FR-004). A host gets a new incarnation each time its process starts and each
/// time it rejoins after a lapse. It is opaque: consumers compare incarnations only for equality.
/// </summary>
public sealed record MemberIncarnation
{
    public MemberIncarnation(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    /// <summary>Mints an incarnation no other run has used.</summary>
    public static MemberIncarnation New() => new(Guid.NewGuid().ToString("N"));

    public override string ToString() => Value;
}
