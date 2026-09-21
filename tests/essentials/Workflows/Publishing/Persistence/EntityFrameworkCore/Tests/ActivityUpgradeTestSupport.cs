using System.Data.Common;
using System.Text.Json;
using Elsa.Activities.Design.Api.Contracts;
using Elsa.Activities.Design.Api.Services;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore.Stores;
using Elsa.Locking.Core;
using Elsa.Primitives.Contracts;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.Services;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Core.Services;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// The two Design contexts a suite opens, whichever provider holds them. An activity upgrade commits both
/// catalogs together, so they always name one database.
/// </summary>
internal sealed record ActivityUpgradeContexts(
    Func<IInterceptor[], ActivitiesDesignDbContext> Activities,
    Func<IInterceptor[], WorkflowsDesignDbContext> Workflows)
{
    /// <summary>
    /// Creates both models in the one database. EnsureCreated creates the database and the first model; it
    /// skips a database that already has tables, so the second model adds its own directly.
    /// </summary>
    public async Task CreateSchemaAsync()
    {
        await using (var activities = Activities([]))
            await activities.Database.EnsureCreatedAsync();
        await using (var workflows = Workflows([]))
            await workflows.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
    }
}

/// <summary>
/// One SQLite file holding both Design catalogs. An activity upgrade commits them together, so a single
/// target is the only topology the shared transaction accepts, and it is the topology the flipped
/// Workbench composes.
/// </summary>
internal sealed class ActivityUpgradeDatabase : IAsyncDisposable
{
    private readonly string path;

    private ActivityUpgradeDatabase(string path) => this.path = path;

    public string ConnectionString => $"Data Source={path};Pooling=False;Default Timeout=30";

    public static async Task<ActivityUpgradeDatabase> CreateAsync()
    {
        var database = new ActivityUpgradeDatabase(Path.Join(Path.GetTempPath(), $"elsa-upgrade-ef-{Guid.NewGuid():N}.db"));
        await using (var connection = new SqliteConnection(database.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode=WAL;";
            await command.ExecuteNonQueryAsync();
        }

        await database.Contexts.CreateSchemaAsync();
        return database;
    }

    public ActivityUpgradeContexts Contexts => new(Activities, Workflows);

    public ActivitiesDesignDbContext Activities(params IInterceptor[] interceptors) =>
        new ActivitiesDesignSqliteDbContext(Options<ActivitiesDesignSqliteDbContext>(interceptors));

    public WorkflowsDesignDbContext Workflows(params IInterceptor[] interceptors) =>
        new WorkflowsDesignSqliteDbContext(Options<WorkflowsDesignSqliteDbContext>(interceptors));

    private DbContextOptions<TContext> Options<TContext>(IInterceptor[] interceptors) where TContext : DbContext
    {
        var builder = new DbContextOptionsBuilder<TContext>().UseSqlite(ConnectionString);
        if (interceptors.Length > 0)
            builder.AddInterceptors(interceptors);
        return builder.Options;
    }

    public ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
            File.Delete(path + suffix);
        return ValueTask.CompletedTask;
    }
}

/// <summary>One request's view of the upgrade bridge: a context pair and the store composed over it.</summary>
internal sealed class ActivityUpgradeScope : IAsyncDisposable
{
    public ActivityUpgradeScope(
        ActivitiesDesignDbContext activities,
        WorkflowsDesignDbContext workflows,
        TestAccess access,
        IIdentityGenerator identities,
        TimeProvider clock,
        IActivityStructureService? structure = null)
    {
        Activities = activities;
        Workflows = workflows;
        Access = access;
        Payloads = ActivityUpgradeFixtures.Serializer();
        Design = new EfActivityDesignStores(activities, access, null, new EfActivityManagementProjectionWriter(activities, access));
        Store = new EfActivityUpgradePlanStore(
            activities,
            workflows,
            access,
            Payloads,
            structure ?? new LeafStructureService(),
            new ActivityProviderRegistry([new TestActivityProvider()]),
            new ActivityContractAuthoringValidator(new EmptyCapabilityCatalog()),
            [new TestManifestRewriter()],
            identities,
            new ImmediateLockProvider(),
            clock);
        // Replacing the one global projection row is a privileged-across-scopes act; the coordinator reads
        // and writes through a store bound to that context, as its production registration composes it.
        PrivilegedDesign = new EfActivityDesignStores(activities, PrivilegedAccess);
        Coordinator = Rebuilder(PrivilegedAccess);
    }

