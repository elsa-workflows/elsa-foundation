using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Resumption.Options;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Recovery;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;
using static Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.SchedulerWorkQueueClaimableDiscoveryContract;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// Runs <see cref="SchedulerWorkQueueClaimableDiscoveryContract"/> against the in-memory queue and the EF Core store on
/// SQLite, and the #2188 starvation scenario through a real resumption sweep over each. The native-provider smoke tests
/// run the same contract on PostgreSQL, SQL Server, and MySQL.
/// </summary>
public sealed class SchedulerWorkQueueClaimableDiscoveryContractTests
{
    private const string InMemory = "in-memory";
    private const string EntityFramework = "entity-framework";

    public static TheoryData<string> Stores => new(InMemory, EntityFramework);

    [Theory]
    [MemberData(nameof(Stores))]
    public async Task Discovery_lists_exactly_the_executions_a_claim_would_serve(string store)
    {
        await using var backend = await QueueBackend.CreateAsync(store);
        await ListsExactlyTheExecutionsAClaimWouldServeAsync(backend.Queue);
    }

    [Theory]
    [MemberData(nameof(Stores))]
    public async Task Discovery_pages_in_ordinal_order_after_the_bound(string store)
    {
        await using var backend = await QueueBackend.CreateAsync(store);
        await PagesInOrdinalOrderAfterTheBoundAsync(backend.Queue);
    }

    /// <summary>
    /// The #2188 scenario at the pump's default bounds: more than a page of older executions whose queued work is hidden
    /// by a live claim or a backoff, and one newer execution whose work is claimable. Before the fix the older ones
    /// filled every backlog slot and the newer one was never re-driven. One sweep must re-drive it, and only it.
    /// </summary>
    [Theory]
    [MemberData(nameof(Stores))]
    public async Task One_sweep_redrives_a_newer_claimable_execution_behind_a_page_of_hidden_older_work(string store)
    {
        await using var backend = await QueueBackend.CreateAsync(store);
        var defaults = new RuntimeResumptionOptions();
        for (var index = 0; index < defaults.BacklogBatchSize + 20; index++)
        {
            var workflowExecutionId = $"wfexec-{index:D4}";
            if (index % 2 == 0)
                await EnqueueClaimedAsync(backend.Queue, workflowExecutionId, Now);
            else
                await EnqueueReleasedAsync(backend.Queue, workflowExecutionId, visibleAt: Now.AddMinutes(1));
        }
        await backend.Queue.EnqueueAsync(Work("wfexec-9999", 1));
        var agents = new RecordingAgentProvider();
        var service = new RuntimeResumptionService(
            new NoOutboxProcessor(),
            backend.Queue,
            new InMemoryRuntimeRecoveryScanner(new InMemoryExecutionLivenessStateStore()),
            agents,
            new ShortRuntimeExecutionIdGenerator(),
            new FixedTimeProvider(Now),
            new InMemoryWorkflowExecutionStateStore());

        var result = await service.SweepAsync(new RuntimeResumptionSweepRequest(
            outboxBatchSize: defaults.OutboxBatchSize,
            backlogBatchSize: defaults.BacklogBatchSize,
            recoveryScanBatchSize: defaults.RecoveryScanBatchSize,
            leaseTimeout: defaults.LeaseTimeout,
            heartbeatTimeout: defaults.HeartbeatTimeout,
            maxExecutionsPerSweep: defaults.MaxExecutionsPerSweep));

        Assert.Equal("wfexec-9999", Assert.Single(result.Dispatches).WorkflowExecutionId);
        Assert.Equal(["wfexec-9999"], agents.Redriven);
    }

    [Fact]
    public async Task EF_discovery_is_scoped()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var tenantA = await QueueBackend.CreateEntityFrameworkAsync(connection, "tenant-a");
        await using var tenantB = await QueueBackend.CreateEntityFrameworkAsync(connection, "tenant-b");
        await tenantA.Queue.EnqueueAsync(Work("wf-a", 1));
        await tenantB.Queue.EnqueueAsync(Work("wf-b", 1));

        Assert.Equal(["wf-a"], await tenantA.Queue.ListClaimableWorkflowExecutionIdsAsync(new RuntimeSchedulerClaimableBacklogQuery(Now)));
        Assert.Equal(["wf-b"], await tenantB.Queue.ListClaimableWorkflowExecutionIdsAsync(new RuntimeSchedulerClaimableBacklogQuery(Now)));
    }

    [Fact]
    public void Query_rejects_an_invalid_limit_or_bound()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RuntimeSchedulerClaimableBacklogQuery(Now, limit: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RuntimeSchedulerClaimableBacklogQuery(Now, limit: RuntimeStorePageRequest.MaximumLimit + 1));
        Assert.Throws<ArgumentException>(() => new RuntimeSchedulerClaimableBacklogQuery(Now, afterWorkflowExecutionId: " "));
    }

    private sealed class QueueBackend(IWorkflowSchedulerWorkQueue queue, params IAsyncDisposable[] resources) : IAsyncDisposable
    {
        private static readonly HmacRuntimeRecoveryContinuationCodec Codec =
            new(Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = new string('k', 32) }));

        public IWorkflowSchedulerWorkQueue Queue { get; } = queue;

        public static async Task<QueueBackend> CreateAsync(string store)
        {
            if (store == InMemory)
                return new QueueBackend(new InMemoryWorkflowSchedulerWorkQueue());

            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            return await CreateEntityFrameworkAsync(connection, "tenant-a", connection);
        }

        public static async Task<QueueBackend> CreateEntityFrameworkAsync(SqliteConnection connection, string scope, params IAsyncDisposable[] owned)
        {
            var context = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
            return new QueueBackend(new EfSchedulerWorkQueueStore(context, new FixedAccessor(scope), Codec), [context, .. owned]);
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var resource in resources)
                await resource.DisposeAsync();
        }
    }

    private sealed class FixedAccessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class NoOutboxProcessor : IRuntimePostCommitOutboxProcessor
    {
        public ValueTask<RuntimePostCommitOutboxProcessResult> ProcessAsync(
            RuntimePostCommitOutboxProcessRequest request,
            CancellationToken cancellationToken = default) => new(new RuntimePostCommitOutboxProcessResult([]));
    }

    private sealed class RecordingAgentProvider : IWorkflowExecutionActorProvider, IWorkflowExecutionActor
    {
        public List<string> Redriven { get; } = [];

        public WorkflowExecutionActorCapabilities Capabilities => WorkflowExecutionActorCapabilities.InProcessMailbox;

        public WorkflowExecutionActorDescriptor Descriptor { get; } = new(
            workflowExecutionId: "wfexec-agent",
            agentId: "agent-1",
            providerName: "test",
            status: WorkflowExecutionActorStatus.Active,
            capabilities: WorkflowExecutionActorCapabilities.InProcessMailbox,
            activatedAt: Now);

        public ValueTask<IWorkflowExecutionActor> GetAgentAsync(WorkflowExecutionActorActivationRequest request, CancellationToken cancellationToken = default) =>
            new(this);

        public ValueTask PassivateAsync(WorkflowExecutionActorPassivationRequest request, CancellationToken cancellationToken = default) => default;

        public ValueTask<WorkflowExecutionCommandDispatchResult> EnqueueAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default)
        {
            Redriven.Add(envelope.WorkflowExecutionId);
            return new(new WorkflowExecutionCommandDispatchResult(
                envelope.EnvelopeId,
                envelope.WorkflowExecutionId,
                WorkflowExecutionCommandDispatchStatus.Accepted,
                Now));
        }
    }
}
