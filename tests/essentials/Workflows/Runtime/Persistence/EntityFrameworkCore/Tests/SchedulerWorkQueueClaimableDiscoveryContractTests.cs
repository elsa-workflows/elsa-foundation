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
using static Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.SchedulerWorkItems;
using static Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.SchedulerWorkQueueClaimableDiscoveryContract;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// Runs <see cref="SchedulerWorkQueueClaimableDiscoveryContract"/> against the in-memory queue and the EF Core store on
/// SQLite, and the #2188 starvation scenarios through a real resumption sweep over each. The native-provider smoke tests
/// run the same contract on PostgreSQL, SQL Server, and MySQL.
/// </summary>
public sealed class SchedulerWorkQueueClaimableDiscoveryContractTests
{
    private const string InMemory = "in-memory";
    private const string EntityFramework = "entity-framework";
    private const int OlderExecutions = 120;
    private const string NewerExecution = "wfexec-9999";

    private readonly RuntimeResumptionOptions _defaults = new();
    private readonly InMemoryWorkflowHoldStateStore _holds = new();
    private readonly RecordingAgentProvider _agents = new();

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
        for (var index = 0; index < OlderExecutions; index++)
        {
            if (index % 2 == 0)
                await EnqueueClaimedAsync(backend.Queue, OlderExecution(index), Now);
            else
                await EnqueueReleasedAsync(backend.Queue, OlderExecution(index), visibleAt: Now.AddMinutes(1));
        }

        await AssertOneSweepRedrivesOnlyTheNewerExecutionAsync(backend.Queue);
    }

    /// <summary>
    /// The issue's first step: more than a page of older executions are paused by a hold. Their heads stay claimable,
    /// because the drainer releases a paused head at once, so only the pause gate tells them apart. One sweep must still
    /// re-drive the newer execution, and none of the paused ones.
    /// </summary>
    [Theory]
    [MemberData(nameof(Stores))]
    public async Task One_sweep_redrives_a_newer_claimable_execution_behind_a_page_of_paused_older_work(string store)
    {
        await using var backend = await QueueBackend.CreateAsync(store);
        for (var index = 0; index < OlderExecutions; index++)
        {
            var workflowExecutionId = OlderExecution(index);
            await _holds.SaveAsync(new WorkflowHoldState(
                controlPlaneStateId: $"control-{workflowExecutionId}",
                workflowExecutionId: workflowExecutionId,
                activeHolds: [WorkflowHold.ForWorkflowExecution($"pause-{workflowExecutionId}", workflowExecutionId, Now, "operator", "Paused for maintenance.")]));
            await backend.Queue.EnqueueAsync(Work(workflowExecutionId, "work-1", 1, WorkflowExecutionCommandKind.StartActivity));
        }

        await AssertOneSweepRedrivesOnlyTheNewerExecutionAsync(backend.Queue);
    }

    [Fact]
    public async Task EF_discovery_is_scoped()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var tenantA = await QueueBackend.CreateEntityFrameworkAsync(connection, "tenant-a");
        await using var tenantB = await QueueBackend.CreateEntityFrameworkAsync(connection, "tenant-b");
        await tenantA.Queue.EnqueueAsync(Work("wf-a", "work-1", 1));
        await tenantB.Queue.EnqueueAsync(Work("wf-b", "work-1", 1));

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

    private async Task AssertOneSweepRedrivesOnlyTheNewerExecutionAsync(IWorkflowSchedulerWorkQueue queue)
    {
        await queue.EnqueueAsync(Work(NewerExecution, "work-1", 1, WorkflowExecutionCommandKind.StartActivity));
        var service = new RuntimeResumptionService(
            new NoOutboxProcessor(),
            queue,
            new InMemoryRuntimeRecoveryScanner(new InMemoryExecutionLivenessStateStore()),
            _agents,
            new ShortRuntimeExecutionIdGenerator(),
            new FixedTimeProvider(Now),
            new InMemoryWorkflowExecutionStateStore(),
            pauseGate: new WorkflowSchedulerPauseGate(new RuntimePauseDecisionProvider(_holds), new FixedTimeProvider(Now)));

        var result = await service.SweepAsync(new RuntimeResumptionSweepRequest(
            outboxBatchSize: _defaults.OutboxBatchSize,
            backlogBatchSize: _defaults.BacklogBatchSize,
            recoveryScanBatchSize: _defaults.RecoveryScanBatchSize,
            leaseTimeout: _defaults.LeaseTimeout,
            heartbeatTimeout: _defaults.HeartbeatTimeout,
            maxExecutionsPerSweep: _defaults.MaxExecutionsPerSweep));

        Assert.Equal(NewerExecution, Assert.Single(result.Dispatches).WorkflowExecutionId);
        Assert.Equal([NewerExecution], _agents.Redriven);
    }

    private static string OlderExecution(int index) => $"wfexec-{index:D4}";

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
}
