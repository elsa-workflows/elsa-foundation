using Elsa.Etl.Persistence.EntityFrameworkCore.Entities;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Elsa.Etl.Persistence.EntityFrameworkCore;

/// <summary>Provider-neutral model for mutable ETL drafts and immutable published artifacts.</summary>
internal abstract class EtlDesignDbContext(DbContextOptions options) : DbContext(options)
{
    internal DbSet<EtlDefinitionDraftRecord> Drafts => Set<EtlDefinitionDraftRecord>();
    internal DbSet<EtlPublishedArtifactRecord> PublishedArtifacts => Set<EtlPublishedArtifactRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasElsaDefaultSchema(this);

        var drafts = modelBuilder.Entity<EtlDefinitionDraftRecord>();
        drafts.ToTable(EtlDesignEfModule.DraftTableName);
        drafts.HasKey(row => new { row.ScopeKey, row.DefinitionKey });
        drafts.Property(row => row.ScopeKey).HasMaxLength(EtlDesignEfModule.HashLength).IsRequired();
        drafts.Property(row => row.TenantId).HasMaxLength(EtlDesignEfModule.EncodedIdentityMaximumLength).IsRequired(false);
        drafts.Property(row => row.PersistencePartition).HasMaxLength(EtlDesignEfModule.EncodedIdentityMaximumLength).IsRequired();
        drafts.Property(row => row.DefinitionKey).HasMaxLength(EtlDesignEfModule.HashLength).IsRequired();
        drafts.Property(row => row.DefinitionId).HasMaxLength(EtlDesignEfModule.EncodedIdentityMaximumLength).IsRequired();
        drafts.Property(row => row.Revision).IsConcurrencyToken();
        drafts.Property(row => row.LastPublishedVersion).IsConcurrencyToken();
        drafts.Property(row => row.SchemaVersion).HasMaxLength(EtlDesignEfModule.SchemaVersionMaximumLength).IsRequired();
        drafts.Property(row => row.PipelineJson).IsRequired();
        drafts.Property(row => row.PipelineHash).HasMaxLength(EtlDesignEfModule.HashLength).IsRequired();

        var published = modelBuilder.Entity<EtlPublishedArtifactRecord>();
        published.ToTable(EtlDesignEfModule.PublishedArtifactTableName);
        published.HasKey(row => new { row.ScopeKey, row.DefinitionKey, row.DefinitionVersion });
        published.Property(row => row.ScopeKey).HasMaxLength(EtlDesignEfModule.HashLength).IsRequired();
        published.Property(row => row.TenantId).HasMaxLength(EtlDesignEfModule.EncodedIdentityMaximumLength).IsRequired(false);
        published.Property(row => row.PersistencePartition).HasMaxLength(EtlDesignEfModule.EncodedIdentityMaximumLength).IsRequired();
        published.Property(row => row.DefinitionKey).HasMaxLength(EtlDesignEfModule.HashLength).IsRequired();
        published.Property(row => row.DefinitionId).HasMaxLength(EtlDesignEfModule.EncodedIdentityMaximumLength).IsRequired();
        published.Property(row => row.SchemaVersion).HasMaxLength(EtlDesignEfModule.SchemaVersionMaximumLength).IsRequired();
        published.Property(row => row.ArtifactJson).IsRequired();
        published.Property(row => row.ArtifactEnvelopeHash).HasMaxLength(EtlDesignEfModule.HashLength).IsRequired();
        published.Property(row => row.PipelineHash).HasMaxLength(EtlDesignEfModule.HashLength).IsRequired();
        foreach (var property in published.Metadata.GetProperties())
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);

        modelBuilder.MapSchemaFinalization(EtlDesignEfModule.HistoryModuleName);
        modelBuilder.IndexSchemaVersionStamps();
        ConfigureProvider(modelBuilder);
    }

    protected abstract void ConfigureProvider(ModelBuilder modelBuilder);
}

internal sealed class EtlDesignSqliteDbContext(DbContextOptions<EtlDesignSqliteDbContext> options)
    : EtlDesignDbContext(options)
{
    internal const string ExpectedProviderName = EfProviderNames.Sqlite;
    protected override void ConfigureProvider(ModelBuilder modelBuilder) { }
}
