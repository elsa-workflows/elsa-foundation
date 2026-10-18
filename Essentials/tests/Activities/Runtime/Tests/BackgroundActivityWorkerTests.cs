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
    public async Task Rejects_an_outcome_only_callback_from_background_execution()
    {
        var probe = new Probe(_ => ActivityTransition.Complete(ActivityUnit.Value));
        await using var harness = await Harness.CreateAsync<ActivityUnit>(
            probe,
            "{}",
            activationOverride: () => new OutcomeOnlyProbeActivity(probe));

        await harness.RunAsync();

        Assert.Equal(BackgroundActivityWorkResultKind.Fault, harness.Store.Item!.Result!.Kind);
        Assert.Equal("BackgroundActivityFaulted", harness.Store.Item.Result.Fault!.Code);
        Assert.Contains("pinned background-safe leaf contract", harness.Store.Item.Result.Fault.Message, StringComparison.Ordinal);
        Assert.Equal(0, probe.Executions);
        Assert.Equal(1, probe.Disposals);
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

        var disposeProbe = new Probe(_ => throw new ClassifiedActivityException("body failed", "RetryableBodyFailure", true)) { DisposeFailure = true };
        await using var disposeHarness = await Harness.CreateAsync<int>(disposeProbe, "0");
        await disposeHarness.RunAsync();

        Assert.Equal(BackgroundActivityWorkResultKind.Fault, disposeHarness.Store.Item!.Result!.Kind);
        Assert.Equal("ActivityDisposalFailed", disposeHarness.Store.Item.Result.Fault!.Code);
        Assert.True(disposeHarness.Store.Item.Result.Fault.IsRetryable);
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

    [Fact]
    public async Task Marked_but_wrong_activated_type_is_disposed_without_running()
    {
        var alternateProbe = new Probe(_ => ActivityTransition.Complete(2));
        await using var harness = await Harness.CreateAsync<int>(
            new Probe(_ => ActivityTransition.Complete(1)), "1",
            activationOverride: () => new AlternateProbeActivity<int>(alternateProbe));

        await harness.RunAsync();

        Assert.Equal(0, alternateProbe.Executions);
        Assert.Equal(1, alternateProbe.Disposals);
        Assert.Equal(BackgroundActivityWorkResultKind.Fault, harness.Store.Item!.Result!.Kind);
        Assert.Contains("different type", harness.Store.Item.Result.Fault!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mismatched_pinned_executable_hash_is_rejected_before_activation()
    {
        var probe = new Probe(_ => ActivityTransition.Complete(1));
        var identity = new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:stale");
        await using var harness = await Harness.CreateAsync<int>(probe, "1", requestIdentityOverride: identity);

        await harness.RunAsync();

        Assert.Equal(0, probe.Executions);
        Assert.Equal(0, probe.Disposals);
        Assert.Equal(BackgroundActivityWorkResultKind.Fault, harness.Store.Item!.Result!.Kind);
        Assert.Contains("pinned executable artifact", harness.Store.Item.Result.Fault!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_pinned_executable_is_recorded_as_terminal_validation_fault()
    {
        var probe = new Probe(_ => ActivityTransition.Complete(1));
        await using var harness = await Harness.CreateAsync<int>(
            probe, "1", executableReaderOverride: new MissingWorkflowExecutableReader());

        await harness.RunAsync();

        Assert.Equal(0, probe.Executions);
        Assert.Equal(0, probe.Disposals);
        Assert.Equal(BackgroundActivityWorkResultKind.Fault, harness.Store.Item!.Result!.Kind);
        Assert.Contains("pinned background executable is unavailable", harness.Store.Item.Result.Fault!.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("coordinator-work-1", (await harness.Queue.DequeueAsync(harness.Request.WorkflowExecutionId))!.WorkItemId);
    }

    [Fact]
    public async Task Preserves_declared_classification_for_activation_faults()
    {
        var probe = new Probe(_ => ActivityTransition.Complete(1));
        var failure = new ClassifiedActivityException("resolver unavailable", "SecretStoreUnavailable", isRetryable: true);
        await using var harness = await Harness.CreateAsync<int>(probe, "1", activationOverride: () => throw failure);

        await harness.RunAsync();

        Assert.Equal(0, probe.Executions);
        Assert.Equal(BackgroundActivityWorkResultKind.Fault, harness.Store.Item!.Result!.Kind);
        Assert.Equal("SecretStoreUnavailable", harness.Store.Item.Result.Fault!.Code);
        Assert.True(harness.Store.Item.Result.Fault.IsRetryable);
    }

    [Fact]
    public async Task Pinned_executable_read_failure_propagates_and_leaves_claim_recoverable()
    {
        var probe = new Probe(_ => ActivityTransition.Complete(1));
        var failure = new ProviderLikeExecutableReadException("simulated transient artifact read failure");
        await using var harness = await Harness.CreateAsync<int>(
            probe, "1", executableReaderOverride: new ThrowingWorkflowExecutableReader(failure));

        var thrown = await Assert.ThrowsAsync<ProviderLikeExecutableReadException>(() => harness.RunAsync());

        Assert.Same(failure, thrown);
        Assert.Equal(0, probe.Executions);
        Assert.Equal(0, probe.Disposals);
        Assert.Equal(BackgroundActivityWorkStatus.Claimed, harness.Store.Item!.Status);
        Assert.Null(harness.Store.Item.Result);
        Assert.Null(await harness.Queue.DequeueAsync(harness.Request.WorkflowExecutionId));
    }

    [Fact]
    public async Task Work_item_from_another_persistence_scope_is_not_visible_to_the_worker()
    {
        var probe = new Probe(_ => ActivityTransition.Complete(1));
        await using var harness = await Harness.CreateAsync<int>(probe, "1");

        await harness.Worker.ExecuteClaimAsync(new PersistenceScope("another-scope"), harness.Claim);

        Assert.Equal(0, probe.Executions);
        Assert.Equal(0, probe.Disposals);
        Assert.Equal(BackgroundActivityWorkStatus.Claimed, harness.Store.Item!.Status);
        Assert.Null(harness.Store.Item.Result);
        Assert.Null(await harness.Queue.DequeueAsync(harness.Request.WorkflowExecutionId));
    }

    private sealed record WorkerDto(string Name, int Count);

    private abstract class ProbeActivityCore<TResult>(Probe probe) : Activity<TResult>, IDisposable
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

    [BackgroundExecutionSafe]
    private sealed class ProbeActivity<TResult>(Probe probe) : ProbeActivityCore<TResult>(probe);

    [BackgroundExecutionSafe]
    private sealed class AlternateProbeActivity<TResult>(Probe probe) : ProbeActivityCore<TResult>(probe);

    [BackgroundExecutionSafe]
    private sealed class OutcomeOnlyProbeActivity(Probe probe) : ProbeActivityCore<ActivityUnit>(probe), IRuntimeActivityChildOutcomeHandler
    {
        public ValueTask<RuntimeStructuralContinuation> OnChildOutcomeAsync(ActivityChildOutcomeContext context) =>
            ValueTask.FromResult(RuntimeStructuralContinuation.Complete());
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
            TestBackgroundActivityWorkStoreState store,
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
        public TestBackgroundActivityWorkStoreState Store { get; }
        public InMemoryWorkflowSchedulerWorkQueue Queue { get; }
        public BackgroundActivityWorkRequest Request { get; }
        public BackgroundActivityWorkClaim Claim { get; }
        public PersistenceScope Scope { get; }
        public BackgroundActivityWorker Worker { get; }

        public static async Task<Harness> CreateAsync<TResult>(
            Probe probe,
            string expectedJson,
            Func<IActivity>? activationOverride = null,
            WorkflowExecutableIdentity? requestIdentityOverride = null,
            IWorkflowExecutableReader? executableReaderOverride = null)
        {
            var activityType = typeof(ProbeActivity<TResult>);
            var activityAlias = TypeAliasConvention.CanonicalAlias(activityType);
            var resultType = typeof(TResult);
            var resultAlias = resultType == typeof(ActivityUnit) ? "Elsa.Unit" : TypeAliasConvention.CanonicalAlias(resultType);
            var executableStore = new InMemoryWorkflowExecutableStore();
            var store = new TestBackgroundActivityWorkStoreState();
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
            if (executableReaderOverride is not null)
                services.Replace(ServiceDescriptor.Scoped<IWorkflowExecutableReader>(_ => executableReaderOverride));
            services.Replace(ServiceDescriptor.Scoped<IBackgroundActivityWorkStore>(serviceProvider =>
                new TestBackgroundActivityWorkStore(serviceProvider.GetRequiredService<IPersistenceAccessContextAccessor>(), store)));
            services.Replace(ServiceDescriptor.Singleton<IActivityActivator>(new ProbeActivator(
                activationOverride ?? (() => new ProbeActivity<TResult>(probe)))));
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
            var requestIdentity = requestIdentityOverride ?? identity;
            var coordinatorPayload = JsonSerializer.SerializeToElement(new RuntimeInvokeActivityCommandPayload(
                requestIdentity, "node-1", "activity-execution-1", "BackgroundActivityResult"));
            var coordinatorWork = new RuntimeSchedulerWorkItem(
                "coordinator-work-1", "workflow-1", "coordinator-command-1", WorkflowExecutionCommandKind.InvokeActivity,
                "envelope-1", "idempotency-1", now, now, payload: coordinatorPayload);
            var request = new BackgroundActivityWorkRequest(
                "background-work-1", "workflow-1", "activity-execution-1", attempt, requestIdentity, "node-1",
                contract.SchemaFingerprint, snapshot, coordinatorWork);
            var claim = store.InitializeClaim(request, "worker-1", now, "worker-test");
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

    private sealed class ProviderLikeExecutableReadException(string message) : InvalidOperationException(message);

    private sealed class ClassifiedActivityException(string message, string? failureCode, bool isRetryable)
        : Exception(message), IRuntimeFaultClassification
    {
        public string? FailureCode { get; } = failureCode;
        public bool IsRetryable { get; } = isRetryable;
    }

    private sealed class ThrowingWorkflowExecutableReader(Exception failure) : IWorkflowExecutableReader
    {
        public ValueTask<WorkflowExecutable?> FindAsync(string artifactId, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<WorkflowExecutable?>(failure);
    }

    private sealed class MissingWorkflowExecutableReader : IWorkflowExecutableReader
    {
        public ValueTask<WorkflowExecutable?> FindAsync(string artifactId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<WorkflowExecutable?>(null);
    }

    private sealed class TestBackgroundActivityWorkStoreState
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

        public ValueTask<BackgroundActivityWorkClaim?> RenewClaimAsync(BackgroundActivityWorkClaim claim, DateTimeOffset now, TimeSpan visibilityTimeout, CancellationToken cancellationToken = default)
        {
            if (!IsVisible)
                return ValueTask.FromResult<BackgroundActivityWorkClaim?>(null);
            var updated = BackgroundActivityWorkLifecycle.TryRenewClaim(state.Item!, claim, now, visibilityTimeout, out var renewed);
            state.Item = updated;
            return ValueTask.FromResult(renewed);
        }

        public ValueTask<BackgroundActivityWorkItem> RecordResultAsync(BackgroundActivityWorkClaim claim, BackgroundActivityWorkTerminalResult result, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            if (!IsVisible)
                throw new BackgroundActivityWorkConflictException(claim.WorkflowExecutionId, claim.WorkId, "work is outside the active persistence scope");
            var updated = BackgroundActivityWorkLifecycle.RecordResult(state.Item!, claim, result, now);
            state.Item = updated;
            return ValueTask.FromResult(updated);
        }

        public ValueTask<IReadOnlyCollection<BackgroundActivityWorkItem>> ListResultReadyAsync(int limit, string? workflowExecutionId = null, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyCollection<BackgroundActivityWorkItem>>(IsVisible && state.Item?.Status == BackgroundActivityWorkStatus.ResultReady ? [state.Item] : []);
    }
}
