using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Configuration;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore;

/// <summary>Provider-neutral EF model for immutable publication snapshot-review authorities.</summary>
public abstract class PublishingSnapshotReviewDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<PublicationSnapshotReviewEntity> SnapshotReviews => Set<PublicationSnapshotReviewEntity>();
    public DbSet<PublicationPolicyEntity> Policies => Set<PublicationPolicyEntity>();
    public DbSet<PublicationProjectionIntentEntity> ProjectionIntents => Set<PublicationProjectionIntentEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new PublicationSnapshotReviewEntityConfiguration());
        modelBuilder.ApplyConfiguration(new PublicationPolicyEntityConfiguration());
        modelBuilder.ApplyConfiguration(new PublicationProjectionIntentEntityConfiguration());
        ConfigureProvider(modelBuilder);
    }

    protected abstract void ConfigureProvider(ModelBuilder modelBuilder);
}
