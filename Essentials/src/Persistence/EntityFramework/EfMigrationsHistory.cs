using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// Per-module EF migrations history table names. The default
/// <c>__EFMigrationsHistory</c> collides when two modules share one database.
/// </summary>
public static class EfMigrationsHistory
{
    public const string TablePrefix = "__EFMigrationsHistory_";

    /// <summary>Installs migration-history compatibility before context options are frozen by pooling.</summary>
    public static void EnsureAdded(DbContextOptionsBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (builder.Options.FindExtension<EfMigrationHistoryOptionsExtension>() is null)
            ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(new EfMigrationHistoryOptionsExtension());
    }

    public static string TableName(string module)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(module);
        if (module.IndexOfAny(['/', '\\', '.', ' ']) >= 0)
        {
            throw new ArgumentException(
                "A migrations history module name must be a single identifier without path or whitespace characters.",
                nameof(module));
        }

        return TablePrefix + module;
    }
}
