using System.Data.Common;
using System.Text.Json;
using Elsa.Activities.Runtime.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed partial class WorkflowInvokeActivitySchedulerWorkHandlerTests
{
    [Theory]
    [InlineData("db")]
    [InlineData("wrapped-db")]
    [InlineData("twice-wrapped-db")]
    [InlineData("wrapped-io")]
    [InlineData("wrapped-timeout")]
    [InlineData("provider-cancellation-timeout")]
    public async Task Native_policy_read_outage_defers_real_queued_invoke_without_acknowledgement_or_effects(string shape)
    {
        var native = CanonicalWorkflowStateTestData.EnsureRunning(new InMemoryWorkflowExecutionStateStore());
        await native.SaveAsync((await native.FindAsync("wfexec-1"))! with
            { HistoryCapturePolicy = new("runtime-history-v3", 3, true, ["ActivityContinuationStarted"]) });
        var failure = PolicyReadFailure(shape);
        var failedReader = new FailingNativePolicyRead(native, failure);
        var activator = new RecordingTypedActivator();
        await _executableStore.SaveAsync(NewTypedExecutable());
        var original = NewTypedRunningState();
        await _activityStateStore.SaveAsync(original);
        var work = NewInvokeWorkItem(NewIdentity());
        await _schedulerWorkQueue.EnqueueAsync(work);
        await using var provider = NewProvider(activator, configure: services => services.AddSingleton<IWorkflowExecutionStateStore>(failedReader));
        var clock = new FakeTimeProvider(_now);
        var poison = new InMemoryWorkflowSchedulerPoisonStore();
        // The drainer reads healthy native state. Only the handler's actual callback policy preflight sees the
        // provider-shaped outage, so an earlier admission read cannot accidentally satisfy this regression.
        var drainer = new WorkflowSchedulerDrainer(_schedulerWorkQueue, [NewHandler(provider)], native, poison,
            new WorkflowSchedulerPauseGate(new RuntimePauseDecisionProvider(new InMemoryWorkflowHoldStateStore()), clock), clock,
            claimOptions: new RuntimeSchedulerWorkClaimOptions { UnavailableRetryDelay = TimeSpan.FromSeconds(5) });
        var result = await drainer.DrainAsync(new("wfexec-1", maxWorkItems: 1));
        Assert.Equal(1, failedReader.Reads);
        Assert.True(result.StoppedOnDeferral);
        Assert.Equal(0, result.DrainedCount);
        var retained = Assert.Single(await _schedulerWorkQueue.ListAllAsync("wfexec-1"));
        Assert.Equal(work.WorkItemId, retained.WorkItemId);
        Assert.Equal(work.Payload!.Value.GetRawText(), retained.Payload!.Value.GetRawText());
        Assert.Equal(0, activator.ActivateCalls);
        Assert.Equal(original, await _activityStateStore.FindAsync("wfexec-1", "actexec-1"));
        Assert.Empty(_checkpointWriter.ListCommits());
        Assert.Empty(await _incidentStateStore.ListAsync("wfexec-1"));
        Assert.Empty(await poison.ListAsync("wfexec-1"));
    }

    [Theory]
    [InlineData("corrupt")]
    [InlineData("wrapped-corrupt")]
    [InlineData("json")]
    [InlineData("wrapped-json")]
    [InlineData("contract")]
    [InlineData("scope")]
    [InlineData("contextual-provider-operation")]
    public async Task Native_policy_read_corruption_contract_and_cancellation_keep_their_original_classification(string shape)
    {
        var failure = PolicyReadFailure(shape);
        var native = CanonicalWorkflowStateTestData.EnsureRunning(new InMemoryWorkflowExecutionStateStore());
        var failedReader = new FailingNativePolicyRead(native, failure);
        var activator = new RecordingTypedActivator();
        await _executableStore.SaveAsync(NewTypedExecutable());
        var original = NewTypedRunningState();
        await _activityStateStore.SaveAsync(original);
        await using var provider = NewProvider(activator, configure: services => services.AddSingleton<IWorkflowExecutionStateStore>(failedReader));
        var observed = await Record.ExceptionAsync(() => NewHandler(provider).HandleAsync(NewInvokeWorkItem(NewIdentity())).AsTask());
        if (shape is "corrupt" or "wrapped-corrupt" or "json" or "wrapped-json")
            Assert.Same(failure, Assert.IsType<RuntimeHistoryIntegrityException>(observed).InnerException);
        else Assert.Same(failure, observed);
        Assert.Equal(1, failedReader.Reads);
        Assert.Equal(0, activator.ActivateCalls);
        Assert.Equal(original, await _activityStateStore.FindAsync("wfexec-1", "actexec-1"));
        Assert.Empty(_checkpointWriter.ListCommits());
        Assert.Empty(await _incidentStateStore.ListAsync("wfexec-1"));
    }

    private static Exception PolicyReadFailure(string shape) => shape switch
    {
        "db" => new PolicyDbException(),
        "provider-cancellation-timeout" => new InvalidOperationException("native adapter read failed", new OperationCanceledException("provider timeout")),
        "wrapped-db" => new InvalidOperationException("native adapter read failed", new PolicyDbException()),
        "twice-wrapped-db" => new InvalidOperationException("native adapter read failed", new InvalidOperationException("provider command failed", new PolicyDbException())),
        "wrapped-io" => new InvalidOperationException("native adapter read failed", new IOException("constructed dependency failure")),
        "wrapped-timeout" => new InvalidOperationException("native adapter read failed", new TimeoutException("constructed dependency failure")),
        "contextual-provider-operation" => new InvalidOperationException("native adapter read failed", new InvalidOperationException("constructed provider context failure")),
        "corrupt" => new InvalidDataException("constructed immutable data corruption"),
        "wrapped-corrupt" => new InvalidOperationException("native adapter read failed", new InvalidDataException("constructed immutable data corruption")),
        "json" => new JsonException("constructed malformed native document"),
        "wrapped-json" => new InvalidOperationException("native adapter read failed", new JsonException("constructed malformed native document")),
        "contract" => new ArgumentException("constructed native contract refusal"),
        "scope" => new InvalidOperationException("constructed ordinary scope refusal"),
        _ => throw new ArgumentOutOfRangeException(nameof(shape))
    };

    private sealed class PolicyDbException() : DbException("constructed provider read outage") { }

    [Fact]
    public async Task Caller_cancellation_during_policy_preflight_preserves_the_failure_without_activation_or_checkpoint()
    {
        using var cancellation = new CancellationTokenSource();
        var failure = new InvalidOperationException("native adapter cancelled", new OperationCanceledException(cancellation.Token));
        var native = CanonicalWorkflowStateTestData.EnsureRunning(new InMemoryWorkflowExecutionStateStore());
        var failedReader = new FailingNativePolicyRead(native, failure, cancellation.Cancel);
        var activator = new RecordingTypedActivator();
        await _executableStore.SaveAsync(NewTypedExecutable());
        await _activityStateStore.SaveAsync(NewTypedRunningState());
        await using var provider = NewProvider(activator, configure: services => services.AddSingleton<IWorkflowExecutionStateStore>(failedReader));
        var caught = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NewHandler(provider).HandleAsync(NewInvokeWorkItem(NewIdentity()), cancellation.Token).AsTask());
        Assert.Same(failure, caught);
        Assert.Equal(0, activator.ActivateCalls);
        Assert.Empty(_checkpointWriter.ListCommits());
        Assert.Empty(await _incidentStateStore.ListAsync("wfexec-1"));
    }

    private sealed class FailingNativePolicyRead(IWorkflowExecutionStateStore inner, Exception failure, Action? beforeFailure = null) : IWorkflowExecutionStateStore
    {
        internal int Reads { get; private set; }
        public ValueTask<WorkflowExecutionState?> FindAsync(string id, CancellationToken cancellationToken = default)
        { Reads++; beforeFailure?.Invoke(); return ValueTask.FromException<WorkflowExecutionState?>(failure); }
        public ValueTask<WorkflowExecutionState> SaveAsync(WorkflowExecutionState state, CancellationToken cancellationToken = default) => inner.SaveAsync(state, cancellationToken);
        public ValueTask<bool> DeleteAsync(string id, CancellationToken cancellationToken = default) => inner.DeleteAsync(id, cancellationToken);
        public ValueTask<IReadOnlyCollection<WorkflowExecutionState>> ListAsync(CancellationToken cancellationToken = default) => inner.ListAsync(cancellationToken);
        public ValueTask<WorkflowExecutionStatePage> QueryPageAsync(WorkflowExecutionStatePageQuery query, CancellationToken cancellationToken = default) => inner.QueryPageAsync(query, cancellationToken);
        public ValueTask<IReadOnlyCollection<string>> ListPinnedExecutableArtifactIdsAsync(CancellationToken cancellationToken = default) => inner.ListPinnedExecutableArtifactIdsAsync(cancellationToken);
    }

    [Theory]
    [InlineData("invoke", false)]
    [InlineData("invoke", true)]
    [InlineData("parent", false)]
    [InlineData("parent", true)]
    [InlineData("notification", false)]
    [InlineData("notification", true)]
    public async Task Required_pin_with_capture_module_removed_refuses_before_invoke_parent_or_notification_effects(string surface, bool required)
    {
        var parent = surface != "invoke";
        var native = CanonicalWorkflowStateTestData.EnsureRunning(new InMemoryWorkflowExecutionStateStore());
        var run = (await native.FindAsync("wfexec-1"))!;
        await native.SaveAsync(run with { HistoryCapturePolicy = required ? new("runtime-history-v3", 3, true, ["ActivityContinuationStarted"]) : null });
        var readerResolutions = 0;
        var typed = new RecordingTypedActivator();
        var structural = new FixedActivityActivator(new DeferringChildCompletionActivity());
        var executable = parent ? NewCallbackExecutable() : NewTypedExecutable();
        await _executableStore.SaveAsync(executable);
        var original = parent ? NewCallbackParentState(executable) : NewTypedRunningState(executable);
        await _activityStateStore.SaveAsync(original);
        if (parent) await _activityStateStore.SaveAsync(NewCompletedCallbackChildState());
        await using var provider = NewProvider(parent ? structural : typed, includeInspection: true, configure: services =>
        {
            services.AddSingleton<IWorkflowExecutionStateStore>(native);
            services.AddSingleton<IRuntimeHistoryStore>(_ => { readerResolutions++; throw new IOException("query-reader-must-not-resolve"); });
        });
        async Task Execute()
        {
            if (surface == "notification")
                await new WorkflowNotifyParentActivitySchedulerWorkHandler(provider.GetRequiredService<IServiceScopeFactory>(), new FakeTimeProvider(_now))
                    .HandleAsync(NewParentNotificationWorkItem("required-pin-notification", "required-pin-command"));
            else if (parent)
                await new WorkflowParentActivityCompletionSchedulerWorkHandler(provider.GetRequiredService<IServiceScopeFactory>(), new FakeTimeProvider(_now))
                    .HandleAsync(NewParentCallbackWorkItem("required-pin-callback", "required-pin-command"));
            else await NewHandler(provider).HandleAsync(NewInvokeWorkItem(NewIdentity()));
        }
        if (required)
        {
            var unavailable = await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(Execute);
            Assert.Equal("runtime.history.capture-unavailable", unavailable.Code);
            Assert.Equal(original, await _activityStateStore.FindAsync("wfexec-1", "actexec-1"));
            Assert.Empty(_checkpointWriter.ListCommits());
            Assert.Empty(await _incidentStateStore.ListAsync("wfexec-1"));
            Assert.Equal(0, parent ? structural.ActivateCalls : typed.ActivateCalls);
        }
        else
        {
            await Execute();
            Assert.Equal(1, parent ? structural.ActivateCalls : typed.ActivateCalls);
        }
        Assert.Equal(0, readerResolutions);
    }
}