    public static TestAccess PrivilegedAccess { get; } = new(
        Elsa.Workflows.Runtime.Core.Models.PersistenceAccessContext.PrivilegedAcrossScopes(
            new Elsa.Workflows.Runtime.Core.Models.PersistenceAccessPurpose("activity-dependency-projection-rebuild")));

    public EfActivityDependencyProjectionRebuildCoordinator Rebuilder(
        TestAccess access,
        IActivityTemplateDependencyDiscoverer? discoverer = null) => new(
        Activities,
        Workflows,
        access,
        Payloads,
        PrivilegedDesign,
        new ActivityTemplateDependencyDiscovererRegistry([discoverer ?? new TestDependencyDiscoverer()]),
        new LeafStructureService(),
        PrivilegedDesign,
        new SequentialIdentities("rebuild"),
        TimeProvider.System);

    public ActivitiesDesignDbContext Activities { get; }
    public WorkflowsDesignDbContext Workflows { get; }
    public TestAccess Access { get; }
    public IPayloadSerializer Payloads { get; }
    public EfActivityDesignStores Design { get; }
    public EfActivityDesignStores PrivilegedDesign { get; }
    public EfActivityUpgradePlanStore Store { get; }
    public EfActivityDependencyProjectionRebuildCoordinator Coordinator { get; }

    public IActivityUpgradePlanApplier Applier(TimeProvider clock) =>
        new Elsa.Workflows.Publishing.Api.Commands.ApplyActivityUpgradePlanCommand(Design, Design, Store, new FrozenClock(clock.GetUtcNow()));

    public async ValueTask DisposeAsync()
    {
        await Activities.DisposeAsync();
        await Workflows.DisposeAsync();
    }
}

internal static class ActivityUpgradeFixtures
{
    public const string Tenant = "default";
    public const string DependencyDefinitionId = "dependency-definition";
    public const string OldVersionId = "old";
    public const string NewVersionId = "new";
    public const string ActivityDefinitionId = "activity-definition";
    public const string ActivityDraftId = "activity-draft";
    public const string WorkflowDefinitionId = "workflow-definition";
    public const string WorkflowDraftId = "workflow-draft";
    public const string ActivityOccurrenceId = "activity-root";
    public const string WorkflowOccurrenceId = "workflow-root";

    public static readonly DateTimeOffset Now = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

    public static IPayloadSerializer Serializer() => new JsonPayloadSerializer(new JsonPayloadConverterRegistry());

    public static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    public static ActivityContract Contract() => new("1", [], [], []);

    public static ActivityProviderManifest Provider() => new("test.provider", "1", Json("{}"));

    public static ActivityDefinitionVersionPublication Publication(
        string versionId,
        string version,
        ActivityDefinitionVersionLifecycle lifecycle,
        string? tenantId = Tenant,
        string definitionId = DependencyDefinitionId) => new()
    {
        Id = versionId,
        TenantId = tenantId,
        DefinitionVersionId = versionId,
        DefinitionId = definitionId,
        Version = version,
        ActivityTypeKey = definitionId,
        ResolutionKind = ActivityDefinitionVersionResolutionKind.ReusableTemplateBoundary,
        SourceDraftId = $"source-draft-{versionId}",
        Contract = Contract(),
        Provider = Provider(),
        TemplateId = $"template-{versionId}",
        TemplateHash = $"hash-{versionId}",
        SourceReferenceId = $"source-{versionId}",
        ProviderFingerprint = "provider",
        DirectDependencyCount = 0,
        ClosedTemplateCount = 1,
        RuntimeRequirements = [],
        ResumeTargetCount = 0,
        Lifecycle = lifecycle,
        PublishedAt = Now,
        CreatedAt = Now,
        LastModifiedAt = Now
    };

    /// <summary>The exact facts a published version contributes to a dependency reference.</summary>
    public static ActivityDefinitionReference Reference(ActivityDefinitionVersionPublication publication, string? tenantId = Tenant) => new(
        "ActivityVersion",
        publication.DefinitionId,
        publication.DefinitionVersionId,
        publication.Version,
        TemplateHash: publication.TemplateHash,
        TenantId: tenantId,
        Lifecycle: publication.Lifecycle);

