using System.Text.Json;
using Elsa.Activities.Runtime;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed class BackgroundActivityWorkerRenewalTests
{
    [Fact]
    public async Task Completed_body_waits_for_an_in_flight_renewal_to_publish_before_recording_receipt()
    {
        var gate = new RenewalRaceGate();
        await using var harness = await Harness.CreateAsync(gate);
        var run = harness.Worker.ExecuteClaimAsync(harness.Scope, harness.Claim);

        await gate.RenewalCommitted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await gate.BodyEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        gate.AllowBodyCompletion.TrySetResult();
        try
        {
            await gate.BodyDisposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(run.IsCompleted);
        }
        finally
        {
            gate.AllowRenewalReturn.TrySetResult();
        }

        await run.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(BackgroundActivityWorkStatus.ResultReady, harness.Store.Item!.Status);
        Assert.Equal("23", harness.Store.Item.Result!.ResultJson);
        Assert.NotNull(await harness.Queue.DequeueAsync(harness.Request.WorkflowExecutionId));
    }

    [Fact]
    public async Task Cancellation_before_receipt_is_treated_as_a_fenced_result_without_waking_the_coordinator()
    {
        var gate = new RenewalRaceGate { PauseReceipt = true };
        await using var harness = await Harness.CreateAsync(gate);
        var run = harness.Worker.ExecuteClaimAsync(harness.Scope, harness.Claim);

        try
        {
            await FinishBodyBeforeReceiptAsync(gate, run);
            harness.Store.Cancel();
            gate.AllowReceipt.TrySetResult();
            await run.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            gate.AllowBodyCompletion.TrySetResult();
            gate.AllowRenewalReturn.TrySetResult();
            gate.AllowReceipt.TrySetResult();
        }

        Assert.Equal(BackgroundActivityWorkStatus.Cancelled, harness.Store.Item!.Status);
        Assert.Null(harness.Store.Item.Result);
        Assert.Null(await harness.Queue.DequeueAsync(harness.Request.WorkflowExecutionId));
    }

    [Fact]
    public async Task Non_cancellation_receipt_conflict_still_escapes_the_worker()
    {
        var gate = new RenewalRaceGate { PauseReceipt = true };
        await using var harness = await Harness.CreateAsync(gate);
        var run = harness.Worker.ExecuteClaimAsync(harness.Scope, harness.Claim);

        try
        {
            await FinishBodyBeforeReceiptAsync(gate, run);
            harness.Store.MakeClaimStale();
            gate.AllowReceipt.TrySetResult();
            var conflict = await Assert.ThrowsAsync<BackgroundActivityWorkConflictException>(() => run.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Contains("stale, expired, or superseded", conflict.Message, StringComparison.Ordinal);
        }
        finally
        {
            gate.AllowBodyCompletion.TrySetResult();
            gate.AllowRenewalReturn.TrySetResult();
            gate.AllowReceipt.TrySetResult();
        }

        Assert.Equal(BackgroundActivityWorkStatus.Claimed, harness.Store.Item!.Status);
        Assert.Null(await harness.Queue.DequeueAsync(harness.Request.WorkflowExecutionId));
    }

    private static async Task FinishBodyBeforeReceiptAsync(RenewalRaceGate gate, Task run)
    {
        await gate.RenewalCommitted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await gate.BodyEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        gate.AllowBodyCompletion.TrySetResult();
        await gate.BodyDisposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(run.IsCompleted);
        gate.AllowRenewalReturn.TrySetResult();
        await gate.ReceiptAttempted.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class RenewalRaceGate
    {
        public TaskCompletionSource RenewalCommitted { get; } = NewSignal();
        public TaskCompletionSource AllowRenewalReturn { get; } = NewSignal();
        public TaskCompletionSource BodyEntered { get; } = NewSignal();
        public TaskCompletionSource AllowBodyCompletion { get; } = new();
        public TaskCompletionSource BodyDisposed { get; } = NewSignal();
        public bool PauseReceipt { get; init; }
        public TaskCompletionSource ReceiptAttempted { get; } = NewSignal();
        public TaskCompletionSource AllowReceipt { get; } = NewSignal();

        private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    [BackgroundExecutionSafe]
    private sealed class RenewalRaceActivity(RenewalRaceGate gate) : Activity<int>, IDisposable
    {
        protected override async ValueTask<ActivityTransition<int>> ExecuteAsync(ActivityExecutionContext context)
        {
            gate.BodyEntered.TrySetResult();
            await gate.AllowBodyCompletion.Task.WaitAsync(context.CancellationToken);
            return ActivityTransition.Complete(23);
        }

        public void Dispose() => gate.BodyDisposed.TrySetResult();
    }

    private sealed class RenewalRaceActivator(RenewalRaceGate gate) : IActivityActivator
    {
        public ValueTask<ActivityActivationLease> ActivateAsync(
            ActivityActivationRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ActivityActivationLease(new RenewalRaceActivity(gate)));
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly ServiceProvider _services;

        private Harness(
            ServiceProvider services,
            TestBackgroundActivityWorkStoreState store,
            InMemoryWorkflowSchedulerWorkQueue queue,
            BackgroundActivityWorkRequest request,
            BackgroundActivityWorkClaim claim)
        {
            _services = services;
            Store = store;
            Queue = queue;
            Request = request;
            Claim = claim;
            Scope = new PersistenceScope("renewal-test");
            Worker = services.GetRequiredService<BackgroundActivityWorker>();
        }

        public TestBackgroundActivityWorkStoreState Store { get; }
        public InMemoryWorkflowSchedulerWorkQueue Queue { get; }
        public BackgroundActivityWorkRequest Request { get; }
        public BackgroundActivityWorkClaim Claim { get; }
        public PersistenceScope Scope { get; }
        public BackgroundActivityWorker Worker { get; }

        public static async Task<Harness> CreateAsync(RenewalRaceGate gate)
        {
            var activityType = typeof(RenewalRaceActivity);
            var activityAlias = TypeAliasConvention.CanonicalAlias(activityType);
            var resultAlias = TypeAliasConvention.CanonicalAlias(typeof(int));
            var executableStore = new InMemoryWorkflowExecutableStore();
            var store = new TestBackgroundActivityWorkStoreState(gate);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddWorkflowRuntime();
            new ActivitiesRuntimeFeature().ConfigureServices(services);
            new SerializationFeature().ConfigureServices(services);
            new BackgroundActivitiesFeature
            {
                RunWorkers = false,
                LeaseDurationSeconds = 7200,
                RenewalIntervalSeconds = 0.001
            }.ConfigureServices(services);
            services.Replace(ServiceDescriptor.Singleton<IWorkflowExecutableStore>(executableStore));
            services.Replace(ServiceDescriptor.Scoped<IBackgroundActivityWorkStore>(serviceProvider =>
                new TestBackgroundActivityWorkStore(
                    serviceProvider.GetRequiredService<IPersistenceAccessContextAccessor>(), store)));
            services.Replace(ServiceDescriptor.Singleton<IActivityActivator>(new RenewalRaceActivator(gate)));
            var provider = services.BuildServiceProvider();

            var types = provider.GetRequiredService<IWellKnownTypeRegistry>();
            types.RegisterType(activityType, activityAlias);
            types.RegisterType(typeof(int), resultAlias);
            var serializer = provider.GetRequiredService<IPayloadSerializer>();
            var descriptorPayload = serializer.SerializeToElement(new ClrActivityDescriptor(activityAlias));
            var contract = new ActivityContract(
                activityAlias,
                "1.0.0",
                typeof(ClrActivityDescriptor).FullName!,
                descriptorPayload,
                [],
                new ActivityResultContract(new ValueTypeDescriptor(resultAlias), true, ActivityValuePolicy.Default, []),
                ["Done"],
                new ActivityActivationRequirement(typeof(ClrActivityDescriptor).FullName!, activityAlias),
                SideEffectProfile.External,
                supportsBackgroundExecution: true);
            var node = new ExecutableNode(
                "node-1", "authored-1", activityAlias, "1.0.0", WellKnownRuntimeActivityConsumers.ClrActivity,
                descriptorPayload, new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(),
                ActivityExecutionPlacement.Background, activityContract: contract);
            var identity = new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test");
            var executable = new WorkflowExecutable(
                identity, node, new Dictionary<string, WorkflowExecutableResumeTarget>(), DateTimeOffset.UtcNow,
                new Dictionary<string, string>(), IncidentStrategyBuiltIns.FaultReference);
            await executableStore.SaveAsync(executable);

            var now = DateTimeOffset.UtcNow;
            var attempt = new ActivityAttempt("attempt-1", "invocation-1", 1, ActivityAttemptReason.Initial, now);
            var snapshot = new ActivityInputSnapshot("invocation-1", contract.SchemaFingerprint, "bindings-1", new Dictionary<string, ValueEnvelope>(), now);
            var coordinatorPayload = JsonSerializer.SerializeToElement(new RuntimeInvokeActivityCommandPayload(
                identity, "node-1", "activity-execution-1", "BackgroundActivityResult"));
            var coordinatorWork = new RuntimeSchedulerWorkItem(
                "coordinator-work-1", "workflow-1", "coordinator-command-1", WorkflowExecutionCommandKind.InvokeActivity,
                "envelope-1", "idempotency-1", now, now, payload: coordinatorPayload);
            var request = new BackgroundActivityWorkRequest(
                "background-work-1", "workflow-1", "activity-execution-1", attempt, identity, "node-1",
                contract.SchemaFingerprint, snapshot, coordinatorWork);
            var claim = store.InitializeClaim(request, "worker-1", now, "renewal-test");
            return new Harness(provider, store,
                (InMemoryWorkflowSchedulerWorkQueue)provider.GetRequiredService<IWorkflowSchedulerWorkQueue>(), request, claim);
        }

        public ValueTask DisposeAsync() => _services.DisposeAsync();
    }

    private sealed class TestBackgroundActivityWorkStoreState(RenewalRaceGate gate)
    {
        public BackgroundActivityWorkItem? Item { get; set; }
        public string? Scope { get; private set; }

        public BackgroundActivityWorkClaim InitializeClaim(BackgroundActivityWorkRequest request, string owner, DateTimeOffset now, string scope)
        {
            var pending = BackgroundActivityWorkLifecycle.CreatePending(request, now);
            Item = BackgroundActivityWorkLifecycle.TryClaim(pending, owner, now, TimeSpan.FromHours(2), out var claim)!;
            Scope = scope;
            return claim!;
        }

        public RenewalRaceGate Gate { get; } = gate;

        public void Cancel() => Item = BackgroundActivityWorkLifecycle.Cancel(Item!, DateTimeOffset.UtcNow);

        public void MakeClaimStale() => Item = Item! with { Revision = checked(Item.Revision + 1) };
    }

    private sealed class TestBackgroundActivityWorkStore(
        IPersistenceAccessContextAccessor accessContextAccessor,
        TestBackgroundActivityWorkStoreState state) : IBackgroundActivityWorkStore
    {
        private bool IsVisible => StringComparer.Ordinal.Equals(accessContextAccessor.Current.RequireScope().Value, state.Scope);

        public ValueTask<BackgroundActivityWorkItem?> FindAsync(string workflowExecutionId, string workId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(IsVisible && state.Item is not null && state.Item.Request.WorkflowExecutionId == workflowExecutionId && state.Item.Request.WorkId == workId ? state.Item : null);

        public ValueTask<BackgroundActivityWorkClaim?> ClaimNextAsync(string ownerId, DateTimeOffset now, TimeSpan visibilityTimeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async ValueTask<BackgroundActivityWorkClaim?> RenewClaimAsync(
            BackgroundActivityWorkClaim claim,
            DateTimeOffset now,
            TimeSpan visibilityTimeout,
            CancellationToken cancellationToken = default)
        {
            if (!IsVisible)
                return null;
            var updated = BackgroundActivityWorkLifecycle.TryRenewClaim(state.Item!, claim, now, visibilityTimeout, out var renewed);
            state.Item = updated;
            state.Gate.RenewalCommitted.TrySetResult();
            await state.Gate.AllowRenewalReturn.Task.WaitAsync(cancellationToken);
            return renewed;
        }

        public async ValueTask<BackgroundActivityWorkItem> RecordResultAsync(
            BackgroundActivityWorkClaim claim,
            BackgroundActivityWorkTerminalResult result,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            if (!IsVisible)
                throw new BackgroundActivityWorkConflictException(claim.WorkflowExecutionId, claim.WorkId, "work is outside the active persistence scope");

            if (state.Gate.PauseReceipt)
            {
                state.Gate.ReceiptAttempted.TrySetResult();
                await state.Gate.AllowReceipt.Task.WaitAsync(cancellationToken);
            }

            var updated = BackgroundActivityWorkLifecycle.RecordResult(state.Item!, claim, result, now);
            state.Item = updated;
            return updated;
        }

        public ValueTask<IReadOnlyCollection<BackgroundActivityWorkItem>> ListResultReadyAsync(int limit, string? workflowExecutionId = null, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyCollection<BackgroundActivityWorkItem>>(IsVisible && state.Item?.Status == BackgroundActivityWorkStatus.ResultReady ? [state.Item] : []);
    }
}
