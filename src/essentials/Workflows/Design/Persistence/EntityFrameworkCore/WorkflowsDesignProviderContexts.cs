using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore;

public sealed class WorkflowsDesignSqliteDbContext(DbContextOptions<WorkflowsDesignSqliteDbContext> options) : WorkflowsDesignDbContext(options)
{
    public const string ExpectedProviderName = EfProviderNames.Sqlite;
    protected override void ConfigureProvider(ModelBuilder modelBuilder) { ConfigureDateTime(modelBuilder, "TEXT"); ConfigureText(modelBuilder, "TEXT"); ApplyOrdinalCollation(modelBuilder, ExpectedProviderName); }
}

public sealed class WorkflowsDesignSqlServerDbContext(DbContextOptions<WorkflowsDesignSqlServerDbContext> options) : WorkflowsDesignDbContext(options)
{
    public const string ExpectedProviderName = EfProviderNames.SqlServer;
    protected override void ConfigureProvider(ModelBuilder modelBuilder) { ConfigureDateTime(modelBuilder, "datetimeoffset"); ConfigureText(modelBuilder, "nvarchar(max)"); ApplyOrdinalCollation(modelBuilder, ExpectedProviderName); }
}

public sealed class WorkflowsDesignPostgreSqlDbContext(DbContextOptions<WorkflowsDesignPostgreSqlDbContext> options) : WorkflowsDesignDbContext(options)
{
    public const string ExpectedProviderName = EfProviderNames.PostgreSql;
    protected override void ConfigureProvider(ModelBuilder modelBuilder) { ConfigureDateTime(modelBuilder, "timestamp with time zone"); ConfigureText(modelBuilder, "text"); ApplyOrdinalCollation(modelBuilder, ExpectedProviderName); }
}

public sealed class WorkflowsDesignMySqlDbContext(DbContextOptions<WorkflowsDesignMySqlDbContext> options) : WorkflowsDesignDbContext(options)
{
    public const string ExpectedProviderName = EfProviderNames.MySql;
    private const string CharacterSetAnnotation = "MySQL:Charset";
    public const string CharacterSet = "utf8mb4";

    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        ConfigureDateTime(modelBuilder, "datetime(6)");
        ConfigureText(modelBuilder, "longtext");
        ConvertDateTimeOffsetsToUtcDateTime(modelBuilder);
        modelBuilder.Model.SetAnnotation(CharacterSetAnnotation, CharacterSet);
        ApplyOrdinalCollation(modelBuilder, ExpectedProviderName);
    }

    /// <summary>
    /// The datetime(6) column keeps microseconds, but the Oracle provider's DateTimeOffset reader drops the fractional
    /// seconds, so a LastModifiedAt concurrency token read back no longer matches the stored value and every update or
    /// delete of the row fails the check (#2204). Its DateTime reader keeps them. The converter stores the UTC
    /// instant truncated to the column's microseconds, so the original value the check sends is the stored one even
    /// for a row this scope wrote with 100 ns ticks.
    /// </summary>
    private static void ConvertDateTimeOffsetsToUtcDateTime(ModelBuilder modelBuilder)
    {
        var converter = new ValueConverter<DateTimeOffset, DateTime>(
            value => new DateTime(value.UtcTicks - value.UtcTicks % TimeSpan.TicksPerMicrosecond, DateTimeKind.Utc),
            value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)));
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        foreach (var property in entityType.GetProperties().Where(x => x.ClrType == typeof(DateTimeOffset) || x.ClrType == typeof(DateTimeOffset?)))
            property.SetValueConverter(converter);
    }
}
