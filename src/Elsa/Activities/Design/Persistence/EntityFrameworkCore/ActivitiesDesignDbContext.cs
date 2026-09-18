using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Globalization;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using Elsa.Persistence.EntityFramework;

namespace Elsa.Activities.Design.Persistence.EntityFrameworkCore;

/// <summary>
/// Shared Activities Design model. Provider-specific contexts only supply column annotations;
/// no provider SQL or IQueryable crosses the domain contracts.
/// </summary>
public abstract class ActivitiesDesignDbContext(DbContextOptions options) : DbContext(options)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    /// <summary>
    /// Domain-separated key for the global definition scope. The prefix keeps a global row
    /// distinct from every opaque tenant identifier without reserving a user-visible tenant ID.
    /// </summary>
    public const string GlobalTenantKey = "g:";
    public const int MaximumIdLength = 450;
    public const int IdentityHashLength = 64;
    private const string TenantKeyPrefix = "t:";
    private static readonly string[] IdentitySourceNames =
    [
        "DefinitionId", "HeadVersionId", "RecommendedVersionId", "DraftId", "DefinitionVersionId",
        "OwnerVersionId", "DependencyVersionId", "OccurrenceId", "ParentOccurrenceId", "PlanId",
        "ResourceId", "SourceDefinitionId", "SourceVersionId", "PublishedVersionId", "CandidateId",
        "PublicCandidateId", "AppliedIdempotencyKey", "ReceiptId"
    ];

    public DbSet<ActivityDefinition> ActivityDefinitions => Set<ActivityDefinition>();
    public DbSet<ActivityDefinitionVersion> ActivityDefinitionVersions => Set<ActivityDefinitionVersion>();
    public DbSet<ActivityAvailabilitySettingsRecord> ActivityAvailabilitySettings => Set<ActivityAvailabilitySettingsRecord>();
    public DbSet<ActivityDefinitionAuthoringState> ActivityDefinitionAuthoringStates => Set<ActivityDefinitionAuthoringState>();
    public DbSet<ActivityDefinitionDraft> ActivityDefinitionDrafts => Set<ActivityDefinitionDraft>();
    public DbSet<ActivityDefinitionDraftLayout> ActivityDefinitionDraftLayouts => Set<ActivityDefinitionDraftLayout>();
    public DbSet<ActivityDraftValidationState> ActivityDraftValidations => Set<ActivityDraftValidationState>();
    public DbSet<ActivityDefinitionVersionPublication> ActivityDefinitionVersionPublications => Set<ActivityDefinitionVersionPublication>();
    public DbSet<ActivityDefinitionVersionLayout> ActivityDefinitionVersionLayouts => Set<ActivityDefinitionVersionLayout>();
    public DbSet<ActivityDependencyEdge> ActivityDependencyEdges => Set<ActivityDependencyEdge>();
    public DbSet<ActivityDependencyProjectionState> ActivityDependencyProjections => Set<ActivityDependencyProjectionState>();
    public DbSet<ActivityForkCandidate> ActivityForkCandidates => Set<ActivityForkCandidate>();
    public DbSet<ActivityForkReceipt> ActivityForkReceipts => Set<ActivityForkReceipt>();
    public DbSet<ActivityManagementProjectionWatermark> ActivityManagementProjectionWatermarks => Set<ActivityManagementProjectionWatermark>();
    public DbSet<ActivityManagementProjectionSnapshot> ActivityManagementProjectionSnapshots => Set<ActivityManagementProjectionSnapshot>();
    public DbSet<ActivityDefinitionManagementProjectionRevision> ActivityDefinitionManagementProjections => Set<ActivityDefinitionManagementProjectionRevision>();
    public DbSet<ActivityDefinitionDraftManagementProjectionRevision> ActivityDraftManagementProjections => Set<ActivityDefinitionDraftManagementProjectionRevision>();
    public DbSet<ActivityDefinitionVersionManagementProjectionRevision> ActivityVersionManagementProjections => Set<ActivityDefinitionVersionManagementProjectionRevision>();
    public DbSet<ActivityUpgradePlanRecord> ActivityUpgradePlans => Set<ActivityUpgradePlanRecord>();
    public DbSet<ActivityUpgradeApplyReceiptRecord> ActivityUpgradeApplyReceipts => Set<ActivityUpgradeApplyReceiptRecord>();
    public DbSet<ActivityDesignOperationRecord> ActivityDesignOperations => Set<ActivityDesignOperationRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // The host's optional schema; nothing changes when none is configured.
        modelBuilder.HasElsaDefaultSchema(this);
        ConfigureEntity<ActivityDefinition>(modelBuilder, "elsa_activity_definitions");
        ConfigureEntity<ActivityDefinitionVersion>(modelBuilder, "elsa_activity_definition_versions_v2");
        ConfigureEntity<ActivityAvailabilitySettingsRecord>(modelBuilder, "elsa_activity_availability_settings");
        ConfigureEntity<ActivityDefinitionAuthoringState>(modelBuilder, "elsa_activity_definition_authoring");
        ConfigureEntity<ActivityDefinitionDraft>(modelBuilder, "elsa_activity_definition_drafts");
        ConfigureEntity<ActivityDefinitionDraftLayout>(modelBuilder, "elsa_activity_definition_draft_layouts");
        ConfigureEntity<ActivityDraftValidationState>(modelBuilder, "elsa_activity_draft_validations");
        ConfigureEntity<ActivityDefinitionVersionPublication>(modelBuilder, "elsa_activity_version_publications");
        ConfigureEntity<ActivityDefinitionVersionLayout>(modelBuilder, "elsa_activity_version_layouts");
        ConfigureEntity<ActivityDependencyEdge>(modelBuilder, "elsa_activity_dependency_edges");
        ConfigureEntity<ActivityDependencyProjectionState>(modelBuilder, "elsa_activity_dependency_projection");
        ConfigureEntity<ActivityForkCandidate>(modelBuilder, "elsa_activity_fork_candidates");
        ConfigureEntity<ActivityForkReceipt>(modelBuilder, "elsa_activity_fork_receipts");
        // Definition is reconstructed from the immutable receipt-owned JSON below. Explicitly
        // ignoring the navigation-shaped domain property prevents conventions from inventing an
        // ActivityDefinition relationship and a provider-dependent shadow foreign key.
        modelBuilder.Entity<ActivityForkReceipt>().Ignore(x => x.Definition);
        // Versions are loaded with their definition explicitly by the persistence store. Keep
        // this navigation out of the relational model so conventions cannot invent a
        // single-column DefinitionId foreign key after scoped/hash keys are introduced.
        modelBuilder.Entity<ActivityDefinitionVersion>().Ignore(x => x.Definition);
        ConfigureEntity<ActivityManagementProjectionWatermark>(modelBuilder, "elsa_activity_management_watermarks");
        ConfigureEntity<ActivityManagementProjectionSnapshot>(modelBuilder, "elsa_activity_management_snapshots");
        ConfigureProjection<ActivityDefinitionManagementProjectionRevision>(modelBuilder, "elsa_activity_management_definitions");
        ConfigureProjection<ActivityDefinitionDraftManagementProjectionRevision>(modelBuilder, "elsa_activity_management_drafts");
        ConfigureProjection<ActivityDefinitionVersionManagementProjectionRevision>(modelBuilder, "elsa_activity_management_versions");
        ConfigureEntity<ActivityUpgradePlanRecord>(modelBuilder, "elsa_activity_upgrade_plans");
        ConfigureEntity<ActivityUpgradeApplyReceiptRecord>(modelBuilder, "elsa_activity_upgrade_apply_receipts");
        ConfigureEntity<ActivityDesignOperationRecord>(modelBuilder, "elsa_activity_design_operations");
        var definition = modelBuilder.Entity<ActivityDefinition>();
        definition.Property<string>("TenantKey").HasMaxLength(66).IsRequired().ValueGeneratedNever();
        definition.HasIndex("TenantKey", nameof(ActivityDefinition.ActivityTypeKey)).IsUnique();
        modelBuilder.Entity<ActivityDefinitionVersion>().HasIndex("TenantScopeKey", "DefinitionIdIdentityHash", nameof(ActivityDefinitionVersion.SemVerSortKey)).IsUnique();
        modelBuilder.Entity<ActivityDefinitionAuthoringState>().HasIndex("TenantScopeKey", "DefinitionIdIdentityHash").IsUnique();
        modelBuilder.Entity<ActivityDefinitionAuthoringState>().HasIndex("TenantScopeKey", "HeadVersionIdIdentityHash");
        modelBuilder.Entity<ActivityDefinitionDraft>().HasIndex("TenantScopeKey", "DefinitionIdIdentityHash", "IdIdentityHash");
        modelBuilder.Entity<ActivityDefinitionDraftLayout>().HasIndex("TenantScopeKey", "DraftIdIdentityHash").IsUnique();
        modelBuilder.Entity<ActivityDraftValidationState>().HasIndex("TenantScopeKey", "DraftIdIdentityHash", nameof(ActivityDraftValidationState.Revision)).IsUnique();
        modelBuilder.Entity<ActivityDefinitionVersionPublication>().HasIndex("TenantScopeKey", "DefinitionIdIdentityHash", "IdIdentityHash");
        modelBuilder.Entity<ActivityDefinitionVersionPublication>().HasIndex("TenantScopeKey", "DefinitionVersionIdIdentityHash").IsUnique();
        modelBuilder.Entity<ActivityDefinitionVersionLayout>().HasIndex("TenantScopeKey", "DefinitionVersionIdIdentityHash").IsUnique();
        modelBuilder.Entity<ActivityDependencyEdge>().HasIndex("TenantScopeKey", "OwnerVersionIdIdentityHash", "OccurrenceIdIdentityHash", "DependencyVersionIdIdentityHash").IsUnique();
        modelBuilder.Entity<ActivityDependencyEdge>().HasIndex("TenantScopeKey", "OwnerVersionIdIdentityHash");
        modelBuilder.Entity<ActivityDependencyEdge>().HasIndex("TenantScopeKey", "DependencyVersionIdIdentityHash");
        modelBuilder.Entity<ActivityForkCandidate>().HasIndex(x => new { x.TenantScopeKey, x.ActorIdentityHash, x.CandidateIdIdentityHash }).IsUnique();
        modelBuilder.Entity<ActivityForkCandidate>().HasIndex("TenantScopeKey", nameof(ActivityForkCandidate.RetentionKey), "IdIdentityHash");
        modelBuilder.Entity<ActivityForkReceipt>().HasIndex(x => new { x.TenantScopeKey, x.ActorIdentityHash, x.IdempotencyIdentityHash }).IsUnique();
        modelBuilder.Entity<ActivityUpgradeApplyReceiptRecord>().HasIndex("TenantScopeKey", "PlanIdIdentityHash", nameof(ActivityUpgradeApplyReceiptRecord.IdempotencyKeyHash)).IsUnique();
        modelBuilder.Entity<ActivityManagementProjectionSnapshot>().HasIndex(x => x.Sequence).IsUnique();
        modelBuilder.Entity<ActivityDefinitionManagementProjectionRevision>().HasIndex("TenantScopeKey", "ResourceIdIdentityHash", nameof(ActivityDefinitionManagementProjectionRevision.ValidToSequenceExclusive));
        modelBuilder.Entity<ActivityDefinitionDraftManagementProjectionRevision>().HasIndex("TenantScopeKey", "ResourceIdIdentityHash", nameof(ActivityDefinitionDraftManagementProjectionRevision.ValidToSequenceExclusive));
        modelBuilder.Entity<ActivityDefinitionVersionManagementProjectionRevision>().HasIndex("TenantScopeKey", "ResourceIdIdentityHash", nameof(ActivityDefinitionVersionManagementProjectionRevision.ValidToSequenceExclusive));
        modelBuilder.Entity<ActivityDesignOperationRecord>().HasIndex(x => new { x.TenantScopeKey, x.OperationKindIdentityHash, x.OperationKeyIdentityHash }).IsUnique();
        foreach (var (type, properties) in new[]
                 {
                     (typeof(ActivityDefinitionVersion), new[] { "DefinitionIdIdentityHash" }),
                     (typeof(ActivityDefinitionAuthoringState), new[] { "DefinitionIdIdentityHash" }),
                     (typeof(ActivityDefinitionDraftLayout), new[] { "DraftIdIdentityHash" }),
                     (typeof(ActivityDraftValidationState), new[] { "DraftIdIdentityHash" }),
                     (typeof(ActivityDefinitionVersionPublication), new[] { "DefinitionIdIdentityHash", "DefinitionVersionIdIdentityHash" }),
                     (typeof(ActivityDefinitionVersionLayout), new[] { "DefinitionVersionIdIdentityHash" }),
                     (typeof(ActivityDependencyEdge), new[] { "OwnerVersionIdIdentityHash", "OccurrenceIdIdentityHash", "DependencyVersionIdIdentityHash" }),
                     (typeof(ActivityUpgradeApplyReceiptRecord), new[] { "PlanIdIdentityHash" }),
                     (typeof(ActivityDefinitionManagementProjectionRevision), new[] { "ResourceIdIdentityHash" }),
                     (typeof(ActivityDefinitionDraftManagementProjectionRevision), new[] { "ResourceIdIdentityHash" }),
                     (typeof(ActivityDefinitionVersionManagementProjectionRevision), new[] { "ResourceIdIdentityHash" })
                 })
            foreach (var property in properties)
                modelBuilder.Entity(type).Property(property).IsRequired();
        modelBuilder.Entity<ActivityDesignOperationRecord>().Property(x => x.OperationKindIdentityHash).HasMaxLength(64).IsRequired();
        modelBuilder.Entity<ActivityDesignOperationRecord>().Property(x => x.OperationKeyIdentityHash).HasMaxLength(64).IsRequired();
        modelBuilder.Entity<ActivityForkCandidate>().Property(x => x.CandidateId).HasMaxLength(512).IsRequired();
        modelBuilder.Entity<ActivityForkCandidate>().Property(x => x.TenantScopeKey).HasMaxLength(66).IsRequired();
        modelBuilder.Entity<ActivityForkCandidate>().Property(x => x.CandidateIdIdentityHash).HasMaxLength(64).IsRequired();
        modelBuilder.Entity<ActivityForkCandidate>().Property(x => x.ActorIdentityHash).HasMaxLength(64).IsRequired();
        modelBuilder.Entity<ActivityForkReceipt>().Property(x => x.TenantScopeKey).HasMaxLength(66).IsRequired();
        modelBuilder.Entity<ActivityForkReceipt>().Property(x => x.ActorIdentityHash).HasMaxLength(64).IsRequired();
        modelBuilder.Entity<ActivityForkReceipt>().Property(x => x.IdempotencyIdentityHash).HasMaxLength(64).IsRequired();
        modelBuilder.Entity<ActivityDefinitionAuthoringState>().Property(x => x.TenantScopeKey).HasMaxLength(66).IsRequired();
        modelBuilder.Entity<ActivityDesignOperationRecord>().Property(x => x.TenantScopeKey).HasMaxLength(66).IsRequired();
        modelBuilder.Entity<ActivityAvailabilitySettingsRecord>().Property(x => x.Scope).HasMaxLength(MaximumIdLength).IsRequired();
        modelBuilder.Entity<ActivityAvailabilitySettingsRecord>().Property<string>("ScopeIdentityHash").HasMaxLength(IdentityHashLength).IsRequired();
        ConfigureStringLengths(modelBuilder);
        ConfigureForeignKeyStringLengths(modelBuilder);
        ConfigureImmutableProperties(modelBuilder);
        ConfigureProvider(modelBuilder);
    }

    protected abstract void ConfigureProvider(ModelBuilder modelBuilder);

    /// <summary>
    /// The string columns this module compares or orders in SQL beyond the ones a key or an index already
    /// covers. <c>EfActivityDesignStores.ByReference</c> checks a reference's raw value next to its hash, so
    /// both sides of that pair are ordinal; the management projections page by <c>SortKey</c> then
    /// <c>ResourceId</c> with <c>string.Compare</c>, so those are cursor keys. Payload, layout, diagnostics,
    /// <c>SearchText</c>, <c>DisplayName</c>, <c>Category</c> and <c>Description</c> are deliberately absent:
    /// they are searched with <c>Contains</c>, and a binary collation would silently narrow those matches.
    /// </summary>
    private static readonly string[] OrdinallyComparedColumns =
    [
        .. IdentitySourceNames,
        .. IdentitySourceNames.Select(name => name + "IdentityHash"),
        "Id", "IdIdentityHash", "TenantId", "TenantScopeKey", "TenantKey",
        "Scope", "ScopeIdentityHash",
        "ActivityTypeKey", "SemVerSortKey", "Version",
        "SortKey", "RetentionKey", "IdempotencyKeyHash",
        "ActorId", "ActorIdentityHash", "IdempotencyKey", "IdempotencyIdentityHash",
        "OperationKind", "OperationKey", "OperationKindIdentityHash", "OperationKeyIdentityHash",
        "ProviderKey", "HeadProviderKey", "RecommendationProviderKey"
    ];

    /// <summary>Binds this module's ordinal columns to <paramref name="providerName"/>'s binary collation, per column.</summary>
    protected static void ApplyOrdinalCollation(ModelBuilder modelBuilder, string providerName) =>
        EfOrdinalCollation.Apply(modelBuilder, providerName, OrdinallyComparedColumns);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampWriteMetadata();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampWriteMetadata();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void StampWriteMetadata()
    {
        var now = DateTimeOffset.UtcNow;
        StampScopedIdentity();
        foreach (var entry in ChangeTracker.Entries<ActivityDefinition>()
                     .Where(x => x.State is EntityState.Added or EntityState.Modified))
        {
            entry.Property("TenantKey").CurrentValue = NormalizeTenantKey(entry.Entity.TenantId);
        }
        foreach (var entry in ChangeTracker.Entries<ActivityDefinitionManagementProjectionRevision>()
                     .Where(x => x.State is EntityState.Added or EntityState.Modified))
        {
            var authority = entry.Entity.ContentAuthority;
            if (authority is not null && !IsValidContentAuthority(authority, entry.Entity.ContentAuthorityKind))
                throw new InvalidOperationException("Activity management projection content authority is invalid.");
            var rawAuthority = authority is null ? null : JsonSerializer.Serialize(authority, Json);
            entry.Property(nameof(ActivityDefinitionManagementProjectionRevision.ContentAuthorityAuthorityKeyJson)).CurrentValue = authority is null ? null : JsonSerializer.Serialize(authority.AuthorityKey, Json);
            entry.Property(nameof(ActivityDefinitionManagementProjectionRevision.ContentAuthoritySourceIdJson)).CurrentValue = authority is null ? null : authority.SourceId is null ? "null" : JsonSerializer.Serialize(authority.SourceId, Json);
            entry.Property(nameof(ActivityDefinitionManagementProjectionRevision.ContentAuthorityAuthorityKey)).CurrentValue = authority?.AuthorityKey;
            entry.Property(nameof(ActivityDefinitionManagementProjectionRevision.ContentAuthoritySourceId)).CurrentValue = authority?.SourceId;
            if (authority is not null)
                entry.Property(nameof(ActivityDefinitionManagementProjectionRevision.ContentAuthorityKind)).CurrentValue = entry.Entity.ContentAuthority.Kind;
            var keyToken = authority is null ? null : JsonSerializer.Serialize(authority.AuthorityKey, Json);
            var sourceToken = authority is null ? null : authority.SourceId is null ? "null" : JsonSerializer.Serialize(authority.SourceId, Json);
            var integrityHash = authority is null ? null : ActivityAuthorityIntegrity.Compute(rawAuthority, rawAuthority, keyToken, sourceToken, authority.AuthorityKey, authority.SourceId, authority.Kind);
            var persistedAuthority = authority is null ? null : AddAuthorityIntegrityHash(rawAuthority!, integrityHash!);
            entry.Property(nameof(ActivityDefinitionManagementProjectionRevision.ContentAuthorityJson)).CurrentValue = persistedAuthority;
            entry.Property(nameof(ActivityDefinitionManagementProjectionRevision.ContentAuthorityCanonicalJson)).CurrentValue = persistedAuthority;
            entry.Property(nameof(ActivityDefinitionManagementProjectionRevision.ContentAuthorityIntegrityHash)).CurrentValue = integrityHash;
        }
        foreach (var entry in ChangeTracker.Entries<Elsa.Primitives.Entities.Entity>()
                     .Where(x => x.State is EntityState.Added or EntityState.Modified))
        {
            if (entry.State == EntityState.Added)
                entry.Property(nameof(Elsa.Primitives.Entities.Entity.CreatedAt)).CurrentValue =
                    entry.Property(nameof(Elsa.Primitives.Entities.Entity.CreatedAt)).CurrentValue is DateTimeOffset created && created != default ? created : now;
            entry.Property(nameof(Elsa.Primitives.Entities.Entity.LastModifiedAt)).CurrentValue = now;
            entry.Property("ConcurrencyToken").CurrentValue = Guid.NewGuid().ToByteArray();
        }
        foreach (var entry in ChangeTracker.Entries<ActivityForkReceipt>().Where(x => x.State == EntityState.Added))
        {
            entry.Entity.TenantScopeKey = NormalizeTenantKey(entry.Entity.TenantId);
            entry.Entity.ActorIdentityHash = ActivityForkIdentityMaterial.ExactHash(entry.Entity.ActorId);
            entry.Entity.IdempotencyIdentityHash = ActivityForkIdentityMaterial.ExactHash(entry.Entity.IdempotencyKey);
        }
        foreach (var entry in ChangeTracker.Entries<ActivityForkCandidate>().Where(x => x.State == EntityState.Added))
        {
            entry.Entity.TenantScopeKey = NormalizeTenantKey(entry.Entity.TenantId);
            entry.Entity.CandidateIdIdentityHash = ActivityForkIdentityMaterial.ExactHash(entry.Entity.CandidateId);
            entry.Entity.ActorIdentityHash = ActivityForkIdentityMaterial.ExactHash(entry.Entity.ActorId);
        }
        foreach (var entry in ChangeTracker.Entries<ActivityDefinitionAuthoringState>().Where(x => x.State == EntityState.Added))
            entry.Entity.TenantScopeKey = NormalizeTenantKey(entry.Entity.TenantId);
        foreach (var entry in ChangeTracker.Entries<ActivityDesignOperationRecord>().Where(x => x.State == EntityState.Added))
            entry.Entity.TenantScopeKey = NormalizeTenantKey(entry.Entity.TenantId);
    }

    /// <summary>
    /// Normalizes nullable tenant scope into a non-null, domain-separated key. Tenant IDs remain
    /// opaque to storage and are hashed to keep the unique composite key within every provider's
    /// index budget; the explicit prefix keeps global scope disjoint from tenant scope.
    /// </summary>
    public static string NormalizeTenantKey(string? tenantId) => tenantId is null
        ? GlobalTenantKey
        : TenantKeyPrefix + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tenantId)));

    public static string ComputeIdentityHash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    /// <summary>
    /// Orders a bounded read, such as <c>Take(2)</c> to detect a duplicate, by the physical key every scoped entity
    /// shares. Without an order the provider chooses which rows a row limit returns, and EF reports the query as
    /// warning 10102.
    /// </summary>
    public static IOrderedQueryable<T> InPhysicalIdentityOrder<T>(IQueryable<T> query) where T : class =>
        query.OrderBy(x => EF.Property<string>(x, "TenantScopeKey"))
            .ThenBy(x => EF.Property<string>(x, "IdIdentityHash"));

    private void StampScopedIdentity()
    {
        foreach (var entry in ChangeTracker.Entries()
                     .Where(x => x.State is EntityState.Added or EntityState.Modified))
        {
            if (entry.Metadata.FindProperty("ScopeIdentityHash") is not null)
            {
                var scope = entry.Property("Scope").CurrentValue as string;
                ArgumentException.ThrowIfNullOrWhiteSpace(scope);
                entry.Property("ScopeIdentityHash").CurrentValue = ComputeIdentityHash(scope);
                continue;
            }

            if (entry.Metadata.FindProperty("TenantScopeKey") is null || entry.Metadata.FindProperty("IdIdentityHash") is null)
                continue;

            var id = entry.Property("Id").CurrentValue as string;
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            entry.Property("TenantScopeKey").CurrentValue = NormalizeTenantKey(
                entry.Metadata.FindProperty("TenantId") is null ? null : entry.Property("TenantId").CurrentValue as string);
            entry.Property("IdIdentityHash").CurrentValue = ComputeIdentityHash(id);

            if (entry.Metadata.FindProperty("TenantKey") is not null)
                entry.Property("TenantKey").CurrentValue = entry.Property("TenantScopeKey").CurrentValue;

            foreach (var source in IdentitySourceNames)
            {
                var hash = source + "IdentityHash";
                if (entry.Metadata.FindProperty(source) is null || entry.Metadata.FindProperty(hash) is null)
                    continue;
                var value = entry.Property(source).CurrentValue as string;
                entry.Property(hash).CurrentValue = value is null ? null : ComputeIdentityHash(value);
            }
        }
    }

    private static void ConfigureEntity<TEntity>(ModelBuilder modelBuilder, string table)
        where TEntity : class
    {
        var entity = modelBuilder.Entity<TEntity>();
        entity.ToTable(table);
        // Keys are scoped, and IDs are legal up to 450 characters. Keep the full ID
        // for exact residual comparisons while indexing a fixed-width digest that fits every
        // provider's key budget. Availability settings use their explicit logical Scope instead.
        entity.Property<string>("Id").HasMaxLength(MaximumIdLength).ValueGeneratedNever();
        if (typeof(TEntity) == typeof(ActivityAvailabilitySettingsRecord))
        {
            // Settings carry no tenant of their own; they are partitioned by the
            // persistence scope that saved them, which the store assigns explicitly.
            entity.Property<string>("TenantScopeKey").HasMaxLength(66).IsRequired();
            entity.Property<string>("ScopeIdentityHash").HasMaxLength(IdentityHashLength).IsRequired().ValueGeneratedOnAdd()
                .HasValueGenerator<ScopeIdentityHashValueGenerator>();
            entity.HasKey("TenantScopeKey", "ScopeIdentityHash");
        }
        else
        {
            entity.Property<string>("TenantScopeKey").HasMaxLength(66).IsRequired().ValueGeneratedOnAdd()
                .HasValueGenerator<TenantScopeKeyValueGenerator>();
            entity.Property<string>("IdIdentityHash").HasMaxLength(IdentityHashLength).IsRequired().ValueGeneratedOnAdd()
                .HasValueGenerator<IdIdentityHashValueGenerator>();
            entity.HasKey("TenantScopeKey", "IdIdentityHash");
            ConfigureIdentityHashProperties(entity);
        }
        // The provider-neutral Entity.RowNumber is an ordinal hint, not a logical identity. EF
        // providers disagree on generated non-key integer columns (SQLite leaves them NULL), so
        // keep ordering explicit with stable Id tie-breakers and do not persist this legacy hint.
        if (typeof(TEntity).GetProperty("RowNumber") is not null)
            entity.Ignore("RowNumber");
        foreach (var name in new[] { "TenantId", "CreatedAt", "LastModifiedAt" }.Where(name => typeof(TEntity).GetProperty(name) is not null))
            entity.Property(name);
        entity.Property<byte[]>("ConcurrencyToken").IsConcurrencyToken().IsRequired(false);
        ConfigureConversions(entity);
    }

    private static void ConfigureIdentityHashProperties<TEntity>(EntityTypeBuilder<TEntity> entity)
        where TEntity : class
    {
        foreach (var property in entity.Metadata.ClrType.GetProperties()
                     .Where(x => x.PropertyType == typeof(string) && IdentitySourceNames.Contains(x.Name, StringComparer.Ordinal)))
        {
            entity.Property<string>(property.Name).HasMaxLength(MaximumIdLength);
            entity.Property<string?>(property.Name + "IdentityHash")
                .HasMaxLength(IdentityHashLength)
                .IsRequired(false)
                .ValueGeneratedNever();
        }
    }

    private static void ConfigureStringLengths(ModelBuilder modelBuilder)
    {
        // Keep the provider-neutral contract useful for providers with larger key budgets. The
        // SQL Server context applies its 900-byte composite-key budget separately.
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties().Where(x => x.ClrType == typeof(string)))
            {
                var indexed = entity.GetIndexes().Any(index => index.Properties.Contains(property));
                if (indexed)
                    property.SetMaxLength(property.Name switch
                    {
                        "TenantId" => 256,
                        "ActivityTypeKey" => 256,
                        "SemVerSortKey" => 256,
                        "OperationKind" => 96,
                        "OperationKey" => 256,
                        "OperationKindIdentityHash" or "OperationKeyIdentityHash" or "CandidateIdIdentityHash" or "ActorIdentityHash" or "IdempotencyIdentityHash" or "ScopeIdentityHash" => 64,
                        var name when name.EndsWith("IdentityHash", StringComparison.Ordinal) => IdentityHashLength,
                        "TenantScopeKey" => 66,
                        "TenantKey" => 66,
                        "ValidToSequenceExclusive" or "ValidFromSequence" => 32,
                        _ => 256
                    });
            }
        }
    }

    private static void ConfigureForeignKeyStringLengths(ModelBuilder modelBuilder)
    {
        foreach (var foreignKey in modelBuilder.Model.GetEntityTypes().SelectMany(x => x.GetForeignKeys()))
        {
            for (var index = 0; index < foreignKey.Properties.Count; index++)
            {
                var dependent = foreignKey.Properties[index];
                var principal = foreignKey.PrincipalKey.Properties[index];
                if (dependent.ClrType == typeof(string) && principal.ClrType == typeof(string) && principal.GetMaxLength() is { } maxLength)
                    dependent.SetMaxLength(maxLength);
            }
        }
    }

    private static void ConfigureImmutableProperties(ModelBuilder modelBuilder)
    {
        var immutable = new[]
        {
            (typeof(ActivityDefinition), "ActivityTypeKey"),
            (typeof(ActivityDefinitionVersion), "Version"),
            (typeof(ActivityDefinitionVersion), "SemVerSortKey"),
            (typeof(ActivityDefinitionVersion), "DefinitionId"),
            (typeof(ActivityDefinitionVersion), "ExecutionType"),
            (typeof(ActivityDefinitionVersion), "SourceKind"),
            (typeof(ActivityDefinitionVersion), "SourceId"),
            (typeof(ActivityDefinitionVersion), "Hash"),
            (typeof(ActivityDefinitionVersion), "DescriptorPayloadSource"),
            (typeof(ActivityDefinitionVersion), "InputsSource"),
            (typeof(ActivityDefinitionVersion), "OutputsSource"),
            (typeof(ActivityDefinitionVersion), "DesignFacetsSource"),
            (typeof(ActivityForkCandidate), "CandidateId"),
            (typeof(ActivityForkCandidate), "TenantScopeKey"),
            (typeof(ActivityForkCandidate), "CandidateIdIdentityHash"),
            (typeof(ActivityForkCandidate), "ActorIdentityHash"),
            (typeof(ActivityForkCandidate), "PreviewIdempotencyKey"),
            (typeof(ActivityForkCandidate), "RequestFingerprint"),
            (typeof(ActivityForkCandidate), "AccessBindingFingerprint"),
            (typeof(ActivityForkCandidate), "ActorId"),
            (typeof(ActivityForkCandidate), "AuthorizationProfile"),
            (typeof(ActivityForkCandidate), "ReservedDefinition"),
            (typeof(ActivityForkCandidate), "ReservedAuthoringState"),
            (typeof(ActivityForkCandidate), "ReservedDraft"),
            (typeof(ActivityForkCandidate), "ReservedLayout"),
            (typeof(ActivityForkReceipt), "IdempotencyKey"),
            (typeof(ActivityForkReceipt), "CandidateId"),
            (typeof(ActivityForkReceipt), "PublicCandidateId"),
            (typeof(ActivityForkReceipt), "RequestFingerprint"),
            (typeof(ActivityForkReceipt), "AccessBindingFingerprint"),
            (typeof(ActivityForkReceipt), "ActorId"),
            (typeof(ActivityForkReceipt), "AuthorizationProfile"),
            (typeof(ActivityForkReceipt), "DefinitionId"),
            (typeof(ActivityForkReceipt), "ActivityTypeKey"),
            (typeof(ActivityForkReceipt), "DraftId"),
            (typeof(ActivityForkReceipt), "DefinitionMaterialJson"),
            (typeof(ActivityDesignOperationRecord), "OperationKindIdentityHash"),
            (typeof(ActivityDesignOperationRecord), "OperationKeyIdentityHash"),
            (typeof(ActivityForkReceipt), "TenantScopeKey"),
            (typeof(ActivityForkReceipt), "ActorIdentityHash"),
            (typeof(ActivityForkReceipt), "IdempotencyIdentityHash"),
            (typeof(ActivityDefinitionAuthoringState), "TenantScopeKey"),
            (typeof(ActivityDesignOperationRecord), "TenantScopeKey")
        };
        foreach (var (type, name) in immutable)
            modelBuilder.Entity(type).Property(name).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
            foreach (var property in entity.GetProperties().Where(x => x.Name is "Id" or "TenantId" or "TenantScopeKey" or "IdIdentityHash" or "ScopeIdentityHash"))
                property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }

    private static void ConfigureProjection<TEntity>(ModelBuilder modelBuilder, string table)
        where TEntity : class
    {
        ConfigureEntity<TEntity>(modelBuilder, table);
        if (typeof(TEntity) == typeof(ActivityDefinitionManagementProjectionRevision))
        {
            modelBuilder.Entity<ActivityDefinitionManagementProjectionRevision>().Ignore(nameof(ActivityDefinitionManagementProjectionRevision.ContentAuthority));
            modelBuilder.Entity<ActivityDefinitionManagementProjectionRevision>().Property(nameof(ActivityDefinitionManagementProjectionRevision.ContentAuthorityJson))
                .HasColumnName(nameof(ActivityDefinitionManagementProjectionRevision.ContentAuthority)).IsRequired(false);
            modelBuilder.Entity<ActivityDefinitionManagementProjectionRevision>().Property(nameof(ActivityDefinitionManagementProjectionRevision.ContentAuthorityIsValid)).ValueGeneratedOnAddOrUpdate();
            modelBuilder.Entity<ActivityDefinitionManagementProjectionRevision>().Property(nameof(ActivityDefinitionManagementProjectionRevision.ContentAuthorityCanonicalJson)).IsRequired(false);
            modelBuilder.Entity<ActivityDefinitionManagementProjectionRevision>().Property(nameof(ActivityDefinitionManagementProjectionRevision.ContentAuthorityAuthorityKeyJson)).IsRequired(false);
            modelBuilder.Entity<ActivityDefinitionManagementProjectionRevision>().Property(nameof(ActivityDefinitionManagementProjectionRevision.ContentAuthoritySourceIdJson)).IsRequired(false);
            modelBuilder.Entity<ActivityDefinitionManagementProjectionRevision>().Property(nameof(ActivityDefinitionManagementProjectionRevision.ContentAuthorityAuthorityKey)).IsRequired(false);
            modelBuilder.Entity<ActivityDefinitionManagementProjectionRevision>().Property(nameof(ActivityDefinitionManagementProjectionRevision.ContentAuthoritySourceId)).IsRequired(false);
            modelBuilder.Entity<ActivityDefinitionManagementProjectionRevision>().Property(nameof(ActivityDefinitionManagementProjectionRevision.ContentAuthorityIntegrityHash)).IsRequired(false);
        }
        modelBuilder.Entity<TEntity>().HasIndex("TenantId", "ValidFromSequence", "ValidToSequenceExclusive");
    }

    private static void ConfigureConversions<TEntity>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TEntity> entity)
        where TEntity : class
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        foreach (var property in typeof(TEntity).GetProperties().Where(p =>
                     p.CanWrite && p.PropertyType != typeof(string) &&
                     (p.PropertyType.IsClass || p.PropertyType.IsInterface) &&
                     p.PropertyType != typeof(DateTimeOffset) &&
                     p.GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.Schema.NotMappedAttribute), true).Length == 0 &&
                     p.Name is not ("Definition" or "RowNumber")))
        {
            var method = typeof(ActivitiesDesignDbContext).GetMethod(nameof(ConfigureJson), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .MakeGenericMethod(typeof(TEntity), property.PropertyType);
            method.Invoke(null, [entity, property.Name, options]);
        }
    }

    private static void ConfigureJson<TEntity, TValue>(EntityTypeBuilder<TEntity> entity, string name, JsonSerializerOptions options)
        where TEntity : class
        => entity.Property<TValue>(name).HasConversion(
            value => SerializeJson(value, options, name),
            value => DeserializeJson<TValue>(value, options, name),
            JsonValueComparer<TValue>(options));

    /// <summary>
    /// Compares a JSON-converted value by its serialized form and snapshots it through a JSON round-trip, using the
    /// converter's own options so equal values are exactly the values that would be written identically. Without it
    /// EF compares these values by reference, and an in-place change to a converted collection or object is never
    /// saved. It calls <see cref="JsonSerializer"/> directly because <see cref="SerializeJson{TValue}"/> and
    /// <see cref="DeserializeJson{TValue}"/> are the strict persistence path: they reject null and raise
    /// <see cref="DesignPersistenceException"/>, while change tracking must snapshot and compare in-memory nulls and
    /// is not a persistence operation.
    /// </summary>
    private static ValueComparer<TValue> JsonValueComparer<TValue>(JsonSerializerOptions options) => new(
        (left, right) => JsonEquals(left, right, options),
        value => JsonHashCode(value, options),
        value => JsonSnapshot(value, options));

    private static bool JsonEquals<TValue>(TValue? left, TValue? right, JsonSerializerOptions options) =>
        left is null || right is null
            ? left is null && right is null
            : string.Equals(JsonSerializer.Serialize(left, options), JsonSerializer.Serialize(right, options), StringComparison.Ordinal);

    private static int JsonHashCode<TValue>(TValue value, JsonSerializerOptions options) =>
        value is null ? 0 : StringComparer.Ordinal.GetHashCode(JsonSerializer.Serialize(value, options));

    private static TValue JsonSnapshot<TValue>(TValue value, JsonSerializerOptions options) =>
        value is null ? value : JsonSerializer.Deserialize<TValue>(JsonSerializer.Serialize(value, options), options)!;

    private static string SerializeJson<TValue>(TValue value, JsonSerializerOptions options, string name)
    {
        try { return JsonSerializer.Serialize(value, options); }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            throw new DesignPersistenceException(DesignPersistenceDomain.Activity, DesignPersistenceFailureKind.Serialization, "json-convert", name, exception);
        }
    }

    private static TValue DeserializeJson<TValue>(string? value, JsonSerializerOptions options, string name)
    {
        // Non-authority JSON columns retain strict materialization behavior. Management authority
        // material is stored through its dedicated raw/canonical string columns so its read path
        // can reject direct corruption in provider-side predicates before hydration.
        try
        {
            return JsonSerializer.Deserialize<TValue>(value ?? throw new InvalidDataException($"Stored JSON property '{name}' is null."), options)
                ?? throw new InvalidDataException($"Stored JSON property '{name}' is null.");
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException or ArgumentException or InvalidDataException)
        {
            throw new DesignPersistenceException(DesignPersistenceDomain.Activity, DesignPersistenceFailureKind.Serialization, "json-convert", name, exception);
        }
    }

    private static bool IsValidContentAuthority(ActivityContentAuthority authority, ActivityContentAuthorityKind scalarKind) =>
        authority.Kind == scalarKind && Enum.IsDefined(authority.Kind) && !string.IsNullOrWhiteSpace(authority.AuthorityKey) &&
        (authority.SourceId is null || !string.IsNullOrWhiteSpace(authority.SourceId)) &&
        (authority.Kind == ActivityContentAuthorityKind.ProviderSource || authority.SourceId is null);

    private static string AddAuthorityIntegrityHash(string rawAuthority, string integrityHash)
    {
        var objectNode = JsonNode.Parse(rawAuthority)?.AsObject()
            ?? throw new InvalidOperationException("Activity management projection authority material is not a JSON object.");
        objectNode["integrityHash"] = integrityHash;
        return objectNode.ToJsonString(Json);
    }
}