    /// <summary>One derived edge whose owner is an immutable published version.</summary>
    public static ActivityDependencyItem VersionDependencyItem(
        ActivityDefinitionVersionPublication owner,
        string occurrenceId,
        ActivityDefinitionVersionPublication target,
        string? tenantId = Tenant)
    {
        var ownerReference = Reference(owner, tenantId);
        var dependency = Reference(target, tenantId);
        return new(
            $"ActivityVersion:{owner.DefinitionVersionId}:{occurrenceId}:{target.DefinitionVersionId}",
            ownerReference,
            dependency,
            new(occurrenceId, []),
            true,
            1,
            [ownerReference, dependency]);
    }

    /// <summary>One derived edge whose owner is a mutable draft.</summary>
    public static ActivityDependencyItem DependencyItem(
        string ownerKind,
        string ownerDefinitionId,
        string ownerId,
        long? revision,
        string occurrenceId,
        ActivityDefinitionVersionPublication target,
        string? tenantId = Tenant)
    {
        var owner = new ActivityDefinitionReference(ownerKind, ownerDefinitionId, DraftId: ownerId, Revision: revision, TenantId: tenantId);
        var dependency = Reference(target, tenantId);
        return new(
            $"{ownerKind}:{ownerId}:{occurrenceId}:{target.DefinitionVersionId}",
            owner,
            dependency,
            new(occurrenceId, []),
            true,
            1,
            [owner, dependency]);
    }

    public static WorkflowDefinitionState WorkflowState(string activityVersionId) =>
        WorkflowDefinitionState.Empty with { RootActivity = new(WorkflowOccurrenceId, activityVersionId, [], []) };
}

internal sealed class FrozenClock(DateTimeOffset now) : ISystemClock
{
    public DateTimeOffset UtcNow => now;
}

internal sealed class TestActivityProvider : IActivityProvider
{
    public string ProviderKey => "test.provider";
    public IReadOnlySet<string> SupportedManifestSchemas { get; } = new HashSet<string> { "1" };
    public ActivityProviderAuthoringCapabilities AuthoringCapabilities { get; } = new("Test", [new("1", true, new HashSet<string> { "1" })], new([]));
    public ValueTask<ActivityContractProposal> ProposeContractAsync(ActivityProviderContractProposalRequest request, CancellationToken cancellationToken = default) => ValueTask.FromResult(new ActivityContractProposal([], []));
    public ValueTask<IReadOnlyList<ActivityDiagnostic>> ValidateAsync(ActivityProviderManifest manifest, ActivityContract contract, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<ActivityDiagnostic>>([]);
    public ValueTask<ActivityManifestMigration> MigrateAsync(ActivityManifestMigrationRequest request, CancellationToken cancellationToken = default) => ValueTask.FromResult(new ActivityManifestMigration(request.Source, []));
}

internal sealed class TestManifestRewriter : IActivityProviderReferenceRewriter
{
    public string ProviderKey => "test.provider";
    public IReadOnlySet<string> SupportedManifestSchemas { get; } = new HashSet<string> { "1" };
    public ValueTask<ActivityProviderManifest> RewriteReferencesAsync(ActivityProviderManifest manifest, IReadOnlyList<ActivityUpgradeOccurrenceReplacement> replacements, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(manifest);
}

/// <summary>Discloses the activity draft's single pinned dependency, as its provider would.</summary>
internal sealed class TestDependencyDiscoverer(string versionId = ActivityUpgradeFixtures.OldVersionId) : IActivityTemplateDependencyDiscoverer
{
    public string ProviderKey => "test.provider";
    public IReadOnlySet<string> SupportedManifestSchemas { get; } = new HashSet<string> { "1" };
    public ValueTask<ActivityTemplateDependencyDiscovery> DiscoverDependenciesAsync(ActivityTemplateDependencyDiscoveryRequest request, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new ActivityTemplateDependencyDiscovery([new(versionId, ActivityUpgradeFixtures.ActivityOccurrenceId, [])], []));
}

internal sealed class EmptyCapabilityCatalog : IActivityContractCapabilityCatalog
{
    public IReadOnlyCollection<ActivityContractTypeCapability> Types => [];
}

internal sealed class LeafStructureService : IActivityStructureService
{
    public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity) => [];
    public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections) => activity;
    public ActivityNodeStructure? CompileExecutableStructure(ActivityNode activity) => activity.Structure;
    public IReadOnlyCollection<Elsa.Expressions.Core.Models.VariableDefinition> ProjectScopedVariables(ActivityNode activity) => [];
    public bool SupportsScopedVariables(ActivityNode activity) => false;
}

