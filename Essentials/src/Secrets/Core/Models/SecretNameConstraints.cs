namespace Elsa.Secrets.Core.Models;

/// <summary>Portable persistence bounds for normalized secret identities.</summary>
public static class SecretNameConstraints
{
    public const int MaximumLength = 256;

    /// <summary>
    /// The most characters a secret's tenant id, normalized name and id may have together. A store keys a deleted
    /// secret by all three, its id being the tombstone that frees the name, and 450 UTF-16 characters are the 900
    /// bytes SQL Server allows a clustered key. A generated id has <see cref="Elsa.Primitives.Identity.ShortIdentityGenerator.Length"/>
    /// characters (11), so a tenant id and a name may have 439 between them.
    /// </summary>
    public const int MaximumKeyLength = 450;

    public static void Validate(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Length > MaximumLength)
        {
            throw new ArgumentException(
                $"A secret name cannot exceed {MaximumLength} characters.",
                nameof(name));
        }

        if (name.Any(character => character is not (
                >= 'a' and <= 'z' or
                >= '0' and <= '9' or
                '.' or '-' or '_' or ':')))
        {
            throw new ArgumentException(
                "A persisted secret name must already be normalized and contain only letters, numbers, dot, dash, underscore, and colon.",
                nameof(name));
        }
    }

    /// <summary>
    /// Refuses a new secret whose tenant id, name and id together exceed <see cref="MaximumKeyLength"/>. Checked when a
    /// secret is created, on every provider, so a secret created since this bound can always be deleted: on SQL Server the
    /// row its delete writes would otherwise be refused.
    /// </summary>
    public static void ValidateKeyLength(string tenantId, string name, string id)
    {
        var available = MaximumKeyLength - tenantId.Length - id.Length;
        if (name.Length <= available)
            return;

        throw new ArgumentException(
            $"Secret name cannot exceed {Math.Max(available, 0)} characters in this tenant: a secret's tenant id, name " +
            $"and id together cannot exceed {MaximumKeyLength} characters, or the secret could not be deleted.",
            nameof(name));
    }
}
