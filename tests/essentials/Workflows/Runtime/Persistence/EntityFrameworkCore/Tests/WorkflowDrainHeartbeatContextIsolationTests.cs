using System.Data.Common;
using System.Threading.Channels;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Incidents;
using Elsa.Workflows.Runtime.Services.Scheduler;
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

        var operationScopeFactory = provider.GetRequiredService<IPersistenceOperationScopeFactory>();
        var neighborPartition = new PersistenceScope("heartbeat-context-isolation-neighbor");
        RuntimeExecutionLease neighborLease;
        VersionedExecutionLivenessState neighborBefore;
        await using (var neighborScope = await operationScopeFactory.CreateAsync(neighborPartition))
        {
            neighborLease = await neighborScope.ServiceProvider.GetRequiredService<IRuntimeExecutionOwnershipService>()
                .AcquireAsync(WorkflowExecutionId);
            neighborBefore = Assert.IsType<VersionedExecutionLivenessState>(await neighborScope.ServiceProvider
                .GetRequiredService<IExecutionLivenessStateStore>()
                .FindVersionedAsync(WorkflowExecutionId, RuntimeExecutionOwnershipStateId.For(WorkflowExecutionId)));
        }

        var partition = new PersistenceScope(Partition);
        await using var operation = await operationScopeFactory.CreateAsync(partition);
        var access = operation.ServiceProvider.GetRequiredService<IPersistenceAccessContextAccessor>().Current;
        Assert.Equal(Partition, access.RequireScope().Value);

        var legacyConstruction = Assert.Throws<InvalidOperationException>(() => new WorkflowDrainOrchestrator(
            operation.ServiceProvider.GetRequiredService<IWorkflowSchedulerDrainer>(),
            operation.ServiceProvider.GetRequiredService<IRuntimePostCommitOutboxProcessor>(),
            operation.ServiceProvider.GetServices<IWorkflowSchedulerDrainObserver>(),
            operation.ServiceProvider.GetRequiredService<CheckpointRuleViolationWorkflowFaulter>(),
            operation.ServiceProvider.GetRequiredService<IRuntimeExecutionOwnershipService>(),
            operation.ServiceProvider.GetRequiredService<IRuntimeExecutionOwnershipContextAccessor>()));
        Assert.Contains("CreateScoped", legacyConstruction.Message, StringComparison.Ordinal);

        var outerContext = operation.ServiceProvider.GetRequiredService<RuntimeDbContext>();
        var databaseIdentity = outerContext.Database.GetDbConnection().DataSource;
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
            Assert.Equal(1, heartbeatRead.Revision);
            Assert.NotEqual(outerContext.ContextId.InstanceId, heartbeatRead.ContextId);
            Assert.Equal(Partition, heartbeatRead.Partition);
            Assert.Equal(databaseIdentity, heartbeatRead.DataSource);

            var heartbeatWrite = await heartbeatProbe.ReadHeartbeatWriteAsync().WaitAsync(TimeSpan.FromSeconds(30));
            var acquiredLease = await heartbeatProbe.AcquiredLease.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(heartbeatRead.ContextId, heartbeatWrite.ContextId);
            Assert.Equal(ExecutionLivenessStateWriteStatus.Saved, heartbeatWrite.Status);
            Assert.Equal(1, heartbeatWrite.ExpectedRevision);
            Assert.Equal(acquiredLease.LeaseId, heartbeatWrite.State.ExecutionLease!.LeaseId);
            Assert.Equal(acquiredLease.OwnerId, heartbeatWrite.State.ExecutionLease.OwnerId);
            Assert.Equal(acquiredLease.FencingToken, heartbeatWrite.State.ExecutionLease.FencingToken);
            Assert.Equal(StartTime, heartbeatWrite.State.ExecutionLease.AcquiredAt);
            Assert.Equal(neighborLease.FencingToken, heartbeatWrite.State.ExecutionLease.FencingToken);
            Assert.Equal(StartTime.AddMinutes(1), acquiredLease.ExpiresAt);
            Assert.Equal(StartTime.AddMinutes(1).AddSeconds(20), heartbeatWrite.State.ExecutionLease.ExpiresAt);
            Assert.Equal(StartTime.AddSeconds(20), heartbeatWrite.State.Heartbeat!.RecordedAt);
            await heartbeatProbe.ScopeDisposed.WaitAsync(TimeSpan.FromSeconds(30));

            // The next renewal timer is created only after HeartbeatAsync returned successfully.
            _ = await clock.NextTimerAsync().WaitAsync(TimeSpan.FromSeconds(30));
            commandGate.ReleaseForegroundQuery();
            var result = await drain;
            Assert.Equal(RuntimeSchedulerDrainStopReason.Quiesced, result.StopReason);

            var released = Assert.IsType<VersionedExecutionLivenessState>(await operation.ServiceProvider
                .GetRequiredService<IExecutionLivenessStateStore>()
                .FindVersionedAsync(WorkflowExecutionId, RuntimeExecutionOwnershipStateId.For(WorkflowExecutionId)));
            Assert.True(await heartbeatProbe.ReleaseFollowedDisposedHeartbeatScope.WaitAsync(TimeSpan.FromSeconds(30)));
            Assert.False(await heartbeatProbe.ReleaseUsedCancelableToken.WaitAsync(TimeSpan.FromSeconds(30)));
            Assert.Equal(3, released.Revision);
            Assert.Null(released.State.ExecutionLease);
            Assert.Null(released.State.Heartbeat);
            Assert.Equal(
                heartbeatWrite.State.Metadata[RuntimeMetadataKeys.OwnershipFencingToken],
                released.State.Metadata[RuntimeMetadataKeys.OwnershipFencingToken]);

            await using var neighborAfterScope = await operationScopeFactory.CreateAsync(neighborPartition);
            var neighborAfter = Assert.IsType<VersionedExecutionLivenessState>(await neighborAfterScope.ServiceProvider
                .GetRequiredService<IExecutionLivenessStateStore>()
                .FindVersionedAsync(WorkflowExecutionId, RuntimeExecutionOwnershipStateId.For(WorkflowExecutionId)));
            Assert.Equal(neighborBefore.Revision, neighborAfter.Revision);
            Assert.Equal(neighborBefore.State.ExecutionLease!.LeaseId, neighborAfter.State.ExecutionLease!.LeaseId);
            Assert.Equal(neighborBefore.State.ExecutionLease.ExpiresAt, neighborAfter.State.ExecutionLease.ExpiresAt);
            Assert.Equal(neighborBefore.State.Heartbeat!.RecordedAt, neighborAfter.State.Heartbeat!.RecordedAt);
            Assert.Equal(neighborBefore.State.Metadata, neighborAfter.State.Metadata);
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

    [Fact(Timeout = 120_000)]
    public async Task Heartbeat_retries_same_owner_revision_conflict_after_checkpoint_fence_touch()
    {
        var clock = new ManualTimerTimeProvider(StartTime);
        var commandGate = new ForegroundQueryGate();
        var heartbeatProbe = new HeartbeatReadProbe(pauseAfterRead: true, pauseForegroundBeforeQuery: true);
        await using var database = await SharedMemoryDatabase.CreateAsync();
        await using var provider = database.BuildProvider(clock, commandGate, heartbeatProbe, pauseForegroundBeforeQuery: true);
        await database.CreateSchemaAsync(provider);

        await using var operation = await provider.GetRequiredService<IPersistenceOperationScopeFactory>()
            .CreateAsync(new PersistenceScope(Partition));
        var outerContext = operation.ServiceProvider.GetRequiredService<RuntimeDbContext>();
        var drain = operation.ServiceProvider.GetRequiredService<IWorkflowDrainOrchestrator>()
            .DrainAsync(NewEnvelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId)).AsTask();

        try
        {
            await heartbeatProbe.ForegroundDrainWaiting.WaitAsync(TimeSpan.FromSeconds(30));
            var cadenceTimer = await clock.NextTimerAsync().WaitAsync(TimeSpan.FromSeconds(30));
            clock.Advance(TimeSpan.FromSeconds(20));
            cadenceTimer.Fire();

            var read = await heartbeatProbe.ReadCompleted.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Null(read.Error);
            Assert.Equal(1, read.Revision);
            var lease = await heartbeatProbe.AcquiredLease.WaitAsync(TimeSpan.FromSeconds(30));

            var checkpoint = new RuntimeCheckpointCommit(
                "heartbeat-cas-checkpoint-commit",
                new RuntimeCheckpoint(
                    "heartbeat-cas-checkpoint",
                    "HeartbeatCasFenceTouch",
                    WorkflowExecutionId,
                    StartTime.AddSeconds(20),
                    [],
                    new Dictionary<string, string>()),
                new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], []),
                [],
                new Dictionary<string, string>())
            {
                ExpectedFence = lease.ToFence()
            };
            commandGate.ArmCheckpointCommitCapture(outerContext);
            var checkpointStore = operation.ServiceProvider.GetRequiredService<IRuntimeCheckpointCommitStore>();
            Assert.IsType<EfRuntimeCheckpointCommitStore>(checkpointStore);
            await checkpointStore
                .CommitAsync(checkpoint, new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate));
            Assert.Equal(outerContext.ContextId.InstanceId, commandGate.FenceReadContextId);
            Assert.Equal(outerContext.ContextId.InstanceId, commandGate.MarkerInsertContextId);
            Assert.NotNull(commandGate.FenceReadTransactionId);
            Assert.Equal(commandGate.FenceReadTransactionId, commandGate.MarkerInsertTransactionId);
            Assert.Single(await outerContext.RuntimeCheckpointCommits.ToArrayAsync());
            var fenceTouched = Assert.IsType<VersionedExecutionLivenessState>(await operation.ServiceProvider
                .GetRequiredService<IExecutionLivenessStateStore>()
                .FindVersionedAsync(WorkflowExecutionId, RuntimeExecutionOwnershipStateId.For(WorkflowExecutionId)));
            Assert.Equal(2, fenceTouched.Revision);
            Assert.Equal(lease.FencingToken, fenceTouched.State.ExecutionLease!.FencingToken);

            heartbeatProbe.ReleaseHeartbeatRead();
            var conflictedWrite = await heartbeatProbe.ReadHeartbeatWriteAsync().WaitAsync(TimeSpan.FromSeconds(30));
            var convergedWrite = await heartbeatProbe.ReadHeartbeatWriteAsync().WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(1, conflictedWrite.ExpectedRevision);
            Assert.Equal(ExecutionLivenessStateWriteStatus.RevisionConflict, conflictedWrite.Status);
            Assert.Equal(2, convergedWrite.ExpectedRevision);
            Assert.Equal(ExecutionLivenessStateWriteStatus.Saved, convergedWrite.Status);
            Assert.Equal(lease.LeaseId, convergedWrite.State.ExecutionLease!.LeaseId);
            Assert.Equal(lease.OwnerId, convergedWrite.State.ExecutionLease.OwnerId);
            Assert.Equal(lease.FencingToken, convergedWrite.State.ExecutionLease.FencingToken);
            Assert.Equal(StartTime.AddSeconds(20), convergedWrite.State.Heartbeat!.RecordedAt);
            await heartbeatProbe.ScopeDisposed.WaitAsync(TimeSpan.FromSeconds(30));

            heartbeatProbe.ReleaseForegroundDrain();
            var result = await drain.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(RuntimeSchedulerDrainStopReason.Quiesced, result.StopReason);

            Assert.True(await heartbeatProbe.ReleaseFollowedDisposedHeartbeatScope.WaitAsync(TimeSpan.FromSeconds(30)));
            Assert.False(await heartbeatProbe.ReleaseUsedCancelableToken.WaitAsync(TimeSpan.FromSeconds(30)));
            Assert.Equal(4, (await operation.ServiceProvider.GetRequiredService<IExecutionLivenessStateStore>()
                .FindVersionedAsync(WorkflowExecutionId, RuntimeExecutionOwnershipStateId.For(WorkflowExecutionId)))!.Revision);
        }
        finally
        {
            heartbeatProbe.ReleaseHeartbeatRead();
            heartbeatProbe.ReleaseForegroundDrain();
            try
            {
                await drain.WaitAsync(TimeSpan.FromSeconds(30));
            }
            catch (Exception)
            {
                // Observe a failed heartbeat while preserving the assertion that identifies the failure.
            }
        }
    }

    [Theory(Timeout = 120_000)]
    [InlineData(false, RuntimeExecutionOwnershipTransitionStatus.Stale)]
    [InlineData(true, RuntimeExecutionOwnershipTransitionStatus.Expired)]
    public async Task Rejected_scoped_heartbeat_cancels_drain_without_overwriting_owner(
        bool expireLease,
        RuntimeExecutionOwnershipTransitionStatus expectedStatus)
    {
        var clock = new ManualTimerTimeProvider(StartTime);
        var commandGate = new ForegroundQueryGate();
        var heartbeatProbe = new HeartbeatReadProbe();
        await using var database = await SharedMemoryDatabase.CreateAsync();
        await using var provider = database.BuildProvider(clock, commandGate, heartbeatProbe);
        await database.CreateSchemaAsync(provider);

        await using var operation = await provider.GetRequiredService<IPersistenceOperationScopeFactory>()
            .CreateAsync(new PersistenceScope(Partition));
        var drain = operation.ServiceProvider.GetRequiredService<IWorkflowDrainOrchestrator>()
            .DrainAsync(NewEnvelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId)).AsTask();

        try
        {
            await commandGate.ForegroundQueryEntered.WaitAsync(TimeSpan.FromSeconds(30));
            var acquired = await heartbeatProbe.AcquiredLease.WaitAsync(TimeSpan.FromSeconds(30));
            RuntimeExecutionLease? successor = null;
            if (expireLease)
            {
                clock.Advance(TimeSpan.FromMinutes(1).Add(TimeSpan.FromSeconds(1)));
            }
            else
            {
                await using var successorScope = await provider.GetRequiredService<IPersistenceOperationScopeFactory>()
                    .CreateAsync(new PersistenceScope(Partition));
                heartbeatProbe.SuppressReadObservation();
                successor = await successorScope.ServiceProvider.GetRequiredService<IRuntimeExecutionOwnershipService>()
                    .AcquireAsync(WorkflowExecutionId);
                heartbeatProbe.ResumeReadObservation();
                Assert.True(successor.FencingToken > acquired.FencingToken);
            }

            var cadenceTimer = await clock.NextTimerAsync().WaitAsync(TimeSpan.FromSeconds(30));
            if (!expireLease)
                clock.Advance(TimeSpan.FromSeconds(20));
            cadenceTimer.Fire();

            var failure = await Assert.ThrowsAsync<RuntimeExecutionOwnershipLostException>(() => drain.WaitAsync(TimeSpan.FromSeconds(30)));
            Assert.Equal("heartbeat", failure.Operation);
            Assert.Equal(expectedStatus, failure.TransitionStatus);
            await heartbeatProbe.ScopeDisposed.WaitAsync(TimeSpan.FromSeconds(30));
            await commandGate.ForegroundQueryCanceled.WaitAsync(TimeSpan.FromSeconds(30));

            await using var verification = await provider.GetRequiredService<IPersistenceOperationScopeFactory>()
                .CreateAsync(new PersistenceScope(Partition));
            var persisted = Assert.IsType<VersionedExecutionLivenessState>(await verification.ServiceProvider
                .GetRequiredService<IExecutionLivenessStateStore>()
                .FindVersionedAsync(WorkflowExecutionId, RuntimeExecutionOwnershipStateId.For(WorkflowExecutionId)));
            if (successor is not null)
            {
                Assert.Equal(successor.LeaseId, persisted.State.ExecutionLease!.LeaseId);
                Assert.Equal(successor.FencingToken, persisted.State.ExecutionLease.FencingToken);
                Assert.Equal(successor.ExpiresAt, persisted.State.ExecutionLease.ExpiresAt);
                Assert.Equal(successor.LeaseId, persisted.State.Heartbeat!.LeaseId);
            }
            else
            {
                Assert.Equal(acquired.LeaseId, persisted.State.ExecutionLease!.LeaseId);
                Assert.Equal(acquired.FencingToken, persisted.State.ExecutionLease.FencingToken);
                Assert.Equal(acquired.ExpiresAt, persisted.State.ExecutionLease.ExpiresAt);
                Assert.Equal(StartTime, persisted.State.Heartbeat!.RecordedAt);
            }
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
                // The expected ownership-lost exception is asserted above.
            }
        }
    }

    [Fact(Timeout = 120_000)]
    public async Task Caller_cancellation_disposes_inflight_heartbeat_scope_before_noncancelable_release()
    {
        var clock = new ManualTimerTimeProvider(StartTime);
        var commandGate = new ForegroundQueryGate();
        var heartbeatProbe = new HeartbeatReadProbe(pauseAfterRead: true, pauseForegroundBeforeQuery: true);
        await using var database = await SharedMemoryDatabase.CreateAsync();
        await using var provider = database.BuildProvider(clock, commandGate, heartbeatProbe, pauseForegroundBeforeQuery: true);
        await database.CreateSchemaAsync(provider);

        await using var operation = await provider.GetRequiredService<IPersistenceOperationScopeFactory>()
            .CreateAsync(new PersistenceScope(Partition));
        using var cancellation = new CancellationTokenSource();
        var drain = operation.ServiceProvider.GetRequiredService<IWorkflowDrainOrchestrator>()
            .DrainAsync(NewEnvelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId), cancellation.Token).AsTask();

        try
        {
            await heartbeatProbe.ForegroundDrainWaiting.WaitAsync(TimeSpan.FromSeconds(30));
            var cadenceTimer = await clock.NextTimerAsync().WaitAsync(TimeSpan.FromSeconds(30));
            clock.Advance(TimeSpan.FromSeconds(20));
            cadenceTimer.Fire();
            var heartbeatRead = await heartbeatProbe.ReadCompleted.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Null(heartbeatRead.Error);

            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => drain.WaitAsync(TimeSpan.FromSeconds(30)));
            await heartbeatProbe.ScopeDisposed.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.True(await heartbeatProbe.ReleaseFollowedDisposedHeartbeatScope.WaitAsync(TimeSpan.FromSeconds(30)));
            Assert.False(await heartbeatProbe.ReleaseUsedCancelableToken.WaitAsync(TimeSpan.FromSeconds(30)));

            var final = Assert.IsType<VersionedExecutionLivenessState>(await operation.ServiceProvider
                .GetRequiredService<IExecutionLivenessStateStore>()
                .FindVersionedAsync(WorkflowExecutionId, RuntimeExecutionOwnershipStateId.For(WorkflowExecutionId)));
            Assert.Equal(2, final.Revision);
            Assert.Null(final.State.ExecutionLease);
            Assert.Null(final.State.Heartbeat);
            Assert.Equal("1", final.State.Metadata[RuntimeMetadataKeys.OwnershipFencingToken]);
        }
        finally
        {
            heartbeatProbe.ReleaseHeartbeatRead();
            heartbeatProbe.ReleaseForegroundDrain();
            commandGate.ReleaseForegroundQuery();
            try
            {
                await drain.WaitAsync(TimeSpan.FromSeconds(30));
            }
            catch (OperationCanceledException)
            {
                // Cancellation is the expected drain outcome.
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

        public ServiceProvider BuildProvider(
            ManualTimerTimeProvider clock,
            ForegroundQueryGate commandGate,
            HeartbeatReadProbe heartbeatProbe,
            bool pauseForegroundBeforeQuery = false)
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
                    heartbeatProbe,
                    pauseForegroundBeforeQuery)));
            services.RemoveAll<IExecutionLivenessStateStore>();
            services.AddScoped<IExecutionLivenessStateStore>(serviceProvider =>
                new ObservedExecutionLivenessStateStore(
                    serviceProvider.GetRequiredService<EfExecutionLivenessStateStore>(),
                    serviceProvider.GetRequiredService<RuntimeDbContext>(),
                    serviceProvider.GetRequiredService<IPersistenceAccessContextAccessor>(),
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
        HeartbeatReadProbe heartbeatProbe,
        bool pauseForegroundBeforeQuery) : IWorkflowSchedulerDrainer
    {
        public async ValueTask<RuntimeSchedulerDrainResult> DrainAsync(
            RuntimeSchedulerDrainRequest request,
            CancellationToken cancellationToken = default)
        {
            heartbeatProbe.Arm(request.WorkflowExecutionId);
            if (pauseForegroundBeforeQuery)
                await heartbeatProbe.WaitBeforeForegroundQueryAsync(cancellationToken);
            else
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
        IPersistenceAccessContextAccessor accessContextAccessor,
        HeartbeatReadProbe probe) : IExecutionLivenessStateStore, IAsyncDisposable
    {
        public ValueTask<ExecutionLivenessState> SaveAsync(
            ExecutionLivenessState state,
            CancellationToken cancellationToken = default) => inner.SaveAsync(state, cancellationToken);

        public async ValueTask<ExecutionLivenessStateWriteResult> TrySaveAsync(
            ExecutionLivenessState state,
            long expectedRevision,
            CancellationToken cancellationToken = default)
        {
            var partition = accessContextAccessor.Current.RequireScope().Value;
            probe.ObserveLease(state.ExecutionLease, partition);
            var result = await inner.TrySaveAsync(state, expectedRevision, cancellationToken);
            probe.RecordWrite(context.ContextId.InstanceId, partition, state, expectedRevision, result, cancellationToken.CanBeCanceled);
            return result;
        }

        public ValueTask<ExecutionLivenessState?> FindAsync(
            string workflowExecutionId,
            string operationalStateId,
            CancellationToken cancellationToken = default) => inner.FindAsync(workflowExecutionId, operationalStateId, cancellationToken);

        public async ValueTask<VersionedExecutionLivenessState?> FindVersionedAsync(
            string workflowExecutionId,
            string operationalStateId,
            CancellationToken cancellationToken = default)
        {
            if (!probe.TryObserveRead(
                    workflowExecutionId,
                    context.ContextId.InstanceId,
                    accessContextAccessor.Current.RequireScope().Value,
                    context.Database.GetDbConnection().DataSource))
                return await inner.FindVersionedAsync(workflowExecutionId, operationalStateId, cancellationToken);

            try
            {
                var result = await inner.FindVersionedAsync(workflowExecutionId, operationalStateId, cancellationToken);
                probe.CompleteRead(result, error: null);
                await probe.WaitAfterHeartbeatReadAsync(cancellationToken);
                return result;
            }
            catch (Exception exception)
            {
                probe.CompleteRead(null, exception);
                throw;
            }
        }

        public ValueTask DisposeAsync()
        {
            probe.RecordScopeDisposed(context.ContextId.InstanceId);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class HeartbeatReadProbe
    {
        private readonly bool _pauseAfterRead;
        private readonly bool _pauseForegroundBeforeQuery;
        private string? _workflowExecutionId;
        private int _readObserved;
        private int _readObservationSuppressed;
        private Guid? _observedContextId;
        private HeartbeatRead? _readDetails;
        private readonly TaskCompletionSource<HeartbeatRead> _readCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Channel<HeartbeatWrite> _heartbeatWrites = Channel.CreateUnbounded<HeartbeatWrite>();
        private readonly TaskCompletionSource<RuntimeExecutionLease> _acquiredLease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _scopeDisposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseHeartbeatRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _foregroundDrainWaiting = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseForegroundDrain = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _releaseFollowedDisposedHeartbeatScope = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _releaseUsedCancelableToken = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public HeartbeatReadProbe(bool pauseAfterRead = false, bool pauseForegroundBeforeQuery = false)
        {
            _pauseAfterRead = pauseAfterRead;
            _pauseForegroundBeforeQuery = pauseForegroundBeforeQuery;
        }

        public Task<HeartbeatRead> ReadCompleted => _readCompleted.Task;
        public Task<RuntimeExecutionLease> AcquiredLease => _acquiredLease.Task;
        public Task ScopeDisposed => _scopeDisposed.Task;
        public Task ForegroundDrainWaiting => _foregroundDrainWaiting.Task;
        public Task<bool> ReleaseFollowedDisposedHeartbeatScope => _releaseFollowedDisposedHeartbeatScope.Task;
        public Task<bool> ReleaseUsedCancelableToken => _releaseUsedCancelableToken.Task;

        public Task<HeartbeatWrite> ReadHeartbeatWriteAsync() => _heartbeatWrites.Reader.ReadAsync().AsTask();

        public void ReleaseHeartbeatRead() => _releaseHeartbeatRead.TrySetResult();

        public void ReleaseForegroundDrain() => _releaseForegroundDrain.TrySetResult();

        public void SuppressReadObservation() => Interlocked.Exchange(ref _readObservationSuppressed, 1);

        public void ResumeReadObservation() => Interlocked.Exchange(ref _readObservationSuppressed, 0);

        public async Task WaitBeforeForegroundQueryAsync(CancellationToken cancellationToken)
        {
            if (!_pauseForegroundBeforeQuery)
                return;

            _foregroundDrainWaiting.TrySetResult();
            await _releaseForegroundDrain.Task.WaitAsync(cancellationToken);
        }

        public async Task WaitAfterHeartbeatReadAsync(CancellationToken cancellationToken)
        {
            if (_pauseAfterRead)
                await _releaseHeartbeatRead.Task.WaitAsync(cancellationToken);
        }

        public void Arm(string workflowExecutionId) => _workflowExecutionId = workflowExecutionId;

        public bool TryObserveRead(string workflowExecutionId, Guid contextId, string partition, string dataSource)
        {
            if (Volatile.Read(ref _readObservationSuppressed) != 0 ||
                !StringComparer.Ordinal.Equals(_workflowExecutionId, workflowExecutionId) ||
                Interlocked.Exchange(ref _readObserved, 1) != 0)
                return false;

            _observedContextId = contextId;
            _readDetails = new HeartbeatRead(
                contextId,
                partition,
                dataSource,
                Revision: null,
                Error: null);
            return true;
        }

        public void CompleteRead(VersionedExecutionLivenessState? state, Exception? error)
        {
            if (_readDetails is { } details)
                _readCompleted.TrySetResult(details with { Revision = state?.Revision, Error = error });
        }

        public void ObserveLease(RuntimeExecutionLease? lease, string partition)
        {
            if (lease is not null && StringComparer.Ordinal.Equals(partition, Partition) && _observedContextId is null)
                _acquiredLease.TrySetResult(lease);
        }

        public void RecordWrite(
            Guid contextId,
            string partition,
            ExecutionLivenessState state,
            long expectedRevision,
            ExecutionLivenessStateWriteResult result,
            bool cancellationTokenCanBeCanceled)
        {
            if (_observedContextId == contextId)
            {
                _heartbeatWrites.Writer.TryWrite(new HeartbeatWrite(contextId, state, expectedRevision, result.Status));
            }
            else if (StringComparer.Ordinal.Equals(partition, Partition) && state.ExecutionLease is null && state.Heartbeat is null)
            {
                _releaseFollowedDisposedHeartbeatScope.TrySetResult(_scopeDisposed.Task.IsCompletedSuccessfully);
                _releaseUsedCancelableToken.TrySetResult(cancellationTokenCanBeCanceled);
            }
        }

        public void RecordScopeDisposed(Guid contextId)
        {
            if (_observedContextId == contextId)
                _scopeDisposed.TrySetResult();
        }
    }

    private sealed record HeartbeatRead(Guid ContextId, string Partition, string DataSource, long? Revision, Exception? Error);
    private sealed record HeartbeatWrite(Guid ContextId, ExecutionLivenessState State, long ExpectedRevision, ExecutionLivenessStateWriteStatus Status);

    private sealed class ForegroundQueryGate : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _foregroundQueryEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseForeground = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _foregroundQueryCanceled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private RuntimeDbContext? _foregroundContext;
        private RuntimeDbContext? _checkpointContext;
        private int _blocked;

        public Task ForegroundQueryEntered => _foregroundQueryEntered.Task;
        public Task ForegroundQueryCanceled => _foregroundQueryCanceled.Task;
        public Guid? FenceReadContextId { get; private set; }
        public Guid? FenceReadTransactionId { get; private set; }
        public Guid? MarkerInsertContextId { get; private set; }
        public Guid? MarkerInsertTransactionId { get; private set; }

        public void ArmForegroundQuery(RuntimeDbContext context) => _foregroundContext = context;

        public void ArmCheckpointCommitCapture(RuntimeDbContext context) => _checkpointContext = context;

        public void ReleaseForegroundQuery() => _releaseForeground.TrySetResult();

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            CaptureCheckpointCommand(command, eventData);
            if (ReferenceEquals(eventData.Context, _foregroundContext) && Interlocked.Exchange(ref _blocked, 1) == 0)
            {
                _foregroundQueryEntered.TrySetResult();
                try
                {
                    await _releaseForeground.Task.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    _foregroundQueryCanceled.TrySetResult();
                    throw;
                }
            }

            return result;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            CaptureCheckpointCommand(command, eventData);
            return ValueTask.FromResult(result);
        }

        private void CaptureCheckpointCommand(DbCommand command, CommandEventData eventData)
        {
            if (!ReferenceEquals(eventData.Context, _checkpointContext) || eventData.Context is not RuntimeDbContext context)
                return;

            if (FenceReadContextId is null && command.CommandText.Contains("elsa_runtime_execution_liveness_state", StringComparison.OrdinalIgnoreCase))
            {
                FenceReadContextId = context.ContextId.InstanceId;
                FenceReadTransactionId = context.Database.CurrentTransaction?.TransactionId;
            }

            if (MarkerInsertContextId is null &&
                command.CommandText.TrimStart().StartsWith("INSERT", StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.Contains("elsa_runtime_checkpoint_commit", StringComparison.OrdinalIgnoreCase))
            {
                MarkerInsertContextId = context.ContextId.InstanceId;
                MarkerInsertTransactionId = context.Database.CurrentTransaction?.TransactionId;
            }
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
