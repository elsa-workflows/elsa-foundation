using System.Data.Common;
using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Executables;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

internal static class KernelExecutablePinContract
{
    private sealed class PinPageCommands : DbCommandInterceptor
    {
        public bool Capture { get; set; }
        public List<string> Pages { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Capture && command.CommandText.Contains("FROM \"RuntimeKernelExecutablePins\"", StringComparison.Ordinal))
                Pages.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    internal static async Task RunAsync(string provider, string connectionString, string scope = "kernel-pin-tenant")
    {
        var now = new DateTimeOffset(2026, 11, 27, 1, 0, 0, TimeSpan.Zero);
        var tenant = new PersistenceScope(scope);
        var clock = new FakeTimeProvider(now);
        var services = new ServiceCollection();
        services.AddLogging();
        var supplementalRoots = new SupplementalScheduleRoots();
        services.AddSingleton<IRuntimeKernelScheduleRetentionRoots>(supplementalRoots);
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IPersistenceAccessContextAccessor>(new Access(tenant));
        services.AddWorkflowRuntime();
        services.AddRuntimeEntityFrameworkCore(new()
        {
            Provider = provider, ConnectionString = connectionString, CacheWorkflowExecutables = true,
            RecoveryContinuationSigningKey = new string('r', 64), HierarchyCursorSigningKey = new string('h', 64)
        });
        var queries = new PinPageCommands();
        if (provider == "Sqlite")
            services.ConfigureDbContext<RuntimeSqliteDbContext>(options => options.AddInterceptors(queries));
        else
            services.ConfigureDbContext<RuntimePostgreSqlDbContext>(options => options.AddInterceptors(queries));
        services.Configure<WorkflowExecutableGarbageCollectionOptions>(options => options.ArtifactCreationGracePeriod = TimeSpan.Zero);
        await using var app = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var writerScope = app.CreateAsyncScope();
        var db = writerScope.ServiceProvider.GetRequiredService<RuntimeDbContext>();
        await db.Database.MigrateAsync();
        var material = writerScope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>();
        var pins = writerScope.ServiceProvider.GetRequiredService<IRuntimeKernelExecutablePinStore>();

        var dependency = Artifact("dependency");
        var selected = Artifact("selected", new WorkflowExecutableDependency(dependency.Identity.ArtifactId, dependency.Identity.ArtifactHash, ["root"]));
        var orphan = Artifact("orphan");
        foreach (var artifact in new[] { selected, dependency, orphan }) await material.SaveAsync(artifact);
        var first = new RuntimeKernelExecutablePin(tenant, tenant.Value, "actor-one", selected.Identity, "issued-one");
        var second = first with { ActorReference = "actor-two", Issuance = "issued-two" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => pins.TryPinAsync([first with { Tenant = "other-tenant" }]).AsTask());

        await Assert.ThrowsAsync<WorkflowExecutableDependencyGraphException>(() =>
            pins.TryPinAsync([first, second with { Executable = selected.Identity with { ArtifactHash = "different-hash" } }]).AsTask());
        Assert.Empty(await pins.ListPinnedExecutableArtifactIdsAsync());

        var guard = Assert.IsType<WorkflowExecutableDeletionGuard>(await material.TryBeginDeletionAsync(dependency.Identity.ArtifactId,
            "deleting-dependency", now.AddMinutes(1), now));
        await Assert.ThrowsAsync<WorkflowExecutableRootWriteLeaseUnavailableException>(() => pins.TryPinAsync([first, second]).AsTask());
        Assert.Empty(await pins.ListPinnedExecutableArtifactIdsAsync());
        await material.CancelDeletionAsync(guard);

        await using var peerScope = app.CreateAsyncScope();
        var peer = peerScope.ServiceProvider.GetRequiredService<IRuntimeKernelExecutablePinStore>();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var contender = Task.Run(async () =>
        {
            await start.Task;
            return await peer.TryPinAsync([first, second]);
        });
        start.SetResult();
        Assert.All(await Task.WhenAll(contender, pins.TryPinAsync([first, second]).AsTask()), result => Assert.True(result));
        Assert.Equal(2, await db.KernelExecutablePins.CountAsync());
        await Assert.ThrowsAsync<InvalidDataException>(() => pins.TryPinAsync([first with { Issuance = "another-issuance" }]).AsTask());
        await Assert.ThrowsAsync<InvalidDataException>(() => pins.TryPinAsync([first with { Executable = orphan.Identity }]).AsTask());

        await using var reopened = app.CreateAsyncScope();
        var roots = reopened.ServiceProvider.GetRequiredService<IRuntimeKernelExecutablePinStore>();
        var collector = reopened.ServiceProvider.GetRequiredService<IWorkflowExecutableReferenceGarbageCollector>();
        Assert.Equal([selected.Identity.ArtifactId], await roots.ListPinnedExecutableArtifactIdsAsync());
        var tail = Assert.IsType<string>(await roots.ReadOpenTailAsync(tenant));
        var left = await roots.ListOpenAsync(tenant, null, tail, 1);
        var right = await roots.ListOpenAsync(tenant, Assert.IsType<string>(left.Next), tail, 1);
        Assert.Null(right.Next);
        Assert.Equal(new[] { first, second }, left.Items.Concat(right.Items).OrderBy(pin => pin.ActorReference, StringComparer.Ordinal));
        await Assert.ThrowsAsync<InvalidOperationException>(() => roots.ReadOpenTailAsync(new("other-scope")).AsTask());
        Assert.Equal(1, (await collector.SweepAsync()).DeletedArtifactCount);
        Assert.Null(await material.FindAsync(orphan.Identity.ArtifactId));
        Assert.NotNull(await material.FindAsync(selected.Identity.ArtifactId));
        Assert.NotNull(await material.FindAsync(dependency.Identity.ArtifactId));

        var manualOrphan = Artifact("manual-collector-orphan");
        await material.SaveAsync(manualOrphan);
        var servicesForCollection = reopened.ServiceProvider;
        var manualCollector = new WorkflowExecutableReferenceGarbageCollector(
            new OpaqueExecutableDecorator(servicesForCollection.GetRequiredService<IWorkflowExecutableStore>()),
            servicesForCollection.GetRequiredService<IWorkflowExecutableSourceReferenceStore>(),
            servicesForCollection.GetRequiredService<IWorkflowExecutionStateStore>(),
            Options.Create(new WorkflowExecutableGarbageCollectionOptions { ArtifactCreationGracePeriod = TimeSpan.Zero }),
            clock, NullLogger<WorkflowExecutableReferenceGarbageCollector>.Instance);
        Assert.Equal(1, (await manualCollector.SweepAsync()).DeletedArtifactCount);
        Assert.Null(await material.FindAsync(manualOrphan.Identity.ArtifactId));
        Assert.NotNull(await material.FindAsync(selected.Identity.ArtifactId));
        Assert.NotNull(await material.FindAsync(dependency.Identity.ArtifactId));

        var scheduled = Artifact("supplemental-schedule-root");
        await material.SaveAsync(scheduled);
        supplementalRoots.ArtifactId = scheduled.Identity.ArtifactId;
        Assert.Equal(0, (await collector.SweepAsync()).DeletedArtifactCount);
        Assert.NotNull(await material.FindAsync(scheduled.Identity.ArtifactId));
        supplementalRoots.ArtifactId = null;
        Assert.Equal(1, (await collector.SweepAsync()).DeletedArtifactCount);
        Assert.Null(await material.FindAsync(scheduled.Identity.ArtifactId));

        await db.KernelExecutablePins.ExecuteUpdateAsync(rows => rows.SetProperty(row => row.ScopeKeyHash, new string('A', 64)));
        await Assert.ThrowsAsync<InvalidDataException>(() => roots.ListPinnedExecutableArtifactIdsAsync().AsTask());
        var exactScopeHash = EfRuntimeOperationalStoreSupport.Hash(tenant.Value);
        await db.KernelExecutablePins.ExecuteUpdateAsync(rows => rows.SetProperty(row => row.ScopeKeyHash, exactScopeHash));

        Assert.False(await roots.TryReleaseAsync(first, (_, _) => ValueTask.FromResult(RuntimeKernelExecutablePinProof.Unresolved)));
        Assert.False(await roots.TryReleaseAsync(first, (_, _) => throw new TimeoutException()));
        Assert.False(await roots.TryReleaseAsync(first, (_, _) => ValueTask.FromResult((RuntimeKernelExecutablePinProof)40)));
        await Assert.ThrowsAsync<InvalidDataException>(() => roots.TryReleaseAsync(first with { Issuance = "invalid" }, UnexpectedProof).AsTask());
        Assert.Equal(0, (await collector.SweepAsync()).DeletedArtifactCount);

        Assert.True(await roots.TryReleaseAsync(first, (observed, _) =>
        {
            Assert.Equal(first, observed);
            return ValueTask.FromResult(RuntimeKernelExecutablePinProof.BirthRetired);
        }));
        Assert.True(await roots.TryReleaseAsync(first, UnexpectedProof));
        Assert.False(await roots.TryPinAsync([first, second with { ActorReference = "third", Issuance = "issued-three" }]));
        Assert.Equal(2, await db.KernelExecutablePins.CountAsync());
        Assert.Equal(0, (await collector.SweepAsync()).DeletedArtifactCount);
        Assert.True(await roots.TryReleaseAsync(second, (observed, _) =>
        {
            Assert.Equal(second, observed);
            return ValueTask.FromResult(RuntimeKernelExecutablePinProof.IssuanceFencedAbsent);
        }));
        Assert.Empty(await roots.ListPinnedExecutableArtifactIdsAsync());
        Assert.Null(await roots.ReadOpenTailAsync(tenant));
        Assert.All(await db.KernelExecutablePins.AsNoTracking().ToArrayAsync(), row =>
        {
            Assert.Equal(2, row.Revision);
            Assert.Null(row.ArtifactId);
            Assert.Null(row.ContentJson);
            Assert.Equal(64, row.Fingerprint.Length);
        });
        Assert.Equal(2, (await collector.SweepAsync()).DeletedArtifactCount);
        Assert.False(await roots.TryPinAsync([first, second]));

        await RefuseReplacementAsync(Artifact("replacement-root"), dependencyToReplace: null);
        var racedDependency = Artifact("replacement-dependency");
        var dependent = Artifact("replacement-parent",
            new WorkflowExecutableDependency(racedDependency.Identity.ArtifactId, racedDependency.Identity.ArtifactHash, ["root"]));
        await RefuseReplacementAsync(dependent, racedDependency);
        await CheckFiniteScanAsync();

        async Task CheckFiniteScanAsync()
        {
            var artifact = Artifact("ordered-scan-material");
            await material.SaveAsync(artifact);
            var legacy = new[] { Pin("legacy-one"), Pin("legacy-two") };
            Assert.True(await pins.TryPinAsync(legacy));

            // Use the real predecessor schema to establish the frozen prefix, then reapply the migration.
            // The disposable database contains only this provider contract's data.
            var migrations = db.Database.GetMigrations().ToArray();
            var scanMigration = Array.FindIndex(migrations, name => name.EndsWith("_KernelExecutablePinScan", StringComparison.Ordinal));
            Assert.True(scanMigration > 0);
            await db.GetService<IMigrator>().MigrateAsync(migrations[scanMigration - 1]);
            await db.Database.MigrateAsync();
            Assert.All(await db.KernelExecutablePins.AsNoTracking().Where(row => row.ReleasedAtUtcTicks == null).ToArrayAsync(),
                row => Assert.Equal(0, row.InsertionOrdinal));

            var newer = new[] { Pin("new-one"), Pin("new-two") };
            Assert.True(await pins.TryPinAsync([newer[0]]));
            Assert.True(await pins.TryPinAsync([newer[1]]));
            var scan = writerScope.ServiceProvider.GetRequiredService<IRuntimeKernelExecutablePinScanStore>();
            var peerScan = peerScope.ServiceProvider.GetRequiredService<IRuntimeKernelExecutablePinScanStore>();
            await Assert.ThrowsAsync<InvalidOperationException>(() => scan.ReadScanPageAsync(new("other-scope"), 1).AsTask());
            var firstPage = (await scan.ReadScanPageAsync(tenant, 1))!;
            var stale = (await peerScan.ReadScanPageAsync(tenant, 1))!;
            var failed = Assert.Single(firstPage.Items);
            Assert.Contains(failed, legacy);
            Assert.True(firstPage.HasMore);
            Assert.Equal(failed, Assert.Single(stale.Items));
            Assert.False(await pins.TryReleaseAsync(failed, (_, _) => throw new TimeoutException()));

            var late = new[] { Pin("late-one"), Pin("late-two"), Pin("late-three") };
            Assert.True(await peer.TryPinAsync(late));
            Assert.True(await scan.TryAdvanceScanAsync(tenant, firstPage));
            Assert.False(await peerScan.TryAdvanceScanAsync(tenant, stale));

            await using var restarted = app.CreateAsyncScope();
            var resumed = restarted.ServiceProvider.GetRequiredService<IRuntimeKernelExecutablePinScanStore>();
            queries.Capture = true;
            var crossing = (await resumed.ReadScanPageAsync(tenant, 2))!;
            queries.Capture = false;
            Assert.Collection(queries.Pages,
                prefix =>
                {
                    Assert.Contains("\"InsertionOrdinal\" = 0", prefix);
                    Assert.Contains("\"Id\" >", prefix);
                    Assert.Matches("""ORDER BY \w+\."ReleasedAtUtcTicks",""", prefix);
                    Assert.DoesNotContain("CASE", prefix);
                    Assert.DoesNotContain(" OR ", prefix);
                },
                positive =>
                {
                    Assert.Contains("\"InsertionOrdinal\" >", positive);
                    Assert.Matches("""ORDER BY \w+\."ReleasedAtUtcTicks",""", positive);
                    Assert.DoesNotContain(" OR ", positive);
                });
            Assert.Equal(new[] { legacy.Single(pin => pin != failed), newer[0] }, crossing.Items);
            Assert.True(crossing.HasMore);
            foreach (var member in legacy.Where(pin => pin != failed).Concat(newer))
            {
                var page = (await resumed.ReadScanPageAsync(tenant, 1))!;
                Assert.Equal(member, Assert.Single(page.Items));
                Assert.True(await resumed.TryAdvanceScanAsync(tenant, page));
            }
            Assert.Equal(failed, Assert.Single((await resumed.ReadScanPageAsync(tenant, 1))!.Items));

            RuntimeKernelExecutablePin Pin(string name) => new(tenant, tenant.Value, name, artifact.Identity, "issuance-" + name);
        }

        async Task RefuseReplacementAsync(WorkflowExecutable root, WorkflowExecutable? dependencyToReplace)
        {
            var old = dependencyToReplace ?? root;
            var replacement = Material(old.Identity with { ArtifactHash = old.Identity.ArtifactHash + "-new" }, []);
            if (dependencyToReplace is not null) await material.SaveAsync(dependencyToReplace);
            await material.SaveAsync(root);
            await using var replacingScope = app.CreateAsyncScope();
            var replacing = replacingScope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>();
            var hook = new BeforeLeaseStore(material, old.Identity.ArtifactId, async token =>
            {
                var deletion = Assert.IsType<WorkflowExecutableDeletionGuard>(await replacing.TryBeginDeletionAsync(old.Identity.ArtifactId,
                    "replace-before-lease", now.AddMinutes(1), now, token));
                Assert.True(await replacing.DeleteAsync(deletion, now, token));
                await replacing.SaveAsync(replacement, token);
            });
            var manager = new WorkflowExecutableRootWriteLeaseManager(hook, Options.Create(new WorkflowExecutableGarbageCollectionOptions()), clock);
            var racedPins = new EfRuntimeKernelExecutablePinStore(db, new Access(tenant), material, manager, clock);
            var pin = new RuntimeKernelExecutablePin(tenant, tenant.Value, root.Identity.ArtifactId, root.Identity, "race-issuance");
            await Assert.ThrowsAsync<WorkflowExecutableDependencyGraphException>(() => racedPins.TryPinAsync([pin]).AsTask());
            Assert.Equal(replacement.Identity, (await replacing.FindMaterialAsync(old.Identity.ArtifactId))?.Identity);
            Assert.Empty(await racedPins.ListPinnedExecutableArtifactIdsAsync());
        }

        static ValueTask<RuntimeKernelExecutablePinProof> UnexpectedProof(RuntimeKernelExecutablePin _, CancellationToken __) =>
            throw new InvalidOperationException("The store must not ask for a proof of this pin.");

        WorkflowExecutable Artifact(string id, params WorkflowExecutableDependency[] dependencies) =>
            Material(new(id, "definition-" + id, "version-" + id, "1", "hash-" + id), dependencies);

        WorkflowExecutable Material(WorkflowExecutableIdentity identity, WorkflowExecutableDependency[] dependencies) => new(identity,
            new ExecutableNode("root", "authored", "test/activity", "1.0.0",
                new RuntimeActivityDescriptor("test", "1.0.0", JsonSerializer.SerializeToElement(new { })),
                new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>()),
            new Dictionary<string, WorkflowExecutableResumeTarget>(), now.AddHours(-1), new Dictionary<string, string>(),
            inputContract: null, dependencies: dependencies, incidentStrategy: IncidentStrategyBuiltIns.FaultReference);
    }