internal sealed class ImmediateLockProvider : IDistributedLockProvider
{
    public IDistributedSynchronizationHandle? TryAcquireLock(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => new Handle();
    public ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => ValueTask.FromResult<IDistributedSynchronizationHandle?>(new Handle());
    public ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => ValueTask.FromResult<IDistributedSynchronizationHandle>(new Handle());

    private sealed class Handle : IDistributedSynchronizationHandle
    {
        public CancellationToken HandleLostToken => CancellationToken.None;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

internal sealed class SequentialIdentities(string prefix = "generated") : IIdentityGenerator
{
    private int next;
    public string Generate() => $"{prefix}-{++next}";
}

/// <summary>Every upgrade owner is readable; the planner's own tenant rule still applies.</summary>
internal sealed class AllowAllAuthorization(string? tenantId = ActivityUpgradeFixtures.Tenant) :
    IActivityDependencyAuthorizationContext,
    IActivityDependencyContextAsync
{
    public string? TenantId => tenantId;
    public string AuthorizationProfile => "access";
    public bool CanRead(ActivityDefinitionReference reference) => reference.TenantId is null || reference.TenantId == tenantId;
    public ValueTask<string> GetAuthorizationProfileAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(AuthorizationProfile);
    public ValueTask<bool> CanReadAsync(ActivityDefinitionReference reference, CancellationToken cancellationToken = default) => ValueTask.FromResult(CanRead(reference));
}

/// <summary>Compatible for activity owners; workflow owners carry no activity diff, as in production.</summary>
internal sealed class CompatibleDiffBuilder : IActivityUpgradeDiffBuilder
{
    public ValueTask<ActivityVersionDiff?> BuildAsync(ActivityUpgradeOwnerSnapshot owner, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<ActivityVersionDiff?>(
            owner.Owner.Kind.StartsWith("Activity", StringComparison.Ordinal)
                ? new(
                    new("ActivityVersion", owner.Owner.DefinitionId, ActivityUpgradeFixtures.OldVersionId),
                    new("ActivityDraft", owner.Owner.DefinitionId, DraftId: owner.Owner.DraftId, Revision: owner.Owner.Revision),
                    ActivityVersionCompatibility.Compatible,
                    ActivityVersionBump.Minor,
                    true,
                    new("graph", "1", "graph", "1", false),
                    new(0, 1, 0, 0),
                    [],
                    [])
                : null);
}

/// <summary>
/// Runs <paramref name="sql"/> on the same connection and transaction immediately before the first
/// non-query command whose text contains <paramref name="fragment"/>, so a row the code under test already
/// read has moved by the time it writes.
/// </summary>
internal sealed class MutateBeforeFirstWriteInterceptor(string fragment, string sql) : DbCommandInterceptor
{
    private int remaining = 1;

    public bool Fired => remaining == 0;

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (!command.CommandText.Contains(fragment, StringComparison.Ordinal) || Interlocked.Exchange(ref remaining, 0) != 1)
            return result;
        await using var interleaved = command.Connection!.CreateCommand();
        interleaved.Transaction = command.Transaction;
        interleaved.CommandText = sql;
        await interleaved.ExecuteNonQueryAsync(cancellationToken);
        return result;
    }
}

/// <summary>Refuses the commit before it reaches the provider, so its outcome is not knowable.</summary>
internal sealed class FailCommitInterceptor : DbTransactionInterceptor
{
    public override ValueTask<InterceptionResult> TransactionCommittingAsync(
        DbTransaction transaction,
        TransactionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default) =>
        throw new InjectedCrashException("The provider refused the commit.");
}

/// <summary>Throws once on the first command whose text contains <paramref name="fragment"/>.</summary>
internal sealed class FailCommandOnceInterceptor(string fragment) : DbCommandInterceptor
{
    private int remaining = 1;

    public bool Fired => remaining == 0;

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains(fragment, StringComparison.Ordinal) && Interlocked.Exchange(ref remaining, 0) == 1)
            throw new InjectedCrashException($"The provider refused a command containing '{fragment}'.");
        return ValueTask.FromResult(result);
    }
}
