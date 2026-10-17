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
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed class BackgroundActivityWorkerTests
{
    [Fact]
    public async Task Records_typed_scalar_completion_and_enqueues_one_coordinator_receipt()
    {
        var probe = new Probe(_ => ActivityTransition.Complete(42));
        await using var harness = await Harness.CreateAsync<int>(probe, "42");

        await harness.RunAsync();

        Assert.Equal(1, probe.Executions);
        Assert.Equal(1, probe.Disposals);
        Assert.Equal(BackgroundActivityWorkResultKind.Complete, harness.Store.Item!.Result!.Kind);
        Assert.Equal("42", harness.Store.Item.Result.ResultJson);
        Assert.Equal(harness.Request.CoordinatorWorkItemId, (await harness.Queue.DequeueAsync(harness.Request.WorkflowExecutionId))!.WorkItemId);
        Assert.Null(await harness.Queue.DequeueAsync(harness.Request.WorkflowExecutionId));
    }

    [Fact]
    public async Task Records_unit_completion()
    {
        var probe = new Probe(_ => ActivityTransition.Complete(ActivityUnit.Value));
        await using var harness = await Harness.CreateAsync<ActivityUnit>(probe, "{}");

        await harness.RunAsync();

        Assert.Equal(BackgroundActivityWorkResultKind.Complete, harness.Store.Item!.Result!.Kind);
        Assert.Equal("{}", harness.Store.Item.Result.ResultJson);
    }

    [Fact]
    public async Task Records_custom_dto_completion_using_the_pinned_result_type()
    {
        var probe = new Probe(_ => ActivityTransition.Complete(new WorkerDto("accepted", 7)));
        await using var harness = await Harness.CreateAsync<WorkerDto>(probe, "{\"name\":\"accepted\",\"count\":7}");

        await harness.RunAsync();

        Assert.Equal(BackgroundActivityWorkResultKind.Complete, harness.Store.Item!.Result!.Kind);
        Assert.Equal(harness.ExpectedJson, harness.Store.Item.Result.ResultJson);
    }

    [Fact]
    public async Task Records_returned_fault_and_cancel_as_distinct_terminal_results()
    {
        var faultProbe = new Probe(_ => ActivityTransition.Fault<int>(new ActivityFault("business-error", "No")));
        await using var faultHarness = await Harness.CreateAsync<int>(faultProbe, "0");
        await faultHarness.RunAsync();

        Assert.Equal(BackgroundActivityWorkResultKind.Fault, faultHarness.Store.Item!.Result!.Kind);
        Assert.Equal("business-error", faultHarness.Store.Item.Result.Fault!.Code);
        Assert.Equal(1, faultProbe.Disposals);

        var cancelProbe = new Probe(_ => ActivityTransition.Cancel<int>("operator-cancelled"));
        await using var cancelHarness = await Harness.CreateAsync<int>(cancelProbe, "0");
        await cancelHarness.RunAsync();

        Assert.Equal(BackgroundActivityWorkResultKind.Cancel, cancelHarness.Store.Item!.Result!.Kind);
        Assert.Equal("operator-cancelled", cancelHarness.Store.Item.Result.CancellationReason);
        Assert.Equal(1, cancelProbe.Disposals);
    }

    [Fact]
    public async Task Converts_thrown_body_and_disposal_failures_to_fault_receipts()
    {
        var throwProbe = new Probe(_ => throw new InvalidOperationException("body failed"));
        await using var throwHarness = await Harness.CreateAsync<int>(throwProbe, "0");
        await throwHarness.RunAsync();

        Assert.Equal(BackgroundActivityWorkResultKind.Fault, throwHarness.Store.Item!.Result!.Kind);
        Assert.Equal("BackgroundActivityFaulted", throwHarness.Store.Item.Result.Fault!.Code);
        Assert.Equal("body failed", throwHarness.Store.Item.Result.Fault.Message);
        Assert.Equal(1, throwProbe.Disposals);

        var disposeProbe = new Probe(_ => ActivityTransition.Complete(9)) { DisposeFailure = true };
        await using var disposeHarness = await Harness.CreateAsync<int>(disposeProbe, "0");
        await disposeHarness.RunAsync();

        Assert.Equal(BackgroundActivityWorkResultKind.Fault, disposeHarness.Store.Item!.Result!.Kind);
        Assert.Equal("ActivityDisposalFailed", disposeHarness.Store.Item.Result.Fault!.Code);
        Assert.Equal(1, disposeProbe.Disposals);
    }

    [Fact]
    public async Task Rejects_unsupported_transition_as_a_fault_receipt()
    {
        var probe = new Probe(_ => ActivityTransition.Suspend<int, string>("waiting", [
            new ActivityTriggerExpectation("approval", "Event", new ValueTypeDescriptor("String"))]));
        await using var harness = await Harness.CreateAsync<int>(probe, "0");

        await harness.RunAsync();

        Assert.Equal(BackgroundActivityWorkResultKind.Fault, harness.Store.Item!.Result!.Kind);
        Assert.Equal("BackgroundActivityFaulted", harness.Store.Item.Result.Fault!.Code);
        Assert.Contains("must return Complete, Fault or Cancel", harness.Store.Item.Result.Fault.Message, StringComparison.Ordinal);
        Assert.Equal(1, probe.Disposals);
    }

    [Fact]
    public async Task Stale_claim_is_rejected_before_activation_or_dispatch()
    {
        var probe = new Probe(_ => ActivityTransition.Complete(1));
        await using var harness = await Harness.CreateAsync<int>(probe, "1");
        var valid = harness.Claim;
        var stale = new BackgroundActivityWorkClaim(
            valid.WorkflowExecutionId, valid.WorkId, valid.ClaimOwnerId, valid.ClaimFence,
            valid.ClaimedAt, valid.VisibleUntil, valid.Revision + 1);

        await harness.Worker.ExecuteClaimAsync(harness.Scope, stale);

        Assert.Equal(0, probe.Executions);
        Assert.Equal(0, probe.Disposals);
        Assert.Equal(BackgroundActivityWorkStatus.Claimed, harness.Store.Item!.Status);
        Assert.Null(harness.Store.Item.Result);
        Assert.Null(await harness.Queue.DequeueAsync(harness.Request.WorkflowExecutionId));
    }

    private sealed record WorkerDto(string Name, int Count);

    [BackgroundExecutionSafe]
    private sealed class ProbeActivity<TResult>(Probe probe) : Activity<TResult>, IDisposable
    {
        protected override ValueTask<ActivityTransition<TResult>> ExecuteAsync(ActivityExecutionContext context)
        {
            probe.Executions++;
            return ValueTask.FromResult((ActivityTransition<TResult>)probe.Execute(context));
        }

        public void Dispose()
        {
            probe.Disposals++;
            if (probe.DisposeFailure)
                throw new InvalidOperationException("dispose failed");
        }
    }

    private sealed class Probe(Func<ActivityExecutionContext, ActivityTransition> execute)
    {
        public int Executions { get; set; }
        public int Disposals { get; set; }
        public bool DisposeFailure { get; init; }
        public ActivityTransition Execute(ActivityExecutionContext context) => execute(context);
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly ServiceProvider _services;
        private Harness(
            ServiceProvider services,
            string expectedJson,
            TestBackgroundActivityWorkStore store,
            InMemoryWorkflowSchedulerWorkQueue queue,
            BackgroundActivityWorkRequest request,
            BackgroundActivityWorkClaim claim)
        {
            _services = services;
            ExpectedJson = expectedJson;
            Store = store;
            Queue = queue;
            Request = request;
            Claim = claim;
            Scope = new PersistenceScope("worker-test");
            Worker = services.GetRequiredService<BackgroundActivityWorker>();
        }

        public string ExpectedJson { get; }
        public TestBackgroundActivityWorkStore Store { get; }
        public InMemoryWorkflowSchedulerWorkQueue Queue { get; }
        public BackgroundActivityWorkRequest Request { get; }
        public BackgroundActivityWorkClaim Claim { get; }
        public PersistenceScope Scope { get; }
        public BackgroundActivityWorker Worker { get; }

        public static async Task<Harness> CreateAsync<TResult>(Probe probe, string expectedJson)
        {
            var activityType = typeof(ProbeActivity<TResult>);
            var activityAlias = TypeAliasConvention.CanonicalAlias(activityType);
            var resultType = typeof(TResult);
            var resultAlias = resultType == typeof(ActivityUnit) ? "Elsa.Unit" : TypeAliasConvention.CanonicalAlias(resultType);
            var executableStore = new InMemoryWorkflowExecutableStore();
            var store = new TestBackgroundActivityWorkStore();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddWorkflowRuntime();
            new ActivitiesRuntimeFeature().ConfigureServices(services);
            new SerializationFeature().ConfigureServices(services);
            new BackgroundActivitiesFeature
            {
                RunWorkers = false,
                LeaseDurationSeconds = 7200,
                RenewalIntervalSeconds = 3600
            }.ConfigureServices(services);
            services.Replace(ServiceDescriptor.Singleton<IWorkflowExecutableStore>(executableStore));
            services.Replace(ServiceDescriptor.Singleton<IBackgroundActivityWorkStore>(store));
            services.Replace(ServiceDescriptor.Singleton<IActivityActivator>(new ProbeActivator(() => new ProbeActivity<TResult>(probe))));
            services.Configure<BackgroundActivityWorkerOptions>(options => options.MaximumResultBytes = 4096);
            var provider = services.BuildServiceProvider();

            var types = provider.GetRequiredService<IWellKnownTypeRegistry>();
            types.RegisterType(activityType, activityAlias);
            types.RegisterType(resultType, resultAlias);
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
            var claim = store.InitializeClaim(request, "worker-1", now);
            return new Harness(provider, expectedJson, store,
                (InMemoryWorkflowSchedulerWorkQueue)provider.GetRequiredService<IWorkflowSchedulerWorkQueue>(), request, claim);
        }

        public Task RunAsync() => Worker.ExecuteClaimAsync(Scope, Claim);

        public ValueTask DisposeAsync() => _services.DisposeAsync();
    }

    private sealed class ProbeActivator(Func<IActivity> create) : IActivityActivator
    {
        public ValueTask<ActivityActivationLease> ActivateAsync(ActivityActivationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ActivityActivationLease(create()));
    }

    private sealed class TestBackgroundActivityWorkStore : IBackgroundActivityWorkStore
    {
        public BackgroundActivityWorkItem? Item { get; private set; }

        public BackgroundActivityWorkClaim InitializeClaim(BackgroundActivityWorkRequest request, string owner, DateTimeOffset now)
        {
            var pending = BackgroundActivityWorkLifecycle.CreatePending(request, now);
            Item = BackgroundActivityWorkLifecycle.TryClaim(pending, owner, now, TimeSpan.FromHours(2), out var claim)!;
            return claim!;
        }

        public ValueTask<BackgroundActivityWorkItem?> FindAsync(string workflowExecutionId, string workId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Item is not null && Item.Request.WorkflowExecutionId == workflowExecutionId && Item.Request.WorkId == workId ? Item : null);

        public ValueTask<BackgroundActivityWorkClaim?> ClaimNextAsync(string ownerId, DateTimeOffset now, TimeSpan visibilityTimeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<BackgroundActivityWorkClaim?> RenewClaimAsync(BackgroundActivityWorkClaim claim, DateTimeOffset now, TimeSpan visibilityTimeout, CancellationToken cancellationToken = default)
        {
            Item = BackgroundActivityWorkLifecycle.TryRenewClaim(Item!, claim, now, visibilityTimeout, out var renewed);
            return ValueTask.FromResult(renewed);
        }

        public ValueTask<BackgroundActivityWorkItem> RecordResultAsync(BackgroundActivityWorkClaim claim, BackgroundActivityWorkTerminalResult result, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            Item = BackgroundActivityWorkLifecycle.RecordResult(Item!, claim, result, now);
            return ValueTask.FromResult(Item);
        }

        public ValueTask<IReadOnlyCollection<BackgroundActivityWorkItem>> ListResultReadyAsync(int limit, string? workflowExecutionId = null, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyCollection<BackgroundActivityWorkItem>>(Item?.Status == BackgroundActivityWorkStatus.ResultReady ? [Item] : []);
    }
}
