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
/// #2564: which compositions give a lease a fence cursor. The runtime's own ownership service over the EF
/// execution-liveness store does, in whatever service scope it runs. A decorated liveness store or another ownership
/// service does not, and its commits read the ownership row as before.
/// </summary>
public sealed class EfExecutionFenceCursorCompositionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    private const string Run = "fence-cursor-execution";
    private static readonly PersistenceScope Partition = new("fence-cursor-tenant");

    [Fact]
    public async Task The_ownership_service_holds_moves_and_removes_the_cursor_across_service_scopes()
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
        Assert.Equal(new ExecutionFenceCursor(1, lease.AcquiredAt, lease.ExpiresAt), acquired);

        // The lease runner renews through a scope of its own, with its own context and ownership service.
        clock.Advance(TimeSpan.FromSeconds(20));
        await using (var heartbeat = await scopes.CreateAsync(Partition))
        {
            Assert.NotSame(drain.ServiceProvider.GetRequiredService<RuntimeDbContext>(), heartbeat.ServiceProvider.GetRequiredService<RuntimeDbContext>());
            Assert.True((await heartbeat.ServiceProvider.GetRequiredService<IRuntimeExecutionOwnershipService>().HeartbeatAsync(lease)).Succeeded);
        }
        Assert.True(cursors.TryGet(key, out var renewed));
        Assert.Equal(new ExecutionFenceCursor(2, lease.AcquiredAt, lease.ExpiresAt.AddSeconds(20)), renewed);

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
    [InlineData("decorated-liveness-store")]
    [InlineData("replaced-ownership-service")]
    public async Task A_lease_that_another_composition_manages_gets_no_cursor_and_its_commits_read_the_row(string composition)
    {
        var clock = new FakeTimeProvider(Now);
        var capture = new CommandCaptureInterceptor();
        await using var database = await SharedMemoryDatabase.CreateAsync();
        await using var provider = database.BuildProvider(clock, capture, services =>
        {
            if (composition == "decorated-liveness-store")
            {
                services.RemoveAll<IExecutionLivenessStateStore>();
                services.AddScoped<IExecutionLivenessStateStore>(serviceProvider =>
                    new ForwardingLivenessStore(serviceProvider.GetRequiredService<EfExecutionLivenessStateStore>()));
            }
            else
            {
                services.Replace(ServiceDescriptor.Scoped<IRuntimeExecutionOwnershipService>(serviceProvider =>
                    new ForwardingOwnershipService(new RuntimeExecutionOwnershipService(
                        serviceProvider.GetRequiredService<IExecutionLivenessStateStore>(),
                        serviceProvider.GetRequiredService<TimeProvider>(),
                        serviceProvider.GetRequiredService<RuntimeExecutionOwnershipOptions>()))));
            }
        });
        await database.CreateSchemaAsync(provider);
        var cursors = provider.GetRequiredService<EfExecutionFenceCursors>();
        await using var drain = await provider.GetRequiredService<IPersistenceOperationScopeFactory>().CreateAsync(Partition);
        var ownership = drain.ServiceProvider.GetRequiredService<IRuntimeExecutionOwnershipService>();
        var store = drain.ServiceProvider.GetRequiredService<EfRuntimeCheckpointCommitStore>();

        var lease = await ownership.AcquireAsync(Run);
        Assert.True((await ownership.HeartbeatAsync(lease)).Succeeded);
        Assert.Equal(0, cursors.Count);

        foreach (var commitId in new[] { "commit-1", "commit-2" })
        {
            var before = capture.Commands.Count;
            await store.CommitAsync(Commit(commitId, lease), new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate));
            Assert.Equal((1, 1), OwnershipCommands(capture, before));
        }

        Assert.Equal(0, cursors.Count);
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
