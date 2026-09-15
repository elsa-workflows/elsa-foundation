using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore;

public sealed class PublishingSnapshotReviewSqliteDbContext(DbContextOptions<PublishingSnapshotReviewSqliteDbContext> options)
    : PublishingSnapshotReviewDbContext(options)
{
    public const string ExpectedProviderName = Elsa.Persistence.EntityFramework.EfProviderNames.Sqlite;
    protected override void ConfigureProvider(ModelBuilder modelBuilder) => PublishingSnapshotReviewProviderModel.ConfigureDateTime(modelBuilder, "INTEGER");
}

public sealed class PublishingSnapshotReviewSqlServerDbContext(DbContextOptions<PublishingSnapshotReviewSqlServerDbContext> options)
    : PublishingSnapshotReviewDbContext(options)
{
    public const string ExpectedProviderName = Elsa.Persistence.EntityFramework.EfProviderNames.SqlServer;
    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        PublishingSnapshotReviewProviderModel.ConfigureDateTime(modelBuilder, "bigint");
        var entity = modelBuilder.Entity<PublicationSnapshotReviewEntity>();
        foreach (var property in new[] { nameof(PublicationSnapshotReviewEntity.PreflightToken), nameof(PublicationSnapshotReviewEntity.DefinitionId), nameof(PublicationSnapshotReviewEntity.SlotName), nameof(PublicationSnapshotReviewEntity.TenantId) })
            entity.Property(property).UseCollation("Latin1_General_BIN2");
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
    }
}

public sealed class PublishingSnapshotReviewMySqlDbContext(DbContextOptions<PublishingSnapshotReviewMySqlDbContext> options)
    : PublishingSnapshotReviewDbContext(options)
{
    private const string CharacterSetAnnotation = "MySQL:Charset";
    private const string CollationAnnotation = "MySQL:Collation";
    public const string ExpectedProviderName = Elsa.Persistence.EntityFramework.EfProviderNames.MySql;
    public const string CharacterSet = "utf8mb4";
    public const string Collation = "utf8mb4_0900_bin";

    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        modelBuilder.Model.SetAnnotation(CharacterSetAnnotation, CharacterSet);
        modelBuilder.UseCollation(Collation);
        var entity = modelBuilder.Entity<PublicationSnapshotReviewEntity>();
        entity.Metadata.SetAnnotation(CollationAnnotation, Collation);
        foreach (var property in new[] { nameof(PublicationSnapshotReviewEntity.PreflightToken), nameof(PublicationSnapshotReviewEntity.DefinitionId), nameof(PublicationSnapshotReviewEntity.SlotName), nameof(PublicationSnapshotReviewEntity.TenantId) })
            entity.Property(property).Metadata.SetAnnotation(CollationAnnotation, Collation);
        PublishingSnapshotReviewProviderModel.ConfigureDateTime(modelBuilder, "bigint");
    }
}

file static class PublishingSnapshotReviewProviderModel
{
    public static void ConfigureDateTime(ModelBuilder modelBuilder, string type) =>
        modelBuilder.Entity<PublicationSnapshotReviewEntity>().Property(row => row.ExpiresAt).HasColumnType(type);
}