internal static class ActivityAuthorityIntegrity
{
    public static string Compute(
        string? raw,
        string? canonical,
        string? authorityKeyToken,
        string? sourceIdToken,
        string? authorityKey,
        string? sourceId,
        ActivityContentAuthorityKind kind)
    {
        var material = string.Join("|", new[]
        {
            raw, canonical, authorityKeyToken, sourceIdToken, authorityKey, sourceId,
            ((int)kind).ToString(CultureInfo.InvariantCulture)
        }.Select(value => value is null ? "-1:" : $"{value.Length.ToString(CultureInfo.InvariantCulture)}:{value}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.Unicode.GetBytes(material)));
    }
}

internal sealed class TenantScopeKeyValueGenerator : ValueGenerator<string>
{
    public override string Next(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry) =>
        ActivitiesDesignDbContext.NormalizeTenantKey(entry.Metadata.FindProperty("TenantId") is null
            ? null
            : entry.Property("TenantId").CurrentValue as string);

    public override bool GeneratesTemporaryValues => false;
}

internal sealed class IdIdentityHashValueGenerator : ValueGenerator<string>
{
    public override string Next(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry) =>
        ActivitiesDesignDbContext.ComputeIdentityHash(
            entry.Property("Id").CurrentValue as string
            ?? throw new InvalidOperationException("An activity persistence entity requires an Id before it is added."));

    public override bool GeneratesTemporaryValues => false;
}

internal sealed class ScopeIdentityHashValueGenerator : ValueGenerator<string>
{
    public override string Next(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry) =>
        ActivitiesDesignDbContext.ComputeIdentityHash(
            entry.Property("Scope").CurrentValue as string
            ?? throw new InvalidOperationException("Availability settings require a Scope before they are added."));

    public override bool GeneratesTemporaryValues => false;
}

/// <summary>Records for the two model units whose domain contract is intentionally provider-neutral.</summary>
public sealed class ActivityDesignOperationRecord : Elsa.Primitives.Entities.Entity
{
    public string? TenantId { get; set; }
    public string TenantScopeKey { get; set; } = null!;
    public string OperationKind { get; set; } = null!;
    public string OperationKey { get; set; } = null!;
    public string OperationKindIdentityHash { get; set; } = null!;
    public string OperationKeyIdentityHash { get; set; } = null!;
    public string CanonicalRequestFingerprint { get; set; } = null!;
    public string AuthoritativeResultFingerprint { get; set; } = null!;
    public string AuthoritativeResultJson { get; set; } = null!;
    public string MutatedUnitsJson { get; set; } = null!;
}

public sealed class ActivityUpgradePlanRecord : Elsa.Primitives.Entities.Entity
{
    public string? TenantId { get; set; }
    public string PlanId { get; set; } = null!;
    public string PlanJson { get; set; } = null!;
}

public sealed class ActivityUpgradeApplyReceiptRecord : Elsa.Primitives.Entities.Entity
{
    public string? TenantId { get; set; }
    public string ReceiptId { get; set; } = null!;
    public string PlanId { get; set; } = null!;
    public string IdempotencyKeyHash { get; set; } = null!;
    public string ReceiptJson { get; set; } = null!;
}
