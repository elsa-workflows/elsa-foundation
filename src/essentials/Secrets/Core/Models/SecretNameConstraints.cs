namespace Elsa.Secrets.Core.Models;

/// <summary>Portable persistence bounds for normalized secret identities.</summary>
public static class SecretNameConstraints
{
    public const int MaximumLength = 256;

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
}
