using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Configuration;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Elsa.Persistence.EntityFramework;

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
        // The host's optional schema; nothing changes when none is configured.
        modelBuilder.HasElsaDefaultSchema(this);
        modelBuilder.ApplyConfiguration(new PublicationSnapshotReviewEntityConfiguration());
        modelBuilder.ApplyConfiguration(new PublicationPolicyEntityConfiguration());
        modelBuilder.ApplyConfiguration(new PublicationProjectionIntentEntityConfiguration());
        modelBuilder.ApplyConfiguration(new PublicationRecordEntityConfiguration());
        modelBuilder.ApplyConfiguration(new ActivityPublicationReceiptEntityConfiguration());
        modelBuilder.ApplyConfiguration(new ActivityDraftTestRunEntityConfiguration());
        ConfigureProvider(modelBuilder);
        // Installed unconditionally, so this context reads a frame whatever wrote it. Nothing here enables an
        // encoder: with no codec configured these columns are written exactly as they were before.
        modelBuilder.UseElsaPayloadColumns(
            this,
            "Content");
    }

    protected abstract void ConfigureProvider(ModelBuilder modelBuilder);

    /// <summary>
    /// The string columns this module compares or orders in SQL beyond the ones a key or an index already
    /// covers: the tenant and resource hashes, the identity material stored beside them, and the bounded
    /// status/action discriminators every filter matches on. <c>Content</c> and the failure-message
    /// columns are absent: the first is deserialized in .NET, and the second pair is free text nothing
    /// compares.
    /// </summary>
    private static readonly string[] OrdinallyComparedColumns =
    [
        "Id", "TenantId", "TenantIdHash", "SchemaVersion", "Status",
        "PreflightToken", "Incarnation", "CandidateHash", "DefinitionId",
        "Action", "RequestedAction", "PolicySource", "DefaultAction",
        "SlotName", "RequestedSlotName", "DefaultSlotName",
        "RequestedExpectedPublicationId", "ActivePublicationId",
        "PolicyKey", "PolicyKeyHash",
        "WorkflowDefinitionId", "WorkflowDefinitionIdHash", "WorkflowDefinitionVersionId",
        "PublicationId", "PublicationIdHash", "SlotId", "SlotIdHash",
        "ArtifactId", "SourceReferenceId",
        "IntentId", "IntentIdHash", "ProjectionKind", "ProjectionKindHash", "Operation",
        "ReceiptKeyHash", "IdempotencyKey", "ReceiptTenantId",
        "TestRunId", "TestRunIdHash"
    ];

    /// <summary>Binds this module's ordinal columns to <paramref name="providerName"/>'s binary collation, per column.</summary>
    protected static void ApplyOrdinalCollation(ModelBuilder modelBuilder, string providerName) =>
        EfOrdinalCollation.Apply(modelBuilder, providerName, OrdinallyComparedColumns);
}
