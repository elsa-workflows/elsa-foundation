namespace Elsa.Secrets.Core.Contracts;

/// <summary>
/// Names the durable <see cref="ISecretRepository"/> implementation selected for a shell.
/// Persistence registrations record this so selecting incompatible implementations fails
/// with an order-independent diagnostic instead of silent last-registration-wins.
/// </summary>
public sealed record SecretRepositoryBackend(string Name)
{
    public static void EnsureCompatible(string? existing, string incoming)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(incoming);
        if (existing is not null && !string.Equals(existing, incoming, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"ISecretRepository is already bound to '{existing}' and cannot also bind '{incoming}'. " +
                "Enable only one Secrets persistence feature.");
        }
    }
}
