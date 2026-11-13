using System.Data.Common;
using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Extensions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>Actual bounded source leases over SQLite owner rows; no source import or promotion acceptance claim.</summary>
public sealed class RuntimeBoundedSourceLeaseTests
{
    [Fact]
    public async Task Bounded_lease_authorization_precedes_every_child_body_read()
    {
        await using var owner = await Owner.Create(); var child = Workflow("child"); var root = Workflow("parent", [new(child.Identity.ArtifactId, child.Identity.ArtifactHash, ["node"])]);
        await owner.Store.SaveAsync(child); await owner.Store.SaveAsync(root); var counted = new CountingStore(owner.Store);
        var manager = Manager(counted); var invoked = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => manager.ExecuteFencedAsync([root.Identity], "read-permission", Owner.Partition, new(),
            (id, _, _, _) => id == child.Identity.ArtifactId ? ValueTask.FromException(new UnauthorizedAccessException()) : ValueTask.CompletedTask,
            (_, _) => { invoked = true; return ValueTask.CompletedTask; }).AsTask());
        Assert.False(invoked); Assert.DoesNotContain(child.Identity.ArtifactId, counted.Reads);
    }

    [Fact]
    public async Task Bounded_lease_uses_the_captured_qualified_partition_when_the_accessor_changes_after_admission()
    {
        await using var owner = await Owner.Create(); var workflow = Workflow(); await owner.Store.SaveAsync(workflow);
        var otherScope = new PersistenceScope("other-partition"); var other = new EfWorkflowExecutableStore(owner.Context, new FixedAccessor(otherScope)); await other.SaveAsync(workflow);
        var changing = new EfWorkflowExecutableStore(owner.Context, new ChangingAccessor(Owner.Partition, otherScope)); var now = DateTimeOffset.UtcNow;
        var grant = await ((IRuntimeBoundedSourceLeaseStore)changing).TryAcquireAsync(workflow.Identity.ArtifactId, "captured-scope", now.AddMinutes(1), now,
            Owner.Partition, new()); Assert.NotNull(grant);
        var row = await owner.Context.WorkflowExecutableRootWriteLeases.AsNoTracking().SingleAsync();
        Assert.Equal(Elsa.Persistence.EntityFramework.EfRelationalIdentity.Encode(Owner.Partition.Value), row.ScopeKey);
        await owner.Store.ReleaseRootWriteLeaseAsync(grant);
    }

    [Fact]
    public async Task Ordinary_decorator_does_not_inherit_bounded_read_or_retention_authority()
    {
        await using var owner = await Owner.Create(); IWorkflowExecutableStore decorated = new OrdinaryDecorator(owner.Store);
        Assert.True((await Assert.ThrowsAsync<RuntimeSourceReadException>(() => decorated.FindMaterialBoundedAsync("id", Owner.Partition, new()).AsTask())).Unsupported);
        await Assert.ThrowsAsync<RuntimeSourceReadException>(() => Manager(decorated).ExecuteFencedAsync([Workflow().Identity], "unsupported", Owner.Partition, new(),
            (_, _, _, _) => ValueTask.CompletedTask, (_, _) => ValueTask.CompletedTask).AsTask());
    }

    [Theory]
    [InlineData("artifact-id", "runtime_source_dependency_missing")]
    [InlineData("artifact-hash", "runtime_source_pin_conflict")]
    [InlineData("definition-id", "runtime_source_pin_conflict")]
    [InlineData("definition-version-id", "runtime_source_pin_conflict")]
    [InlineData("artifact-version", "runtime_source_pin_conflict")]
    public async Task Bounded_root_set_checks_all_five_pin_fields_before_acquiring_any_lease(string field, string code)
    {
        await using var owner = await Owner.Create(); var root = Workflow(); await owner.Store.SaveAsync(root); var callback = false;
        var requested = field switch
        {
            "artifact-id" => root.Identity with { ArtifactId = "different-artifact" },
            "artifact-hash" => root.Identity with { ArtifactHash = "different-hash" },
            "definition-id" => root.Identity with { DefinitionId = "different-producer" },
            "definition-version-id" => root.Identity with { DefinitionVersionId = "different-version" },
            _ => root.Identity with { ArtifactVersion = "2.0.0" }
        };
        var exception = await Assert.ThrowsAsync<RuntimeSourceReadException>(() => Manager(owner.Store).ExecuteFencedAsync(
            [requested], "pin-conflict", Owner.Partition, new(),
            (_, _, _, _) => ValueTask.CompletedTask, (_, _) => { callback = true; return ValueTask.CompletedTask; }).AsTask());
        Assert.Equal(code, exception.Code); Assert.False(callback);
        Assert.Equal(0, await owner.Context.WorkflowExecutableRootWriteLeases.CountAsync());
    }

    [Theory]
    [InlineData("cycle", "runtime_source_dependency_cycle")]
    [InlineData("long-second-path", "runtime_source_dependency_depth_limit")]
    [InlineData("count", "runtime_source_material_limit")]
    public async Task Actual_stored_graph_bounds_are_checked_before_any_lease(string profile, string code)
    {
        await using var owner = await Owner.Create();
        // These deliberately controlled stored identities exercise the retention graph, not semantic hash acceptance.
        WorkflowExecutable Stored(string id, params string[] children)
        {
            var original = Workflow(id); var edges = children.Select(child => new WorkflowExecutableDependency(child, "hash-" + child, ["node"])).ToArray();
            return new(new(id, "producer", "version", "1.0.0", "hash-" + id), original.RootActivity, original.ResumeTargets, original.CreatedAt,
                original.CompatibilityMetadata, original.InputContract, edges, original.IncidentStrategy);
        }
        var items = profile == "cycle" ? new[] { Stored("root", "child"), Stored("child", "root") } :
            new[] { Stored("root", "target", "one"), Stored("one", "two"), Stored("two", "three"), Stored("three", "target"), Stored("target") };
        foreach (var item in items) await owner.Store.SaveAsync(item);
        var limits = new RuntimeSourceReadLimits { MaxMaterials = profile == "count" ? 1 : 100, MaxDependencyDepth = 3 };
        Assert.Equal(code, (await Assert.ThrowsAsync<RuntimeSourceReadException>(() => Manager(owner.Store).ExecuteFencedAsync([items[0].Identity], "graph-limit",
            Owner.Partition, limits, (_, _, _, _) => ValueTask.CompletedTask, (_, _) => ValueTask.CompletedTask).AsTask())).Code);
        Assert.Equal(0, await owner.Context.WorkflowExecutableRootWriteLeases.CountAsync());
    }

    [Fact]
    public async Task Complete_actual_closure_stays_leased_through_callback_and_blocks_collection()
    {
        await using var owner = await Owner.Create(); var child = Workflow("child");
        var root = Workflow("parent", [new(child.Identity.ArtifactId, child.Identity.ArtifactHash, ["node"])]);
        await owner.Store.SaveAsync(child); await owner.Store.SaveAsync(root); var callback = false;
        await Manager(owner.Store).ExecuteFencedAsync([root.Identity], "capture-held", Owner.Partition, new(),
            (_, _, _, _) => ValueTask.CompletedTask, async (grants, token) =>
            {
                callback = true; Assert.Equal(2, grants.Count); Assert.Equal(2, await owner.Context.WorkflowExecutableRootWriteLeases.CountAsync(token));
                var now = DateTimeOffset.UtcNow;
                Assert.Null(await owner.Store.TryBeginDeletionAsync(child.Identity.ArtifactId, "competing-gc", now.AddMinutes(1), now, token));
            });
        Assert.True(callback); Assert.Equal(0, await owner.Context.WorkflowExecutableRootWriteLeases.CountAsync());
    }

    [Fact]
    public async Task Actual_model_to_native_closure_is_retained_with_the_complete_worker_pin()
    {
        await using var owner = await Owner.Create(); var worker = Workflow("worker"); await owner.Store.SaveAsync(worker);
        var provisional = new WorkflowExecutableIdentity("model", "model-producer", "model-version", "1.0.0", "initial");
        var seed = new RuntimeModelMaterial(1, provisional, new("fixture.protocol", 1), "fixture-profile",
            JsonSerializer.SerializeToElement(new { graph = "controlled" }), new(1, []), [], [new("worker", worker.Identity)], []);
        var model = new RuntimeModelMaterial(1, provisional with { ArtifactHash = seed.ComputeContentHash() }, seed.Protocol,
            seed.Profile, seed.Definition, seed.InputContract, [], seed.Dependencies, []);
        await ((IWorkflowExecutableStore)owner.Store).SaveModelAsync(model);
        var reads = new List<(string Id, WorkflowExecutableIdentity? Full)>();
        await Manager(owner.Store).ExecuteFencedAsync([model.Identity], "model-closure", Owner.Partition, new(),
            (id, _, full, _) => { reads.Add((id, full)); return ValueTask.CompletedTask; }, async (grants, token) =>
            {
                Assert.Equal(2, grants.Count);
                Assert.Contains(reads, read => read.Id == worker.Identity.ArtifactId && read.Full == worker.Identity);
                Assert.Equal(2, await owner.Context.WorkflowExecutableRootWriteLeases.CountAsync(token));
            });
        Assert.Equal(0, await owner.Context.WorkflowExecutableRootWriteLeases.CountAsync());
    }

    [Fact]
    public async Task Changed_actual_SQLite_incarnation_between_inspection_and_acquisition_refuses_callback_and_releases_grants()
    {
        await using var owner = await Owner.Create(); var root = Workflow(); await owner.Store.SaveAsync(root);
        var counted = new CountingStore(owner.Store);
        counted.BeforeAcquire = async () =>
        {
            var replacement = Guid.NewGuid().ToString("N");
            await owner.Context.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_runtime_workflow_executable SET IncarnationId={replacement}");
            await owner.Context.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_runtime_workflow_executable_coordination SET IncarnationId={replacement}");
        };
        var invoked = false;
        var exception = await Assert.ThrowsAsync<RuntimeSourceReadException>(() => Manager(counted).ExecuteFencedAsync(
            [root.Identity], "incarnation-race", Owner.Partition, new(), (_, _, _, _) => ValueTask.CompletedTask,
            (_, _) => { invoked = true; return ValueTask.CompletedTask; }).AsTask());
        Assert.Equal("runtime_source_closure_changed", exception.Code); Assert.False(invoked);
        Assert.Equal(0, await owner.Context.WorkflowExecutableRootWriteLeases.CountAsync());
    }

    [Fact]
    public async Task Bounded_renewal_checks_raw_lease_identity_before_provider_string_hydration()
    {
        await using var owner = await Owner.Create(); var workflow = Workflow(); await owner.Store.SaveAsync(workflow);
        var now = DateTimeOffset.UtcNow; var store = (IRuntimeBoundedSourceLeaseStore)owner.Store;
        var grant = await store.TryAcquireAsync(workflow.Identity.ArtifactId, "renew-corrupt", now.AddMinutes(1), now, Owner.Partition, new()); Assert.NotNull(grant);
        await owner.Context.Database.ExecuteSqlRawAsync("UPDATE elsa_runtime_workflow_executable_root_write_lease SET Token=CAST(X'FF' AS TEXT)");
        var exception = await Assert.ThrowsAsync<RuntimeSourceReadException>(() =>
            store.RenewAsync(grant, now.AddMinutes(2), now, Owner.Partition, new()).AsTask());
        Assert.Equal("runtime_source_stored_content_invalid", exception.Code);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Builtin_cache_adapters_forward_qualified_source_reads_and_all_lease_operations(bool cached)
    {
        await using var owner = await Owner.Create(); var workflow = Workflow(); await owner.Store.SaveAsync(workflow);
        var cache = new WorkflowExecutableCache(new WorkflowExecutableCacheOptions());
        IWorkflowExecutableStore selected = cached ? new CachingWorkflowExecutableStore(owner.Store, cache, Owner.Partition.Value,
            (_, _) => throw new InvalidOperationException("Source reads must bypass the body cache.")) :
            new InvalidatingWorkflowExecutableStore(owner.Store, cache, Owner.Partition.Value);
        Assert.Equal(workflow.Identity, (await selected.FindMaterialBoundedAsync(workflow.Identity.ArtifactId, Owner.Partition, new()))!.Material.Identity);
        var leases = Assert.IsAssignableFrom<IRuntimeBoundedSourceLeaseStore>(selected); var now = DateTimeOffset.UtcNow;
        var grant = await leases.TryAcquireAsync(workflow.Identity.ArtifactId, "selected-cache", now.AddMinutes(1), now, Owner.Partition, new()); Assert.NotNull(grant);
        Assert.True(await leases.RenewAsync(grant, now.AddMinutes(2), now, Owner.Partition, new()));
        await leases.ReleaseAsync(grant, Owner.Partition, new()); Assert.Equal(0, await owner.Context.WorkflowExecutableRootWriteLeases.CountAsync());
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task Actual_runtime_EF_feature_selection_preserves_bounded_retention(bool cache, bool privileged)
    {
        var path = Path.Combine(Path.GetTempPath(), "elsa-source-selected-" + Guid.NewGuid().ToString("N") + ".sqlite");
        try
        {
            var services = new ServiceCollection();
            services.AddWorkflowRuntime();
            services.AddSingleton<IPersistenceAccessContextAccessor>(new SelectedAccessor(privileged));
            new RuntimeEntityFrameworkCoreFeature
            {
                Provider = "Sqlite", ConnectionString = "Data Source=" + path + ";Pooling=False",
                CacheWorkflowExecutables = cache,
                RecoveryContinuationSigningKey = "source-selected-recovery-signing-key-32-bytes",
                HierarchyCursorSigningKey = "source-selected-hierarchy-signing-key-32-bytes"
            }.ConfigureServices(services);
            await using var provider = services.BuildServiceProvider(validateScopes: true);
            await using var scope = provider.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<RuntimeDbContext>();
            await context.Database.EnsureCreatedAsync();
            var selected = scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>();
            Assert.Equal(cache ? privileged ? typeof(InvalidatingWorkflowExecutableStore) : typeof(CachingWorkflowExecutableStore) :
                typeof(EfWorkflowExecutableStore), selected.GetType());
            var workflow = Workflow(); await selected.SaveAsync(workflow);
            await Manager(selected).ExecuteFencedAsync([workflow.Identity], "composed-source", Owner.Partition, new(),
                (_, _, _, _) => ValueTask.CompletedTask, async (grants, token) =>
                {
                    Assert.Single(grants);
                    Assert.Equal(1, await context.WorkflowExecutableRootWriteLeases.CountAsync(token));
                    var now = DateTimeOffset.UtcNow;
                    Assert.Null(await selected.TryBeginDeletionAsync(workflow.Identity.ArtifactId, "composed-gc", now.AddMinutes(1), now, token));
                });
            Assert.Equal(0, await context.WorkflowExecutableRootWriteLeases.CountAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools(); File.Delete(path); File.Delete(path + "-wal"); File.Delete(path + "-shm");
        }
    }

    private sealed class SelectedAccessor(bool privileged) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = privileged
            ? PersistenceAccessContext.PrivilegedScoped(Owner.Partition, new("release-source-capture"))
            : PersistenceAccessContext.Scoped(Owner.Partition);
    }

    private static WorkflowExecutable Workflow(string id = "root", IReadOnlyCollection<WorkflowExecutableDependency>? dependencies = null)
    {
        var node = new ExecutableNode("node", "node", "fixture.Module", "1.0.0", "fixture.consumer", JsonSerializer.SerializeToElement(new { z = "a" }),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>());
        var input = new WorkflowExecutableInputContract(1, []); var edges = dependencies ?? []; var hasher = new WorkflowExecutableHasher(); var hash = hasher.ComputeHash(node, input, edges);
        return new(new(id + "-" + hash[7..], "producer", "producer-version", "1.0.0", hash), node, new Dictionary<string, WorkflowExecutableResumeTarget>(),
            DateTimeOffset.UnixEpoch, new Dictionary<string, string>(), input, edges, IncidentStrategyBuiltIns.FaultReference);
    }
    private static WorkflowExecutableRootWriteLeaseManager Manager(IWorkflowExecutableStore store) => new(store,
        Options.Create(new WorkflowExecutableGarbageCollectionOptions { RootWriteLeaseDuration = TimeSpan.FromMinutes(1) }), TimeProvider.System);
    private sealed class OrdinaryDecorator(IWorkflowExecutableStore inner) : WorkflowExecutableStoreDecorator(inner);
    private sealed class CountingStore(IWorkflowExecutableStore inner) : WorkflowExecutableStoreDecorator(inner), IWorkflowExecutableStore, IRuntimeBoundedSourceLeaseStore
    {
        public List<string> Reads { get; } = [];
        public Func<ValueTask>? BeforeAcquire { get; set; }
        public bool SupportsModelMaterial => inner.SupportsModelMaterial;
        public ValueTask<RuntimeMaterialSourceSnapshot?> FindMaterialBoundedAsync(string id, PersistenceScope scope, RuntimeSourceReadLimits limits, CancellationToken token = default)
        { Reads.Add(id); return inner.FindMaterialBoundedAsync(id, scope, limits, token); }
        public override ValueTask<RuntimeStorePage<WorkflowExecutable>> ListPageAsync(RuntimeStorePageRequest request, CancellationToken token = default) =>
            throw new InvalidOperationException("Bounded source retention must never enumerate unrelated material.");
        public async ValueTask<WorkflowExecutableRootWriteLease?> TryAcquireAsync(string id, string leaseId, DateTimeOffset expires, DateTimeOffset now, PersistenceScope scope, RuntimeSourceReadLimits limits, CancellationToken token = default)
        {
            var before = BeforeAcquire; BeforeAcquire = null;
            if (before is not null) await before();
            return await ((IRuntimeBoundedSourceLeaseStore)inner).TryAcquireAsync(id, leaseId, expires, now, scope, limits, token);
        }
        public ValueTask<bool> RenewAsync(WorkflowExecutableRootWriteLease lease, DateTimeOffset expires, DateTimeOffset now, PersistenceScope scope, RuntimeSourceReadLimits limits, CancellationToken token = default) => ((IRuntimeBoundedSourceLeaseStore)inner).RenewAsync(lease, expires, now, scope, limits, token);
        public ValueTask ReleaseAsync(WorkflowExecutableRootWriteLease lease, PersistenceScope scope, RuntimeSourceReadLimits limits, CancellationToken token = default) => ((IRuntimeBoundedSourceLeaseStore)inner).ReleaseAsync(lease, scope, limits, token);
    }
    private sealed class Accessor : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(Owner.Partition); }
    private sealed class FixedAccessor(PersistenceScope scope) : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(scope); }
    private sealed class ChangingAccessor(PersistenceScope first, PersistenceScope after) : IPersistenceAccessContextAccessor
    { private int _reads; public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(Interlocked.Increment(ref _reads) == 1 ? first : after); }
    private sealed class Cursor : IRuntimeRecoveryContinuationCodec
    { public string Encode(string purpose, ReadOnlySpan<byte> payload) => Convert.ToBase64String(payload); public byte[] Decode(string purpose, string token) => Convert.FromBase64String(token); }
    private sealed class Spy : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Commands.Add(command.CommandText); return ValueTask.FromResult(result); }
    }
    private sealed class Owner : IAsyncDisposable
    {
        public static PersistenceScope Partition { get; } = new("source-partition");
        private readonly string _path = Path.Combine(Path.GetTempPath(), "elsa-release-source-" + Guid.NewGuid().ToString("N") + ".sqlite");
        public Accessor Access { get; } = new(); public Spy Spy { get; } = new();
        public RuntimeSqliteDbContext Context { get; private set; } = null!;
        public EfWorkflowExecutableStore Store { get; private set; } = null!;
        public EfWorkflowExecutableSourceReferenceStore References { get; private set; } = null!;
        public EfExecutableActivityTemplateStore Templates { get; private set; } = null!;
        public static async Task<Owner> Create(bool compressed = false)
        {
            var owner = new Owner();
            var options = new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite("Data Source=" + owner._path + ";Pooling=False").AddInterceptors(owner.Spy);
            if (compressed) options.UseElsaPayloadCompression(new() { Codec = EfPayloadCompression.GZip, MinimumLength = 0 });
            owner.Context = new(options.Options);
            await owner.Context.Database.EnsureCreatedAsync(); owner.Store = new(owner.Context, owner.Access); owner.References = new(owner.Context, owner.Access, new Cursor());
            owner.Templates = new(owner.Context, owner.Access, new Cursor()); return owner;
        }
        public async ValueTask DisposeAsync()
        { await Context.DisposeAsync(); SqliteConnection.ClearAllPools(); File.Delete(_path); File.Delete(_path + "-wal"); File.Delete(_path + "-shm"); }
    }
}
