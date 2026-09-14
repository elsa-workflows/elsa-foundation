using Microsoft.EntityFrameworkCore;

namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// Last-line defense against applying the wrong dialect. The host still selects the derived
/// context; this guard refuses a Sqlite file opened through a PostgreSQL-derived context.
/// </summary>
public static class EfProviderGuard
{
    public static void Ensure(DbContext context, string expectedProviderName)
    {
        ArgumentNullException.ThrowIfNull(context);
        Ensure(context.Database.ProviderName, expectedProviderName, context.GetType().Name);
    }

    public static void Ensure(string? actualProviderName, string expectedProviderName, string owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedProviderName);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        if (string.Equals(actualProviderName, expectedProviderName, StringComparison.Ordinal))
            return;

        throw new InvalidOperationException(
            $"{owner} refuses provider '{actualProviderName ?? "<none>"}'. Expected '{expectedProviderName}'.");
    }
}
