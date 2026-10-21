using Elsa.Etl.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

#nullable disable

namespace Elsa.Etl.Persistence.EntityFrameworkCore.Migrations.Sqlite;

[DbContext(typeof(EtlDesignSqliteDbContext))]
internal sealed class EtlDesignSqliteDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
#pragma warning disable 612, 618
        modelBuilder.HasAnnotation("ProductVersion", "10.0.10");
        modelBuilder.HasAnnotation("Relational:MaxIdentifierLength", 64);

        modelBuilder.Entity("Elsa.Persistence.EntityFramework.SchemaFinalization.EfDatabaseIdentityRow", b =>
        {
            b.Property<int>("Id").HasColumnType("INTEGER");
            b.Property<string>("DatabaseIdentity").IsRequired().HasMaxLength(64).HasColumnType("TEXT");
            b.Property<string>("SchemaVersion").IsRequired().HasMaxLength(32).HasColumnType("TEXT");
            b.HasKey("Id");
            b.HasIndex("SchemaVersion");
            b.ToTable("__ElsaDatabaseIdentity_EtlDesign", (string)null);
        });

        modelBuilder.Entity("Elsa.Persistence.EntityFramework.SchemaFinalization.EfSchemaFinalizationRecordRow", b =>
        {
            b.Property<string>("Family").HasMaxLength(128).HasColumnType("TEXT");
            b.Property<string>("DatabaseIdentity").IsRequired().HasMaxLength(64).HasColumnType("TEXT");
            b.Property<string>("FinalizedVersion").IsRequired().HasMaxLength(32).HasColumnType("TEXT");
            b.Property<string>("FinishHistoryJson").IsRequired().HasColumnType("TEXT");
            b.Property<string>("FinishJson").HasColumnType("TEXT");
            b.Property<string>("HistoryJson").IsRequired().HasColumnType("TEXT");
            b.Property<string>("HoldsJson").IsRequired().HasColumnType("TEXT");
            b.Property<string>("IntentJson").HasColumnType("TEXT");
            b.Property<long>("Revision").IsConcurrencyToken().HasColumnType("INTEGER");
            b.Property<string>("SchemaVersion").IsRequired().HasMaxLength(32).HasColumnType("TEXT");
            b.HasKey("Family");
            b.HasIndex("SchemaVersion");
            b.ToTable("__ElsaSchemaFinalization_EtlDesign", (string)null);
        });

        modelBuilder.Entity("Elsa.Etl.Persistence.EntityFrameworkCore.Entities.EtlDefinitionDraftRecord", b =>
        {
            b.Property<string>("ScopeKey").HasMaxLength(64).IsRequired().HasColumnType("TEXT");
            b.Property<string>("DefinitionKey").HasMaxLength(64).IsRequired().HasColumnType("TEXT");
            b.Property<string>("DefinitionId").HasMaxLength(1200).IsRequired().HasColumnType("TEXT");
            b.Property<string>("TenantId").HasMaxLength(1200).HasColumnType("TEXT");
            b.Property<string>("PersistencePartition").HasMaxLength(1200).IsRequired().HasColumnType("TEXT");
            b.Property<long>("Revision").IsConcurrencyToken().HasColumnType("INTEGER");
            b.Property<int>("LastPublishedVersion").IsConcurrencyToken().HasColumnType("INTEGER");
            b.Property<string>("SchemaVersion").HasMaxLength(32).IsRequired().HasColumnType("TEXT");
            b.Property<string>("PipelineJson").IsRequired().HasColumnType("TEXT");
            b.Property<string>("PipelineHash").HasMaxLength(64).IsRequired().HasColumnType("TEXT");
            b.Property<long>("UpdatedAtUtcTicks").HasColumnType("INTEGER");
            b.HasKey("ScopeKey", "DefinitionKey");
            b.HasIndex("SchemaVersion");
            b.ToTable("elsa_etl_definition_drafts", (string)null);
        });

        modelBuilder.Entity("Elsa.Etl.Persistence.EntityFrameworkCore.Entities.EtlPublishedArtifactRecord", b =>
        {
            b.Property<string>("ScopeKey").HasMaxLength(64).IsRequired().HasColumnType("TEXT");
            b.Property<string>("DefinitionKey").HasMaxLength(64).IsRequired().HasColumnType("TEXT");
            b.Property<int>("DefinitionVersion").HasColumnType("INTEGER");
            b.Property<string>("DefinitionId").HasMaxLength(1200).IsRequired().HasColumnType("TEXT");
            b.Property<string>("TenantId").HasMaxLength(1200).HasColumnType("TEXT");
            b.Property<string>("PersistencePartition").HasMaxLength(1200).IsRequired().HasColumnType("TEXT");
            b.Property<string>("SchemaVersion").HasMaxLength(32).IsRequired().HasColumnType("TEXT");
            b.Property<string>("ArtifactJson").IsRequired().HasColumnType("TEXT");
            b.Property<string>("ArtifactEnvelopeHash").HasMaxLength(64).IsRequired().HasColumnType("TEXT");
            b.Property<string>("PipelineHash").HasMaxLength(64).IsRequired().HasColumnType("TEXT");
            b.Property<long>("PublishedAtUtcTicks").HasColumnType("INTEGER");
            foreach (var property in b.Metadata.GetProperties())
                property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            b.HasKey("ScopeKey", "DefinitionKey", "DefinitionVersion");
            b.HasIndex("SchemaVersion");
            b.ToTable("elsa_etl_published_artifacts", (string)null);
        });
#pragma warning restore 612, 618
    }
}
