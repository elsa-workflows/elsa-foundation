using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Configuration;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore;

/// <summary>
/// Provider-neutral EF model for the Publishing ledger: snapshot reviews, policies, projection intents,
/// publication records, activity-publication receipts and activity draft test runs (P01-P06).
/// </summary>
public abstract class PublishingSnapshotReviewDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<PublicationSnapshotReviewEntity> SnapshotReviews => Set<PublicationSnapshotReviewEntity>();
    public DbSet<PublicationPolicyEntity> Policies => Set<PublicationPolicyEntity>();
    public DbSet<PublicationProjectionIntentEntity> ProjectionIntents => Set<PublicationProjectionIntentEntity>();
    public DbSet<PublicationRecordEntity> PublicationRecords => Set<PublicationRecordEntity>();
    public DbSet<ActivityPublicationReceiptEntity> ActivityPublicationReceipts => Set<ActivityPublicationReceiptEntity>();
    public DbSet<ActivityDraftTestRunEntity> ActivityDraftTestRuns => Set<ActivityDraftTestRunEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new PublicationSnapshotReviewEntityConfiguration());
        modelBuilder.ApplyConfiguration(new PublicationPolicyEntityConfiguration());
        modelBuilder.ApplyConfiguration(new PublicationProjectionIntentEntityConfiguration());
        modelBuilder.ApplyConfiguration(new PublicationRecordEntityConfiguration());
        modelBuilder.ApplyConfiguration(new ActivityPublicationReceiptEntityConfiguration());
        modelBuilder.ApplyConfiguration(new ActivityDraftTestRunEntityConfiguration());
        ConfigureProvider(modelBuilder);
    }

    protected abstract void ConfigureProvider(ModelBuilder modelBuilder);
}
