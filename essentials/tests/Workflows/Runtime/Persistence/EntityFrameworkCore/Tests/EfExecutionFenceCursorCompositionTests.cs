using Elsa.Persistence.EntityFramework.Tests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// #2564: which compositions give a lease a fence cursor. A lease the registered EF execution-liveness store wrote
/// has one, in whatever service scope and for whatever ownership service the store wrote it. A lease written by
/// any other store has none, and its commits read the ownership row as before.
/// </summary>
public sealed class EfExecutionFenceCursorCompositionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    private const string Run = "fence-cursor-execution";
    private static readonly PersistenceScope Partition = new("fence-cursor-tenant");

    [Fact]
    public async Task The_registered_liveness_store_holds_moves_and_removes_the_cursor_across_service_scopes()
    {
        var clock = new FakeTimeProvider(Now);
        var capture = new CommandCaptureInterceptor();
        await using var database = await SharedMemoryDatabase.CreateAsync();
        await using var provider = database.BuildProvider(clock, capture);
        await database.CreateSchemaAsync(provider);
        var scopes = provider.GetRequiredService<IPersistenceOperationScopeFactory>();
        var cursors = provider.GetRequiredService<EfExecutionFenceCursors>();
        await using var drain = await scopes.CreateAsync(Partition);
        var ownership = drain.ServiceProvider.GetRequiredService<IRuntimeExecutionOwnershipService>();

        var lease = await ownership.AcquireAsync(Run);
        var key = EfExecutionFenceCursors.Key.For(Partition.Value, lease);
        Assert.True(cursors.TryGet(key, out var acquired));
        Assert.Equal(new ExecutionFenceCursor(1, lease.ExpiresAt, RuntimeOperationalStateEfModule.SchemaVersion), acquired);

        // The lease runner renews through a scope of its own, with its own context and ownership service.
        clock.Advance(TimeSpan.FromSeconds(20));
        await using (var heartbeat = await scopes.CreateAsync(Partition))
        {
            Assert.NotSame(drain.ServiceProvider.GetRequiredService<RuntimeDbContext>(), heartbeat.ServiceProvider.GetRequiredService<RuntimeDbContext>());
            Assert.True((await heartbeat.ServiceProvider.GetRequiredService<IRuntimeExecutionOwnershipService>().HeartbeatAsync(lease)).Succeeded);
        }
        Assert.True(cursors.TryGet(key, out var renewed));
        Assert.Equal(new ExecutionFenceCursor(2, lease.ExpiresAt.AddSeconds(20), RuntimeOperationalStateEfModule.SchemaVersion), renewed);

        // The commit in the drain's scope finds the cursor the other scope moved.
        var store = drain.ServiceProvider.GetRequiredService<EfRuntimeCheckpointCommitStore>();
        var before = capture.Commands.Count;
        await store.CommitAsync(Commit("commit-1", lease), new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate));
        Assert.Equal((0, 1), OwnershipCommands(capture, before));
        Assert.True(cursors.TryGet(key, out var committed));
        Assert.Equal(renewed with { Revision = 3 }, committed);

        // The same lease in another partition is another row, and has no cursor.
        Assert.False(cursors.TryGet(key with { Scope = "fence-cursor-neighbor" }, out _));

        Assert.True((await ownership.ReleaseAsync(lease)).Succeeded);
        Assert.Equal(0, cursors.Count);
    }

    [Theory]
    [InlineData("another-ownership-service-over-the-registered-store", true)]
    [InlineData("liveness-store-that-delegates-to-the-ef-store", false)]
    [InlineData("liveness-store-that-writes-the-table-itself", false)]
    public async Task A_lease_has_a_cursor_exactly_when_the_registered_liveness_store_wrote_it(string composition, bool writtenByTheRegisteredStore)
    {
        // The registered EF store records a cursor for what it saves, whoever asked it to. It is handed the cursors
        // when it is resolved as the liveness contract. A host that replaces that contract, with a store that
        // delegates to the EF store or with one that writes the table itself, gets no cursors: its leases have none
        // and their commits read the row.
        var clock = new FakeTimeProvider(Now);
        var capture = new CommandCaptureInterceptor();
        await using var database = await SharedMemoryDatabase.CreateAsync();
        await using var provider = database.BuildProvider(clock, capture, services =>
        {
            switch (composition)
            {
                case "liveness-store-that-delegates-to-the-ef-store":
                    services.RemoveAll<IExecutionLivenessStateStore>();
                    services.AddScoped<IExecutionLivenessStateStore>(serviceProvider =>
                        new ForwardingLivenessStore(serviceProvider.GetRequiredService<EfExecutionLivenessStateStore>()));
                    break;
                case "another-ownership-service-over-the-registered-store":
                    services.Replace(ServiceDescriptor.Scoped<IRuntimeExecutionOwnershipService>(serviceProvider =>
                        new ForwardingOwnershipService(new RuntimeExecutionOwnershipService(
                            serviceProvider.GetRequiredService<IExecutionLivenessStateStore>(),
                            serviceProvider.GetRequiredService<TimeProvider>(),
                            serviceProvider.GetRequiredService<RuntimeExecutionOwnershipOptions>()))));
                    break;
                default:
                    services.RemoveAll<IExecutionLivenessStateStore>();
                    services.AddScoped<IExecutionLivenessStateStore>(serviceProvider => new ForeignLivenessStore(
                        serviceProvider.GetRequiredService<RuntimeDbContext>(),
                        serviceProvider.GetRequiredService<IPersistenceAccessContextAccessor>()));
                    break;
            }
        });
        await database.CreateSchemaAsync(provider);
        var cursors = provider.GetRequiredService<EfExecutionFenceCursors>();
        await using var drain = await provider.GetRequiredService<IPersistenceOperationScopeFactory>().CreateAsync(Partition);
        var ownership = drain.ServiceProvider.GetRequiredService<IRuntimeExecutionOwnershipService>();
        var store = drain.ServiceProvider.GetRequiredService<EfRuntimeCheckpointCommitStore>();

        var lease = await ownership.AcquireAsync(Run);
        Assert.True((await ownership.HeartbeatAsync(lease)).Succeeded);
        Assert.Equal(writtenByTheRegisteredStore, cursors.TryGet(EfExecutionFenceCursors.Key.For(Partition.Value, lease), out _));

        foreach (var commitId in new[] { "commit-1", "commit-2" })
        {
            var before = capture.Commands.Count;
            await store.CommitAsync(Commit(commitId, lease), new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate));
            Assert.Equal(writtenByTheRegisteredStore ? (0, 1) : (1, 1), OwnershipCommands(capture, before));
        }

        Assert.Equal(writtenByTheRegisteredStore ? 1 : 0, cursors.Count);
    }

    private static RuntimeCheckpointCommit Commit(string commitId, RuntimeExecutionLease lease) => new(
        commitId,
        new RuntimeCheckpoint($"checkpoint-{commitId}", "EmptyCheckpoint", Run, Now, [], new Dictionary<string, string>()),
        new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], []),
        [],
        new Dictionary<string, string>())
    {
        ExpectedFence = lease.ToFence()
    };

    private static (int Reads, int Writes) OwnershipCommands(CommandCaptureInterceptor capture, int from)
    {
        var commands = capture.Commands.Skip(from)
            .Where(command => command.Contains(RuntimeOperationalStateEfModule.ExecutionLivenessTableName, StringComparison.Ordinal))
            .ToArray();
        return (commands.Count(command => command.StartsWith("SELECT", StringComparison.Ordinal)),
            commands.Count(command => command.StartsWith("UPDATE", StringComparison.Ordinal)));
    }

    private sealed class ForwardingLivenessStore(IExecutionLivenessStateStore inner) : IExecutionLivenessStateStore
    {
        public ValueTask<ExecutionLivenessState> SaveAsync(ExecutionLivenessState state, CancellationToken cancellationToken = default) =>
            inner.SaveAsync(state, cancellationToken);

        public ValueTask<ExecutionLivenessStateWriteResult> TrySaveAsync(ExecutionLivenessState state, long expectedRevision, CancellationToken cancellationToken = default) =>
            inner.TrySaveAsync(state, expectedRevision, cancellationToken);

        public ValueTask<ExecutionLivenessState?> FindAsync(string workflowExecutionId, string operationalStateId, CancellationToken cancellationToken = default) =>
            inner.FindAsync(workflowExecutionId, operationalStateId, cancellationToken);

        public ValueTask<VersionedExecutionLivenessState?> FindVersionedAsync(string workflowExecutionId, string operationalStateId, CancellationToken cancellationToken = default) =>
            inner.FindVersionedAsync(workflowExecutionId, operationalStateId, cancellationToken);
    }

    /// <summary>
    /// A liveness store that is not the EF store: it writes the same table through the context, with the same
    /// revision compare-and-swap, and the EF store never sees its writes.
    /// </summary>
    private sealed class ForeignLivenessStore(RuntimeDbContext context, IPersistenceAccessContextAccessor accessor) : IExecutionLivenessStateStore
    {
        private string Scope => accessor.Current.RequireScope().Value;

        public ValueTask<ExecutionLivenessState> SaveAsync(ExecutionLivenessState state, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async ValueTask<ExecutionLivenessStateWriteResult> TrySaveAsync(ExecutionLivenessState state, long expectedRevision, CancellationToken cancellationToken = default)
        {
            context.ChangeTracker.Clear();
            var id = EfRuntimeOperationalStoreSupport.CompositeId(Scope, state.WorkflowExecutionId, state.ExecutionLivenessStateId);
            var row = await context.ExecutionLivenessStates.SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
            if (row is null != (expectedRevision == 0) || row is not null && row.Revision != expectedRevision)
                return new(ExecutionLivenessStateWriteStatus.RevisionConflict, row?.Revision);
            if (row is null)
                context.ExecutionLivenessStates.Add(EfExecutionLivenessStateStore.ToEntity(context, state, Scope, 1));
            else
                EfExecutionLivenessStateStore.Copy(context, row, state, Scope, expectedRevision + 1);
            await context.SaveChangesAsync(cancellationToken);
            context.ChangeTracker.Clear();
            return new(ExecutionLivenessStateWriteStatus.Saved, expectedRevision + 1);
        }

        public async ValueTask<ExecutionLivenessState?> FindAsync(string workflowExecutionId, string operationalStateId, CancellationToken cancellationToken = default) =>
            (await FindVersionedAsync(workflowExecutionId, operationalStateId, cancellationToken))?.State;

        public async ValueTask<VersionedExecutionLivenessState?> FindVersionedAsync(string workflowExecutionId, string operationalStateId, CancellationToken cancellationToken = default)
        {
            var id = EfRuntimeOperationalStoreSupport.CompositeId(Scope, workflowExecutionId, operationalStateId);
            var row = await context.ExecutionLivenessStates.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
            return row is null ? null : new(EfExecutionLivenessStateStore.Read(row, Scope, workflowExecutionId, operationalStateId), row.Revision);
        }
    }

    private sealed class ForwardingOwnershipService(IRuntimeExecutionOwnershipService inner) : IRuntimeExecutionOwnershipService
    {
        public ValueTask<RuntimeExecutionLease> AcquireAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
            inner.AcquireAsync(workflowExecutionId, cancellationToken);

        public ValueTask<RuntimeExecutionOwnershipTransitionResult> HeartbeatAsync(RuntimeExecutionLease lease, CancellationToken cancellationToken = default) =>
            inner.HeartbeatAsync(lease, cancellationToken);

        public ValueTask<RuntimeExecutionOwnershipTransitionResult> ReleaseAsync(RuntimeExecutionLease lease, CancellationToken cancellationToken = default) =>
            inner.ReleaseAsync(lease, cancellationToken);

        public ValueTask EnsureCurrentAsync(string workflowExecutionId, long fencingToken, CancellationToken cancellationToken = default) =>
            inner.EnsureCurrentAsync(workflowExecutionId, fencingToken, cancellationToken);
    }

    private sealed class SharedMemoryDatabase(SqliteConnection keeper) : IAsyncDisposable
    {
        public static async Task<SharedMemoryDatabase> CreateAsync()
        {
            var keeper = new SqliteConnection($"Data Source=file:runtime-fence-cursor-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Pooling=False");
            try
            {
                await keeper.OpenAsync();
                return new SharedMemoryDatabase(keeper);
            }
            catch
            {
                await keeper.DisposeAsync();
                throw;
            }
        }

        public ServiceProvider BuildProvider(FakeTimeProvider clock, CommandCaptureInterceptor capture, Action<IServiceCollection>? compose = null)
        {
            const string connectionName = "RuntimeFenceCursor";
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { [$"ConnectionStrings:{connectionName}"] = keeper.ConnectionString })
                .Build());
            services.AddWorkflowRuntime();
            services.AddRuntimeEntityFrameworkCore(new RuntimeEntityFrameworkCoreOptions
            {
                Provider = "Sqlite",
                ConnectionName = connectionName,
                ConnectionString = null,
                RecoveryContinuationSigningKey = "runtime-fence-cursor-test-recovery-key-32-bytes",
                HierarchyCursorSigningKey = "runtime-fence-cursor-test-hierarchy-key-32-bytes"
            });
            services.ConfigureDbContext<RuntimeSqliteDbContext>(options => options.AddInterceptors(capture));
            services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock));
            services.AddPersistenceModes(options => options.Mode = PersistenceMode.Durable);
            compose?.Invoke(services);
            return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        }

        public async Task CreateSchemaAsync(IServiceProvider provider)
        {
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<RuntimeDbContext>().Database.EnsureCreatedAsync();
        }

        public ValueTask DisposeAsync() => keeper.DisposeAsync();
    }
}
