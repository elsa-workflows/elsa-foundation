using System.Security.Cryptography;
using System.Text;

namespace Elsa.Persistence.Groundwork.Targets;

/// <summary>
/// One host declaration of an admitted physical Groundwork store: a name, the provider leaf that opens
/// it, and a non-reversible fingerprint of the connection it addresses.
/// <para>
/// The connection string itself is never retained here and never appears in a diagnostic. Two
/// declarations of the same target name are compared by <see cref="ProviderIdentity"/> and
/// <see cref="ConnectionFingerprint"/> so a repeated composition is recognized as idempotent while a
/// second, different connection is rejected loudly instead of being silently discarded.
/// </para>
/// </summary>
/// <param name="Name">The canonical target name, already run through <see cref="GroundworkTargetNames.Normalize"/>.</param>
/// <param name="ProviderIdentity">The concrete provider leaf identity, e.g. <c>sqlite</c> or <c>postgresql</c>.</param>
/// <param name="ConnectionFingerprint">A truncated SHA-256 of the connection string; safe to log.</param>
public sealed record GroundworkTargetDeclaration(
    string Name,
    string ProviderIdentity,
    string ConnectionFingerprint)
{
    /// <summary>Creates a declaration, canonicalizing the name and fingerprinting the connection.</summary>
    public static GroundworkTargetDeclaration Create(
        string? name,
        string providerIdentity,
        string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        return new(
            GroundworkTargetNames.Normalize(name),
            providerIdentity,
            Fingerprint(connectionString));
    }

    /// <summary>Whether <paramref name="other"/> addresses the same provider and connection as this one.</summary>
    public bool AddressesSameStoreAs(GroundworkTargetDeclaration other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return string.Equals(ProviderIdentity, other.ProviderIdentity, StringComparison.Ordinal)
               && string.Equals(ConnectionFingerprint, other.ConnectionFingerprint, StringComparison.Ordinal);
    }

    /// <summary>A log-safe description naming the provider and connection fingerprint but never the connection.</summary>
    public string Describe() => $"provider '{ProviderIdentity}', connection fingerprint '{ConnectionFingerprint}'";

    private static string Fingerprint(string connectionString) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(connectionString)))[..16];
}
