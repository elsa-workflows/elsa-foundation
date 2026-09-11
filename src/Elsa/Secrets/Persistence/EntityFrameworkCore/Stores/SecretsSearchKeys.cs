using Elsa.Secrets.Core.Models;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Stores;

public static class SecretsSearchKeys
{
    public static string SearchKey(string value) => value.ToUpperInvariant();

    // Keep the Phase 1 projection byte-compatible with databases created by the already-merged
    // Initial migrations. Phase 3 owns the provider-neutral, versioned Unicode projection.
    public static string LookupKey(string value) => value.ToUpperInvariant();

    public static string StatusValue(SecretStatus status) => status.ToString().ToLowerInvariant();
}
