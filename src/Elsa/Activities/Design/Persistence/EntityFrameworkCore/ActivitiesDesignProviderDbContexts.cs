using Elsa.Activities.Design.Persistence.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Activities.Design.Persistence.EntityFrameworkCore;

public sealed class ActivitiesDesignSqliteDbContext(DbContextOptions<ActivitiesDesignSqliteDbContext> options) : ActivitiesDesignDbContext(options)
{
    public const string ExpectedProviderName = Elsa.Persistence.EntityFramework.EfProviderNames.Sqlite;
    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        modelBuilder.UseCollation("BINARY");
        modelBuilder.Model.FindEntityType(typeof(ActivityDefinition))!.FindProperty("ConcurrencyToken")!.SetColumnType("BLOB");
        modelBuilder.Entity<ActivityDefinitionManagementProjectionRevision>().Property(x => x.ContentAuthorityIsValid)
            .HasComputedColumnSql(ActivityAuthorityValiditySql.Compose(ActivityAuthorityValiditySql.Sqlite(), "1", "0"), stored: false);
    }
}

public sealed class ActivitiesDesignSqlServerDbContext(DbContextOptions<ActivitiesDesignSqlServerDbContext> options) : ActivitiesDesignDbContext(options)
{
    public const string ExpectedProviderName = Elsa.Persistence.EntityFramework.EfProviderNames.SqlServer;
    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        modelBuilder.UseCollation("Latin1_General_100_BIN2");
        modelBuilder.Model.GetEntityTypes().ToList().ForEach(entity => entity.FindProperty("ConcurrencyToken")?.SetColumnType("varbinary(16)"));
        modelBuilder.Entity<ActivityDefinitionManagementProjectionRevision>().Property(x => x.ContentAuthorityIsValid)
            .HasComputedColumnSql(ActivityAuthorityValiditySql.Compose(ActivityAuthorityValiditySql.SqlServer(), "CONVERT(bit, 1)", "CONVERT(bit, 0)"), stored: false);
        // SQL Server limits an index key to 900 bytes (nvarchar uses two bytes per
        // character). Preserve the provider-neutral lengths elsewhere, but proportionally
        // bound only composite SQL Server indexes so every declared key is model-valid.
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        foreach (var index in entity.GetIndexes())
        {
            var strings = index.Properties.Where(x => x.ClrType == typeof(string)).ToArray();
            var total = strings.Sum(x => x.GetMaxLength() ?? 450);
            if (total <= 450) continue;
            // TenantScopeKey is a fixed-width, provider-neutral discriminator. Keep its full
            // 66-character representation so global and opaque tenant scopes remain lossless;
            // proportionally bound only the other indexed strings to the remaining budget.
            var fixedLength = strings.Where(x => x.Name == "TenantScopeKey").Sum(x => x.GetMaxLength() ?? 450);
            var variable = strings.Where(x => x.Name != "TenantScopeKey").ToArray();
            var variableTotal = variable.Sum(x => x.GetMaxLength() ?? 450);
            foreach (var property in variable)
            {
                var current = property.GetMaxLength() ?? 450;
                property.SetMaxLength(Math.Max(1, current * (450 - fixedLength) / Math.Max(1, variableTotal)));
            }
        }
    }
}

public sealed class ActivitiesDesignPostgreSqlDbContext(DbContextOptions<ActivitiesDesignPostgreSqlDbContext> options) : ActivitiesDesignDbContext(options)
{
    public const string ExpectedProviderName = Elsa.Persistence.EntityFramework.EfProviderNames.PostgreSql;
    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        modelBuilder.UseCollation("C");
        modelBuilder.Model.GetEntityTypes().ToList().ForEach(entity => entity.FindProperty("ConcurrencyToken")?.SetColumnType("bytea"));
        modelBuilder.Entity<ActivityDefinitionManagementProjectionRevision>().Property(x => x.ContentAuthorityIsValid)
            .HasComputedColumnSql(ActivityAuthorityValiditySql.Compose(ActivityAuthorityValiditySql.PostgreSql(), "true", "false"), stored: true);
    }
}

public sealed class ActivitiesDesignMySqlDbContext(DbContextOptions<ActivitiesDesignMySqlDbContext> options) : ActivitiesDesignDbContext(options)
{
    public const string ExpectedProviderName = Elsa.Persistence.EntityFramework.EfProviderNames.MySql;
    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        modelBuilder.UseCollation("utf8mb4_0900_bin");
        modelBuilder.Model.GetEntityTypes().ToList().ForEach(entity => entity.FindProperty("ConcurrencyToken")?.SetColumnType("varbinary(16)"));
        modelBuilder.Entity<ActivityDefinitionManagementProjectionRevision>().Property(x => x.ContentAuthorityIsValid)
            .HasComputedColumnSql(ActivityAuthorityValiditySql.Compose(ActivityAuthorityValiditySql.MySql(), "1", "0"), stored: false);
    }
}
