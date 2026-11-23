using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Reconciliation;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Services;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore.Stores;
using Elsa.Activities.Design.Reconciliation.Options;
using Elsa.Activities.Design.Reconciliation.Services;
using Elsa.Events.Core.Contracts;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Activities.Design.Persistence.EntityFrameworkCore.Tests;

public sealed class EfDesignAtomicWriteScopedCatalogTests
{
    [Fact]
    public async Task Sqlite_ordinary_scoped_authoring_create_without_request_tenant_keeps_marker_in_bound_scope()
    {
        await using var connection = await OpenDatabaseAsync();
        await using var db = CreateContext(connection);
        var bound = "acme";
        var writer = CreateWriter(db, PersistenceAccessContext.Scoped(new PersistenceScope(bound)));

        var result = await writer.ExecuteAsync(
            AuthoringRequest("create:authoring-missing-tenant"),
            (context, _) =>
            {
                StageAuthoring(context.Db, "authoring", bound);
                return Task.FromResult(Accepted());
            });

        Assert.Equal(EfDesignAtomicWriteStatus.Committed, result.Status);
        db.ChangeTracker.Clear();
        var marker = await db.ActivityDesignOperations.SingleAsync();
        Assert.Equal(bound, marker.TenantId);
        Assert.Equal(ActivitiesDesignDbContext.NormalizeTenantKey(bound), marker.TenantScopeKey);
        Assert.NotEqual(ActivitiesDesignDbContext.GlobalTenantKey, marker.TenantScopeKey);
        Assert.Equal("acme", await db.ActivityDefinitions.Where(x => x.Id == "authoring").Select(x => x.TenantId).SingleAsync());
    }

    [Fact]
    public async Task Sqlite_same_authoring_operation_key_under_two_tenants_yields_two_markers()
    {
        await using var connection = await OpenDatabaseAsync();
        await using var db = CreateContext(connection);
        var operation = new EfDesignOperationIdentity("activity.authoring.create.v1", "create:shared-key");

        foreach (var tenant in new[] { "tenant-a", "tenant-b" })
        {
            var writer = CreateWriter(db, PersistenceAccessContext.Scoped(new PersistenceScope(tenant)));
            var committed = await writer.ExecuteAsync(
                new EfDesignAtomicWriteRequest(operation, $"authoring-{tenant}", AuthoringUnits),
                (context, _) =>
                {
                    StageAuthoring(context.Db, $"definition-{tenant}", tenant);
                    return Task.FromResult(Accepted());
                });
            Assert.Equal(EfDesignAtomicWriteStatus.Committed, committed.Status);
            db.ChangeTracker.Clear();
        }

        var markers = await db.ActivityDesignOperations.OrderBy(x => x.TenantId).ToListAsync();
        Assert.Collection(
            markers,
            first =>
            {
                Assert.Equal("tenant-a", first.TenantId);
                Assert.Equal(ActivitiesDesignDbContext.NormalizeTenantKey("tenant-a"), first.TenantScopeKey);
                Assert.NotEqual(ActivitiesDesignDbContext.GlobalTenantKey, first.TenantScopeKey);
            },
            second =>
            {
                Assert.Equal("tenant-b", second.TenantId);
                Assert.Equal(ActivitiesDesignDbContext.NormalizeTenantKey("tenant-b"), second.TenantScopeKey);
                Assert.NotEqual(ActivitiesDesignDbContext.GlobalTenantKey, second.TenantScopeKey);
            });
    }

    [Fact]
    public async Task Sqlite_ordinary_scoped_add_definition_without_host_wide_intent_refuses_null_tenant_rows()
    {
        await using var connection = await OpenDatabaseAsync();
        await using var db = CreateContext(connection);
        var store = CreateStore(db, PersistenceAccessContext.Scoped(new PersistenceScope("default")));

        var failure = await Assert.ThrowsAsync<DesignPersistenceException>(() => store.Execute(
            new DesignOperationKey("host-wide-create"),
            Definition("global", tenant: null),
            Version("global", "global-v1", tenant: null)));

        Assert.IsType<InvalidDataException>(failure.InnerException);
        Assert.False(await db.ActivityDefinitions.AnyAsync());
        Assert.False(await db.ActivityDefinitionVersions.AnyAsync());
        Assert.False(await db.ActivityDesignOperations.AnyAsync());
    }

