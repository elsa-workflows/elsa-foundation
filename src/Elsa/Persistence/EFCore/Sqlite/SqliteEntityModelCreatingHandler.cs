using Elsa.Persistence.EFCore.Contracts;
using Elsa.Primitives.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Elsa.Persistence.EFCore.Sqlite;

/// <summary>
/// Represents a class that handles entity model creation for SQLite databases.
/// </summary>
public sealed class SqliteEntityModelCreatingHandler : IEntityModelCreatingHandler
{
    /// <inheritdoc />
    public void Handle(ElsaDbContextBase dbContext, ModelBuilder modelBuilder, IMutableEntityType entityType)
    {
        if (!dbContext.Database.IsSqlite())
            return;

        // SQLite does not have proper support for DateTimeOffset via Entity Framework Core, see the limitations
        // here: https://docs.microsoft.com/en-us/ef/core/providers/sqlite/limitations#query-limitations
        var properties = entityType.ClrType.GetProperties().Where(p => p.PropertyType == typeof(DateTimeOffset) || p.PropertyType == typeof(DateTimeOffset?));

        foreach (var property in properties)
        {
            modelBuilder
                .Entity(entityType.Name)
                .Property(property.Name)
                .HasConversion(new DateTimeOffsetToStringConverter());
        }

        // RowNumber is a bigint IDENTITY on production providers (Postgres/SqlServer/MySql)
        // but SQLite cannot auto-increment a non-PK column. Strip it from the SQLite model
        // entirely so migrations don't generate the column and queries don't reference it.
        if (typeof(Entity).IsAssignableFrom(entityType.ClrType))
        {
            modelBuilder
                .Entity(entityType.ClrType)
                .Ignore(nameof(Entity.RowNumber));
        }
    }
}