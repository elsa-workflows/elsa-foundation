using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Jobs.Persistence.EntityFrameworkCore;

internal abstract class JobsDbContext(DbContextOptions options) : DbContext(options)
{
    internal DbSet<JobRecord> Jobs => Set<JobRecord>();
    internal DbSet<JobRecognitionRecord> Recognition => Set<JobRecognitionRecord>();
    internal DbSet<JobOriginRecord> Origins => Set<JobOriginRecord>();
    internal DbSet<JobAttemptRecord> Attempts => Set<JobAttemptRecord>();
    internal DbSet<JobGateRecord> Gates => Set<JobGateRecord>();
    internal DbSet<JobHoldRecord> Holds => Set<JobHoldRecord>();
    internal DbSet<JobPermitRecord> Permits => Set<JobPermitRecord>();
    internal DbSet<JobGateMembershipRecord> Memberships => Set<JobGateMembershipRecord>();
    internal DbSet<JobClaimCursorRecord> ClaimCursors => Set<JobClaimCursorRecord>();
    internal DbSet<JobClaimGrantRecord> ClaimGrants => Set<JobClaimGrantRecord>();
    internal DbSet<JobPhysicalReceiptRecord> Receipts => Set<JobPhysicalReceiptRecord>();
    internal DbSet<JobOperationRecord> Operations => Set<JobOperationRecord>();
    internal DbSet<JobScheduleRecord> Schedules => Set<JobScheduleRecord>();
    internal DbSet<JobOccurrenceRecord> Occurrences => Set<JobOccurrenceRecord>();
    internal DbSet<JobDeliveryRecord> Deliveries => Set<JobDeliveryRecord>();
    internal abstract Task<long> ReadUtcTicksAsync(CancellationToken cancellationToken);
    protected abstract string ProviderName { get; }
    protected abstract string TokenType { get; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasElsaDefaultSchema(this);
        modelBuilder.Entity<JobRecord>().ToTable("elsa_jobs_jobs").HasIndex(x => new { x.ScopeKey, x.State, x.EligibleAt, x.Key });
        modelBuilder.Entity<JobRecognitionRecord>().ToTable("elsa_jobs_recognition").HasIndex(x => new { x.ScopeKey, x.RecognizeUntil });
        modelBuilder.Entity<JobOriginRecord>().ToTable("elsa_jobs_origins");
        modelBuilder.Entity<JobAttemptRecord>().ToTable("elsa_jobs_attempts").HasIndex(x => new { x.JobKey, x.Number }).IsUnique();
        modelBuilder.Entity<JobGateRecord>().ToTable("elsa_jobs_gates");
        modelBuilder.Entity<JobHoldRecord>().ToTable("elsa_jobs_holds").HasIndex(x => new { x.ScopeKey, x.GateKey });
        modelBuilder.Entity<JobPermitRecord>().ToTable("elsa_jobs_permits").HasIndex(x => new { x.GateKey, x.ExpiresAt });
        modelBuilder.Entity<JobPermitRecord>().HasIndex(x => x.AttemptKey);
        modelBuilder.Entity<JobGateMembershipRecord>().ToTable("elsa_jobs_gate_memberships").HasIndex(x => new { x.JobKey, x.GateKey }).IsUnique();
        modelBuilder.Entity<JobClaimCursorRecord>().ToTable("elsa_jobs_claim_cursors");
        modelBuilder.Entity<JobClaimGrantRecord>().ToTable("elsa_jobs_claim_grants");
        modelBuilder.Entity<JobPhysicalReceiptRecord>().ToTable("elsa_jobs_receipts").HasIndex(x => new { x.ScopeKey, x.JobKey, x.Key });
        modelBuilder.Entity<JobOperationRecord>().ToTable("elsa_jobs_operations").HasIndex(x => new { x.ScopeKey, x.Key });
        modelBuilder.Entity<JobScheduleRecord>().ToTable("elsa_jobs_schedules").HasIndex(x => new { x.ScopeKey, x.Enabled, x.NextOccurrence, x.Key });
        modelBuilder.Entity<JobOccurrenceRecord>().ToTable("elsa_jobs_occurrences");
        modelBuilder.Entity<JobDeliveryRecord>().ToTable("elsa_jobs_deliveries").HasIndex(x => new { x.ScopeKey, x.Acknowledged, x.Key });
        foreach (var entity in modelBuilder.Model.GetEntityTypes().Where(x => typeof(IJobRecord).IsAssignableFrom(x.ClrType)).ToArray())
        {
            var mapping = modelBuilder.Entity(entity.ClrType);
            mapping.HasKey(nameof(IJobRecord.Key));
            mapping.Property(nameof(IJobRecord.Key)).HasMaxLength(64);
            mapping.Property(nameof(IJobRecord.ScopeKey)).HasMaxLength(64).IsRequired();
            mapping.Property(nameof(IJobRecord.Identity)).IsRequired();
            mapping.Property(nameof(IJobRecord.Token)).HasColumnType(TokenType).HasMaxLength(16).IsConcurrencyToken().ValueGeneratedNever();
            mapping.Property(nameof(IJobRecord.SchemaVersion)).HasMaxLength(32).IsRequired();
        }
        modelBuilder.Entity<JobRecord>().Property(x => x.JobId).HasMaxLength(256);
        modelBuilder.Entity<JobRecord>().Property(x => x.Queue).HasMaxLength(256);
        modelBuilder.Entity<JobRecord>().Property(x => x.JobType).HasMaxLength(256);
        modelBuilder.Entity<JobRecord>().Property(x => x.HandlerKey).HasMaxLength(64);
        modelBuilder.Entity<JobRecord>().Property(x => x.RequestSha256).HasMaxLength(64);
        modelBuilder.Entity<JobRecord>().Property(x => x.ActiveAttemptKey).HasMaxLength(64);
        modelBuilder.Entity<JobRecord>().Property(x => x.RetriedFromJobId).HasMaxLength(256);
        modelBuilder.Entity<JobGateRecord>().Property(x => x.Name).HasMaxLength(256);
        modelBuilder.Entity<JobGateRecord>().Property(x => x.ModelKind).HasMaxLength(256);
        modelBuilder.Entity<JobAttemptRecord>().Property(x => x.Owner).HasMaxLength(256);
        modelBuilder.Entity<JobAttemptRecord>().Property(x => x.AttemptId).HasMaxLength(256);
        modelBuilder.Entity<JobAttemptRecord>().Property(x => x.ImplementationIdentity).HasMaxLength(256);
        EfOrdinalCollation.Apply(modelBuilder, ProviderName, nameof(IJobRecord.Identity), nameof(JobRecord.JobId),
            nameof(JobRecord.Queue), nameof(JobRecord.JobType), nameof(JobAttemptRecord.Owner));
        modelBuilder.MapSchemaFinalization(JobsEfModule.Name);
        modelBuilder.IndexSchemaVersionStamps();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        Stamp();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        Stamp();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void Stamp()
    {
        foreach (var entry in ChangeTracker.Entries().Where(x => x.Entity is IJobRecord && x.State is EntityState.Added or EntityState.Modified))
            ((IJobRecord)entry.Entity).Token = Guid.NewGuid().ToByteArray();
    }
}