    [Fact]
    public async Task Sqlite_activity_version_reconciler_startup_task_writes_host_wide_catalog_rows()
    {
        await using var connection = await OpenDatabaseAsync();
        await using var db = CreateContext(connection);
        var access = new BindableAccess(PersistenceAccessContext.Scoped(new PersistenceScope("default")));
        var stores = new EfActivityDesignStores(db, access, new EfDesignAtomicWrite(db, access));
        var incoming = CreateCatalogVersion();
        var services = new ServiceCollection();
        services.AddSingleton<IPersistenceAccessContextBinder>(access);
        services.AddSingleton<IActivityVersionReconciler>(new ActivityVersionReconciler(
            NullLogger<ActivityVersionReconciler>.Instance,
            new DefaultActivityDefinitionHasher(),
            new DirectPublisher(incoming),
            Options.Create(new ActivityVersionReconcilerOptions()),
            stores,
            stores,
            stores,
            stores,
            stores));
        await using var provider = services.BuildServiceProvider();

        await new ActivityVersionReconcilerStartupTask(provider.GetRequiredService<IServiceScopeFactory>())
            .ExecuteAsync(CancellationToken.None);

        Assert.True(access.Current.IsGlobal);
        Assert.Equal(PersistenceAccessPolicy.Privileged, access.Current.AccessPolicy);
        Assert.Equal("activity-version-reconciliation", access.Current.Purpose?.Value);
        db.ChangeTracker.Clear();
        var definition = await db.ActivityDefinitions.SingleAsync();
        var version = await db.ActivityDefinitionVersions.SingleAsync();
        var marker = await db.ActivityDesignOperations.SingleAsync();
        Assert.Null(definition.TenantId);
        Assert.Null(version.TenantId);
        Assert.Equal(
            ActivitiesDesignDbContext.GlobalTenantKey,
            await db.ActivityDefinitions.Where(x => x.Id == definition.Id).Select(x => EF.Property<string>(x, "TenantKey")).SingleAsync());
        Assert.Null(marker.TenantId);
        Assert.Equal(ActivitiesDesignDbContext.GlobalTenantKey, marker.TenantScopeKey);
        Assert.Equal("activity.definition.create.v1", marker.OperationKind);
    }

    [Fact]
    public async Task Sqlite_activity_version_reconciler_startup_task_with_publishing_keeps_the_shell_scope()
    {
        await using var connection = await OpenDatabaseAsync();
        await using var db = CreateContext(connection);
        var access = new BindableAccess(PersistenceAccessContext.Scoped(new PersistenceScope("default")));
        var stores = new EfActivityDesignStores(db, access, new EfDesignAtomicWrite(db, access));
        var incoming = CreateCatalogVersion();
        var publisher = new ScopeRequiringPublisher(access);
        var services = new ServiceCollection();
        services.AddSingleton<IPersistenceAccessContextBinder>(access);
        services.AddSingleton<IActivitySourceVersionPublisher>(publisher);
        services.AddSingleton<IActivityVersionReconciler>(new ActivityVersionReconciler(
            NullLogger<ActivityVersionReconciler>.Instance,
            new DefaultActivityDefinitionHasher(),
            new DirectPublisher(incoming),
            Options.Create(new ActivityVersionReconcilerOptions()),
            stores,
            stores,
            stores,
            stores,
            stores,
            [publisher]));
        await using var provider = services.BuildServiceProvider();

        await new ActivityVersionReconcilerStartupTask(provider.GetRequiredService<IServiceScopeFactory>())
            .ExecuteAsync(CancellationToken.None);

        Assert.Equal("default", publisher.PublicationScope);
        Assert.Equal(PersistenceAccessPolicy.Ordinary, access.Current.AccessPolicy);
        Assert.False(access.Current.IsGlobal);
        Assert.Equal("default", access.Current.RequireScope().Value);
    }

    private static readonly string[] AuthoringUnits =
        ["activityDefinition", "activityDefinitionAuthoring", "activityDefinitionDraft", "activityDefinitionDraftLayout"];

    private static EfDesignAtomicWriteRequest AuthoringRequest(string operationKey) =>
        new(new EfDesignOperationIdentity("activity.authoring.create.v1", operationKey), operationKey, AuthoringUnits);

    private static EfDesignAtomicWrite CreateWriter(ActivitiesDesignDbContext db, PersistenceAccessContext access) =>
        new(db, new TestAccess(access));

    private static IAddActivityDefinitionCommand CreateStore(ActivitiesDesignDbContext db, PersistenceAccessContext access)
    {
        var accessor = new TestAccess(access);
        return new EfActivityDesignStores(db, accessor, new EfDesignAtomicWrite(db, accessor));
    }

