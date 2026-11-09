namespace Elsa.Connections.Services;

internal static class ConnectionName
{
    public const int MaximumLength = 128;

    public static string Normalize(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name.Trim().ToLowerInvariant();
    }

    public static void Validate(string name)
    {
        var normalized = Normalize(name);
        if (normalized.Length > MaximumLength)
            throw new ArgumentException($"A connection name cannot exceed {MaximumLength} characters.", nameof(name));

        if (normalized.Any(character => character is not (
                >= 'a' and <= 'z' or
                >= '0' and <= '9' or
                '.' or '-' or '_')))
        {
            throw new ArgumentException(
                "A connection name may contain only letters, numbers, dot, dash and underscore.",
                nameof(name));
        }
    }

    public static string OwnedSecretName(string connectionName, string fieldName) =>
        $"connection:{Normalize(connectionName)}:{fieldName.Trim().ToLowerInvariant()}";
}
