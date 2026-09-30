using System.Data.Common;
using System.Threading.Channels;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class WorkflowDrainHeartbeatContextIsolationTests
{
    private static readonly DateTimeOffset StartTime = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
    private const string WorkflowExecutionId = "heartbeat-context-isolation-execution";
    private const string Partition = "heartbeat-context-isolation-tenant";

    [Fact(Timeout = 120_000)]
    public async Task Ownership_heartbeat_uses_a_distinct_context_while_the_foreground_query_is_blocked()
    {
        var clock = new ManualTimerTimeProvider(StartTime);
        var commandGate = new ForegroundQueryGate();
        var heartbeatProbe = new HeartbeatReadProbe();
        await using var database = await SharedMemoryDatabase.CreateAsync();
        await using var provider = database.BuildProvider(clock, commandGate, heartbeatProbe);
        await database.CreateSchemaAsync(provider);

        var partition = new PersistenceScope(Partition);
        await using var operation = await provider.GetRequiredService<IPersistenceOperationScopeFactory>().CreateAsync(partition);
        var access = operation.ServiceProvider.GetRequiredService<IPersistenceAccessContextAccessor>().Current;
        Assert.Equal(Partition, access.RequireScope().Value);

        var outerContext = operation.ServiceProvider.GetRequiredService<RuntimeDbContext>();
        var envelope = NewEnvelope();
        var drain = operation.ServiceProvider.GetRequiredService<IWorkflowDrainOrchestrator>()
            .DrainAsync(envelope, new RuntimeSchedulerDrainRequest(WorkflowExecutionId)).AsTask();

        try
        {
            await commandGate.ForegroundQueryEntered.WaitAsync(TimeSpan.FromSeconds(30));
            var cadenceTimer = await clock.NextTimerAsync().WaitAsync(TimeSpan.FromSeconds(30));
            clock.Advance(TimeSpan.FromSeconds(20));
            cadenceTimer.Fire();

            var outcome = await Task.WhenAny(heartbeatProbe.ReadCompleted, drain)
                .WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Same(heartbeatProbe.ReadCompleted, outcome);

            var heartbeatRead = await heartbeatProbe.ReadCompleted;
            Assert.Null(heartbeatRead.Error);
            Assert.NotEqual(outerContext.ContextId.InstanceId, heartbeatRead.Context.ContextId.InstanceId);

            // The next renewal timer is created only after HeartbeatAsync returned successfully.
            _ = await clock.NextTimerAsync().WaitAsync(TimeSpan.FromSeconds(30));
            commandGate.ReleaseForegroundQuery();
            var result = await drain;
            Assert.Equal(RuntimeSchedulerDrainStopReason.Quiesced, result.StopReason);
        }
        finally
        {
            commandGate.ReleaseForegroundQuery();
            try
            {
                await drain.WaitAsync(TimeSpan.FromSeconds(30));
            }
            catch (Exception)
            {
                // Observe the pre-fix ownership-loss failure while allowing the original assertion to report it.
            }
        }
    }

    private static WorkflowExecutionCommandEnvelope NewEnvelope()
    {
        var now = StartTime;
        var command = new WorkflowExecutionCommand(
            "heartbeat-command",
            WorkflowExecutionId,
            WorkflowExecutionCommandKind.RunSchedulerWork,
            now,
            Payload: null,
            Metadata: new Dictionary<string, string>());
        return new WorkflowExecutionCommandEnvelope(
            "heartbeat-envelope",
            WorkflowExecutionId,
            command,
            "heartbeat-command",
            WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            now,
            partition: new WorkflowExecutionPartition(Partition));
    }

    private sealed class SharedMemoryDatabase(SqliteConnection keeper) : IAsyncDisposable
    {
        public static async Task<SharedMemoryDatabase> CreateAsync()
        {
            var keeper = new SqliteConnection(
                $"Data Source=file:runtime-heartbeat-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Pooling=False");
            await keeper.OpenAsync();
            return new SharedMemoryDatabase(keeper);
        }

        public ServiceProvider BuildProvider(
            ManualTimerTimeProvider clock,
            ForegroundQueryGate commandGate,
            HeartbeatReadProbe heartbeatProbe)
        {
            const string connectionName = "RuntimeHeartbeat";
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"ConnectionStrings:{connectionName}"] = keeper.ConnectionString
                })
                .Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddWorkflowRuntime();
            services.AddRuntimeEntityFrameworkCore(new RuntimeEntityFrameworkCoreOptions
            {
                Provider = "Sqlite",
                ConnectionName = connectionName,
                ConnectionString = null,
                RecoveryContinuationSigningKey = "runtime-heartbeat-context-test-recovery-key-32-bytes",
                HierarchyCursorSigningKey = "runtime-heartbeat-context-test-hierarchy-key-32-bytes"
            });
            services.ConfigureDbContext<RuntimeSqliteDbContext>(options => options.AddInterceptors(commandGate));
            services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock));
            services.Replace(ServiceDescriptor.Scoped<IWorkflowSchedulerDrainer>(serviceProvider =>
                new BlockingQuerySchedulerDrainer(
                    serviceProvider.GetRequiredService<RuntimeDbContext>(),
                    commandGate,
                    heartbeatProbe)));
            services.RemoveAll<IExecutionLivenessStateStore>();
            services.AddScoped<IExecutionLivenessStateStore>(serviceProvider =>
                new ObservedExecutionLivenessStateStore(
                    serviceProvider.GetRequiredService<EfExecutionLivenessStateStore>(),
                    serviceProvider.GetRequiredService<RuntimeDbContext>(),
                    heartbeatProbe));
            return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        }

        public async Task CreateSchemaAsync(IServiceProvider provider)
        {
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<RuntimeDbContext>().Database.EnsureCreatedAsync();
        }

        public ValueTask DisposeAsync() => keeper.DisposeAsync();
    }

    private sealed class BlockingQuerySchedulerDrainer(
        RuntimeDbContext context,
        ForegroundQueryGate commandGate,
        HeartbeatReadProbe heartbeatProbe) : IWorkflowSchedulerDrainer
    {
        public async ValueTask<RuntimeSchedulerDrainResult> DrainAsync(
            RuntimeSchedulerDrainRequest request,
            CancellationToken cancellationToken = default)
        {
            heartbeatProbe.Arm(request.WorkflowExecutionId);
            commandGate.ArmForegroundQuery(context);
            _ = await context.ExecutionLivenessStates.AsNoTracking()
                .AnyAsync(row => row.WorkflowExecutionId == "foreground-query-probe", cancellationToken);
            return new RuntimeSchedulerDrainResult(
                request.WorkflowExecutionId,
                StartTime,
                StartTime,
                []);
        }
    }

    private sealed class ObservedExecutionLivenessStateStore(
        IExecutionLivenessStateStore inner,
        RuntimeDbContext context,
        HeartbeatReadProbe probe) : IExecutionLivenessStateStore
    {
        public ValueTask<ExecutionLivenessState> SaveAsync(
            ExecutionLivenessState state,
            CancellationToken cancellationToken = default) => inner.SaveAsync(state, cancellationToken);

        public ValueTask<ExecutionLivenessStateWriteResult> TrySaveAsync(
            ExecutionLivenessState state,
            long expectedRevision,
            CancellationToken cancellationToken = default) => inner.TrySaveAsync(state, expectedRevision, cancellationToken);

        public ValueTask<ExecutionLivenessState?> FindAsync(
            string workflowExecutionId,
            string operationalStateId,
            CancellationToken cancellationToken = default) => inner.FindAsync(workflowExecutionId, operationalStateId, cancellationToken);

        public async ValueTask<VersionedExecutionLivenessState?> FindVersionedAsync(
            string workflowExecutionId,
            string operationalStateId,
            CancellationToken cancellationToken = default)
        {
            if (!probe.TryObserveRead(workflowExecutionId))
                return await inner.FindVersionedAsync(workflowExecutionId, operationalStateId, cancellationToken);

            try
            {
                var result = await inner.FindVersionedAsync(workflowExecutionId, operationalStateId, cancellationToken);
                probe.CompleteRead(context, error: null);
                return result;
            }
            catch (Exception exception)
            {
                probe.CompleteRead(context, exception);
                throw;
            }
        }
    }

    private sealed class HeartbeatReadProbe
    {
        private string? _workflowExecutionId;
        private int _readObserved;
        private readonly TaskCompletionSource<HeartbeatRead> _readCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<HeartbeatRead> ReadCompleted => _readCompleted.Task;

        public void Arm(string workflowExecutionId) => _workflowExecutionId = workflowExecutionId;

        public bool TryObserveRead(string workflowExecutionId) =>
            StringComparer.Ordinal.Equals(_workflowExecutionId, workflowExecutionId) &&
            Interlocked.Exchange(ref _readObserved, 1) == 0;

        public void CompleteRead(RuntimeDbContext context, Exception? error) =>
            _readCompleted.TrySetResult(new HeartbeatRead(context, error));
    }

    private sealed record HeartbeatRead(RuntimeDbContext Context, Exception? Error);

    private sealed class ForegroundQueryGate : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _foregroundQueryEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseForeground = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private RuntimeDbContext? _foregroundContext;
        private int _blocked;

        public Task ForegroundQueryEntered => _foregroundQueryEntered.Task;

        public void ArmForegroundQuery(RuntimeDbContext context) => _foregroundContext = context;

        public void ReleaseForegroundQuery() => _releaseForeground.TrySetResult();

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (ReferenceEquals(eventData.Context, _foregroundContext) && Interlocked.Exchange(ref _blocked, 1) == 0)
            {
                _foregroundQueryEntered.TrySetResult();
                await _releaseForeground.Task.WaitAsync(cancellationToken);
            }

            return result;
        }
    }

    private sealed class ManualTimerTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private readonly Channel<ManualTimer> _createdTimers = Channel.CreateUnbounded<ManualTimer>();
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(callback, state);
            _createdTimers.Writer.TryWrite(timer);
            return timer;
        }

        public Task<ManualTimer> NextTimerAsync() => _createdTimers.Reader.ReadAsync().AsTask();

        public void Advance(TimeSpan delta) => _now = _now.Add(delta);

        public sealed class ManualTimer(TimerCallback callback, object? state) : ITimer
        {
            private int _disposed;

            public bool Change(TimeSpan dueTime, TimeSpan period) => Volatile.Read(ref _disposed) == 0;

            public void Fire()
            {
                if (Volatile.Read(ref _disposed) == 0)
                    callback(state);
            }

            public void Dispose() => Interlocked.Exchange(ref _disposed, 1);

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