    private static async Task<SqliteConnection> OpenDatabaseAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var setup = CreateContext(connection);
        await setup.Database.EnsureCreatedAsync();
        return connection;
    }

    private static ActivitiesDesignSqliteDbContext CreateContext(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options);

    private static EfDesignAtomicWriteStageResult Accepted() =>
        EfDesignAtomicWriteStageResult.Accepted("accepted", "{}");

    private static void StageAuthoring(ActivitiesDesignDbContext db, string definitionId, string tenant)
    {
        db.ActivityDefinitions.Add(Definition(definitionId, tenant));
        db.ActivityDefinitionAuthoringStates.Add(new ActivityDefinitionAuthoringState
        {
            Id = definitionId,
            DefinitionId = definitionId,
            TenantId = tenant,
            ContentAuthority = new(ActivityContentAuthorityKind.Design, WellKnownActivityContentAuthorities.Design)
        });
        db.ActivityDefinitionDrafts.Add(Draft($"{definitionId}-draft", definitionId, tenant, 1));
        db.ActivityDefinitionDraftLayouts.Add(Layout($"{definitionId}-draft", tenant, 1));
    }

    private static ActivityDefinition Definition(string id, string? tenant) =>
        new() { Id = id, TenantId = tenant, ActivityTypeKey = $"Acme.{id}", Category = "Tests" };

    private static ActivityDefinitionVersion Version(string definitionId, string id, string? tenant) =>
        new("1.0.0", definitionId)
        {
            Id = id,
            TenantId = tenant,
            ProviderKey = "provider",
            ProviderSchemaVersion = "1",
            ConsumerKey = "consumer",
            ConsumerSchemaVersion = "1",
            SourceKind = "test",
            SourceId = id
        };

    private static ActivityDefinitionDraft Draft(string id, string definitionId, string tenant, long revision) =>
        new()
        {
            Id = id,
            DefinitionId = definitionId,
            TenantId = tenant,
            Revision = revision,
            State = new(new("1", [], [], []), new("provider", "1", JsonDocument.Parse("{}").RootElement.Clone()), new Dictionary<string, string>())
        };

    private static ActivityDefinitionDraftLayout Layout(string draftId, string tenant, long revision) =>
        new() { Id = $"layout-{draftId}", DraftId = draftId, TenantId = tenant, Revision = revision, Records = [] };

    private static IActivityDefinitionVersion CreateCatalogVersion()
    {
        var identities = new SequentialIdentityGenerator();
        var definition = new ActivityDefinitionFactory(identities).Create(
            "Acme.Activities.Greet",
            "Acme",
            description: "catalog",
            id: "def-greet");
        return new ActivityDefinitionVersionFactory(identities, new DefaultActivityDefinitionHasher()).Create(
            definition,
            "1.0.0",
            "elsa.clr-activity",
            "1",
            "elsa.clr-activity",
            "1",
            JsonSerializer.SerializeToElement(new ClrActivityDescriptor("Object")),
            "test",
            "source",
            [],
            [],
            [],
            id: "ver-greet-1");
    }

    private sealed class TestAccess(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }

    private sealed class BindableAccess(PersistenceAccessContext current)
        : IPersistenceAccessContextAccessor, IPersistenceAccessContextBinder
    {
        public PersistenceAccessContext Current { get; private set; } = current;

        public void Bind(PersistenceAccessContext context) => Current = context;
    }

    private sealed class ScopeRequiringPublisher(IPersistenceAccessContextAccessor access) : IActivitySourceVersionPublisher
    {
        public string? PublicationScope { get; private set; }

        public Task<ActivitySourceVersionPublicationResult> PublishAsync(
            IActivityDefinition definition,
            IActivityDefinitionVersion version,
            CancellationToken cancellationToken = default)
        {
            PublicationScope = access.Current.RequireScope(admitPrivileged: true).Value;
            return Task.FromResult(ActivitySourceVersionPublicationResult.Published);
        }
    }

    private sealed class DirectPublisher(IActivityDefinitionVersion version) : IInlineEventPublisher
    {
        public Task Publish(IEvent @event, CancellationToken cancellationToken = default)
        {
            if (@event is ActivityVersionsReconciling reconciling)
                reconciling.Versions.Add(version);
            return Task.CompletedTask;
        }
    }

    private sealed class SequentialIdentityGenerator : IIdentityGenerator
    {
        public string Generate() => "generated";
    }
}