    private sealed class BeforeLeaseStore(IWorkflowExecutableStore inner, string target, Func<CancellationToken, Task> beforeLease)
        : WorkflowExecutableStoreDecorator(inner), IWorkflowExecutableStore
    {
        private int _ran;

        bool IWorkflowExecutableStore.SupportsModelMaterial => Inner.SupportsModelMaterial;
        ValueTask<RuntimeStorePage<RuntimeExecutionMaterial>> IWorkflowExecutableStore.ListMaterialPageAsync(
            RuntimeStorePageRequest request, CancellationToken cancellationToken) => Inner.ListMaterialPageAsync(request, cancellationToken);

        public override async ValueTask<WorkflowExecutableRootWriteLease?> TryAcquireRootWriteLeaseAsync(string artifactId,
            string leaseId, DateTimeOffset expiresAt, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            if (artifactId == target && Interlocked.Exchange(ref _ran, 1) == 0)
                await beforeLease(cancellationToken);
            return await base.TryAcquireRootWriteLeaseAsync(artifactId, leaseId, expiresAt, now, cancellationToken);
        }
    }

    private sealed class OpaqueExecutableDecorator(IWorkflowExecutableStore inner) : WorkflowExecutableStoreDecorator(inner);

    private sealed class SupplementalScheduleRoots : IRuntimeKernelScheduleRetentionRoots
    {
        internal string? ArtifactId { get; set; }

        public ValueTask<IReadOnlyCollection<string>> ListPinnedExecutableArtifactIdsAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyCollection<string>>(ArtifactId is { } id ? [id] : []);
    }

    private sealed class Access(PersistenceScope scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(scope);
    }
}
