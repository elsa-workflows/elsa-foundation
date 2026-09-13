using System.Globalization;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;

/// <summary>Preserves the public Groundwork Identity revision shape while storage is EF-backed.</summary>
internal static class IdentityProviderConfigurationRevisionCodec
{
    public static string FromVersion(long version)
    {
        if (version <= 0)
            throw new ArgumentOutOfRangeException(nameof(version), "Identity revisions must be positive.");
        return "gw:" + version.ToString("D20", CultureInfo.InvariantCulture);
    }

    public static bool TryGetVersion(string? value, out long version)
    {
        version = default;
        return value is { Length: 23 } &&
               value.StartsWith("gw:", StringComparison.Ordinal) &&
               long.TryParse(value.AsSpan(3), NumberStyles.None, CultureInfo.InvariantCulture, out version) &&
               version > 0 &&
               string.Equals(value, FromVersion(version), StringComparison.Ordinal);
    }
}
