using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore;

public sealed class PublishingSnapshotReviewSqliteDbContext(DbContextOptions<PublishingSnapshotReviewSqliteDbContext> options)
    : PublishingSnapshotReviewDbContext(options)
{
    public const string ExpectedProviderName = Elsa.Persistence.EntityFramework.EfProviderNames.Sqlite;
    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        PublishingSnapshotReviewProviderModel.ConfigureDateTime(modelBuilder, "INTEGER");
        ApplyOrdinalCollation(modelBuilder, ExpectedProviderName);
    }
}

public sealed class PublishingSnapshotReviewSqlServerDbContext(DbContextOptions<PublishingSnapshotReviewSqlServerDbContext> options)
    : PublishingSnapshotReviewDbContext(options)
{
    public const string ExpectedProviderName = Elsa.Persistence.EntityFramework.EfProviderNames.SqlServer;
    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        PublishingSnapshotReviewProviderModel.ConfigureDateTime(modelBuilder, "bigint");
        ApplyOrdinalCollation(modelBuilder, ExpectedProviderName);
    }
}

public sealed class PublishingSnapshotReviewPostgreSqlDbContext(DbContextOptions<PublishingSnapshotReviewPostgreSqlDbContext> options)
    : PublishingSnapshotReviewDbContext(options)
{
    public const string ExpectedProviderName = Elsa.Persistence.EntityFramework.EfProviderNames.PostgreSql;
    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        modelBuilder.Model.RemoveAnnotation("Npgsql:ValueGenerationStrategy");
        PublishingSnapshotReviewProviderModel.ConfigureDateTime(modelBuilder, "bigint");
        ApplyOrdinalCollation(modelBuilder, ExpectedProviderName);
    }
}

public sealed class PublishingSnapshotReviewMySqlDbContext(DbContextOptions<PublishingSnapshotReviewMySqlDbContext> options)
    : PublishingSnapshotReviewDbContext(options)
{
    private const string CharacterSetAnnotation = "MySQL:Charset";
    public const string ExpectedProviderName = Elsa.Persistence.EntityFramework.EfProviderNames.MySql;
    public const string CharacterSet = "utf8mb4";

    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        modelBuilder.Model.SetAnnotation(CharacterSetAnnotation, CharacterSet);
        PublishingSnapshotReviewProviderModel.ConfigureDateTime(modelBuilder, "bigint");
        ApplyOrdinalCollation(modelBuilder, ExpectedProviderName);
    }
}

file static class PublishingSnapshotReviewProviderModel
{
    public static void ConfigureDateTime(ModelBuilder modelBuilder, string type) =>
        modelBuilder.Entity<PublicationSnapshotReviewEntity>().Property(row => row.ExpiresAt).HasColumnType(type);
}
