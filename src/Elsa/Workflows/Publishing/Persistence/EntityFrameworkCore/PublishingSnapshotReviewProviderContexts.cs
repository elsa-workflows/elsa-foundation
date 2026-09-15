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
        PublishingSnapshotReviewProviderModel.ConfigureSqlServerCollation(modelBuilder, "Latin1_General_BIN2");
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
        PublishingSnapshotReviewProviderModel.ConfigureMySqlCollation(modelBuilder, Collation, CollationAnnotation);
        PublishingSnapshotReviewProviderModel.ConfigureDateTime(modelBuilder, "bigint");
    }
}

file static class PublishingSnapshotReviewProviderModel
{
    public static void ConfigureDateTime(ModelBuilder modelBuilder, string type) =>
        modelBuilder.Entity<PublicationSnapshotReviewEntity>().Property(row => row.ExpiresAt).HasColumnType(type);

    public static void ConfigureSqlServerCollation(ModelBuilder modelBuilder, string collation)
    {
        ConfigureStringCollation<PublicationSnapshotReviewEntity>(modelBuilder, collation,
            nameof(PublicationSnapshotReviewEntity.PreflightToken), nameof(PublicationSnapshotReviewEntity.DefinitionId), nameof(PublicationSnapshotReviewEntity.SlotName), nameof(PublicationSnapshotReviewEntity.TenantId));
        ConfigureStringCollation<PublicationPolicyEntity>(modelBuilder, collation,
            nameof(PublicationPolicyEntity.PolicyKey), nameof(PublicationPolicyEntity.WorkflowDefinitionId), nameof(PublicationPolicyEntity.TenantId), nameof(PublicationPolicyEntity.DefaultSlotName));
        ConfigureStringCollation<PublicationProjectionIntentEntity>(modelBuilder, collation,
            nameof(PublicationProjectionIntentEntity.IntentId), nameof(PublicationProjectionIntentEntity.PublicationId), nameof(PublicationProjectionIntentEntity.ProjectionKind), nameof(PublicationProjectionIntentEntity.TenantId));
    }

    public static void ConfigureMySqlCollation(ModelBuilder modelBuilder, string collation, string annotation)
    {
        foreach (var entity in new[]
        {
            modelBuilder.Entity<PublicationSnapshotReviewEntity>().Metadata,
            modelBuilder.Entity<PublicationPolicyEntity>().Metadata,
            modelBuilder.Entity<PublicationProjectionIntentEntity>().Metadata
        })
        {
            entity.SetAnnotation(annotation, collation);
            foreach (var property in entity.GetProperties().Where(property => property.ClrType == typeof(string)))
                property.SetAnnotation(annotation, collation);
        }
    }

    private static void ConfigureStringCollation<TEntity>(ModelBuilder modelBuilder, string collation, params string[] properties)
        where TEntity : class
    {
        var entity = modelBuilder.Entity<TEntity>();
        foreach (var property in properties)
            entity.Property(property).UseCollation(collation);
    }
}
