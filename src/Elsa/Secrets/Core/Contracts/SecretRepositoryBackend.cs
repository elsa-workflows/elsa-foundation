namespace Elsa.Secrets.Core.Contracts;

/// <summary>
/// Names the durable <see cref="ISecretRepository"/> implementation selected for a shell.
/// Groundwork and EF both record this so enabling both features fails with an
/// order-independent diagnostic instead of silent last-registration-wins.
/// </summary>
public sealed record SecretRepositoryBackend(string Name)
{
    public const string Groundwork = "groundwork";
    public const string EntityFramework = "entity-framework";

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
