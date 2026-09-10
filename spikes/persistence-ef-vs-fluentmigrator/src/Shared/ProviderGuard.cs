namespace Elsa.Persistence.Spike;

/// <summary>Refuse to apply the wrong dialect to a live connection.</summary>
public static class ProviderGuard
{
    public const string SqliteProviderName = "Microsoft.EntityFrameworkCore.Sqlite";
    public const string NpgsqlProviderName = "Npgsql.EntityFrameworkCore.PostgreSQL";

    public static void Ensure(string? actualProviderName, string expectedProviderName, string owner)
    {
        if (string.Equals(actualProviderName, expectedProviderName, StringComparison.Ordinal))
            return;

        throw new InvalidOperationException(
            $"{owner} refuses provider '{actualProviderName ?? "<none>"}'. Expected '{expectedProviderName}'.");
    }
}
