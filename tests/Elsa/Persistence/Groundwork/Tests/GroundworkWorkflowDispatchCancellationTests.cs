using Elsa.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

public sealed class GroundworkWorkflowDispatchCancellationTests
{
    private static readonly DateTimeOffset AdmittedAt = DateTimeOffset.UnixEpoch.AddSeconds(1);
    private static readonly DateTimeOffset CancelledAt = DateTimeOffset.UnixEpoch.AddSeconds(2);

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Cancellation_winner_suppresses_every_later_admission(string provider)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create(provider);
        var store = CreateStore(fixture);
        var pending = WaitPending("parent-cancel-first", "activity-1");
        await store.SaveAsync(pending);

        var cancellationGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var admissionGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellation = ApplyCancellationAfterAsync(store, pending, cancellationGate.Task);
        var admission = AdmitAfterAsync(store, pending.DispatchId, admissionGate.Task);

        cancellationGate.SetResult();
        var cancellationResult = await cancellation;
        admissionGate.SetResult();
        var admissionResult = await admission;

        Assert.Equal(WorkflowDispatchCancellationDisposition.AppliedBeforeAdmission, cancellationResult.Disposition);
        Assert.Equal(WorkflowDispatchAdmissionDisposition.CancelledBeforeAdmission, admissionResult.Disposition);
        Assert.Equal(WorkflowDispatchStatus.Cancelled, admissionResult.Record.Status);
        Assert.True(WorkflowDispatchLifecycle.WasCancelledBeforeAdmission(admissionResult.Record));

        var duplicate = await store.TryAdmitAsync(pending.DispatchId, AdmittedAt.AddMinutes(1));
        Assert.Equal(WorkflowDispatchAdmissionDisposition.CancelledBeforeAdmission, duplicate.Disposition);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Admission_winner_retains_started_state_and_records_cancellation_marker(string provider)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create(provider);
        var store = CreateStore(fixture);
        var pending = WaitPending("parent-admit-first", "activity-1");
        await store.SaveAsync(pending);

        var admissionGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var admission = AdmitAfterAsync(store, pending.DispatchId, admissionGate.Task);
        var cancellation = ApplyCancellationAfterAsync(store, pending, cancellationGate.Task);

        admissionGate.SetResult();
        var admissionResult = await admission;
        cancellationGate.SetResult();
        var cancellationResult = await cancellation;

        Assert.Equal(WorkflowDispatchAdmissionDisposition.Admitted, admissionResult.Disposition);
        Assert.Equal(WorkflowDispatchCancellationDisposition.CancellationRequestedAfterAdmission, cancellationResult.Disposition);
        Assert.Equal(WorkflowDispatchStatus.Started, cancellationResult.Record.Status);
        Assert.True(WorkflowDispatchLifecycle.IsCancellationRequested(cancellationResult.Record));

        var duplicate = await store.ApplyCancellationAsync(Request(pending));
        Assert.Equal(WorkflowDispatchCancellationDisposition.CancellationRequestedAfterAdmission, duplicate.Disposition);
        Assert.True(WorkflowDispatchLifecycle.RecordsEqual(cancellationResult.Record, duplicate.Record));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Admission_clamps_an_earlier_delivery_clock_to_the_durable_record_time(string provider)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create(provider);
        var store = CreateStore(fixture);
        var pending = WaitPending("parent-clock-rollback", "activity-1");
        await store.SaveAsync(pending);

        var result = await store.TryAdmitAsync(pending.DispatchId, pending.UpdatedAt.AddMinutes(-1));

        Assert.Equal(WorkflowDispatchAdmissionDisposition.Admitted, result.Disposition);
        Assert.Equal(WorkflowDispatchStatus.Started, result.Record.Status);
        Assert.Equal(pending.UpdatedAt, result.Record.UpdatedAt);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Admission_rejects_a_missing_delivery_timestamp(string provider)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create(provider);
        var store = CreateStore(fixture);
        var pending = WaitPending("parent-missing-admission-time", "activity-1");
        await store.SaveAsync(pending);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => store.TryAdmitAsync(pending.DispatchId, default).AsTask());

        Assert.Equal(WorkflowDispatchStatus.Pending, (await store.FindAsync(pending.DispatchId))?.Status);
    }

    [Theory]
    [InlineData(WorkflowDispatchStatus.Completed)]
    [InlineData(WorkflowDispatchStatus.Faulted)]
    [InlineData(WorkflowDispatchStatus.Cancelled)]
    [InlineData(WorkflowDispatchStatus.DispatchFailed)]
    public async Task Existing_terminal_state_wins_over_late_cancellation(WorkflowDispatchStatus terminalStatus)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create("memory");
        var store = CreateStore(fixture);
        var pending = WaitPending($"parent-terminal-{terminalStatus}", "activity-1");
        var started = pending.TransitionTo(WorkflowDispatchStatus.Started, AdmittedAt);
        var terminal = terminalStatus == WorkflowDispatchStatus.DispatchFailed
            ? started.TransitionToDispatchFailed(CancelledAt)
            : started.TransitionTo(terminalStatus, CancelledAt);
        await store.SaveAsync(pending);
        await store.SaveAsync(started);
        await store.SaveAsync(terminal);

        var result = await store.ApplyCancellationAsync(Request(pending, CancelledAt.AddSeconds(1)));

        Assert.Equal(WorkflowDispatchCancellationDisposition.TerminalUnchanged, result.Disposition);
        Assert.True(WorkflowDispatchLifecycle.RecordsEqual(terminal, result.Record));
        Assert.False(WorkflowDispatchLifecycle.IsCancellationRequested(result.Record));
    }

    [Theory]
    [InlineData(WorkflowDispatchStatus.Completed)]
    [InlineData(WorkflowDispatchStatus.Faulted)]
    [InlineData(WorkflowDispatchStatus.Cancelled)]
    public async Task Terminal_projection_after_cancellation_marker_preserves_terminal_status(
        WorkflowDispatchStatus terminalStatus)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create("memory");
        var store = CreateStore(fixture);
        var pending = WaitPending($"parent-marker-{terminalStatus}", "activity-1");
        await store.SaveAsync(pending);
        await store.TryAdmitAsync(pending.DispatchId, AdmittedAt);
        var cancellation = await store.ApplyCancellationAsync(Request(pending));

        var terminal = cancellation.Record.TransitionTo(terminalStatus, CancelledAt.AddSeconds(1));
        await store.SaveAsync(terminal);

        var persisted = Assert.IsType<WorkflowDispatchRecord>(await store.FindAsync(pending.DispatchId));
        Assert.Equal(terminalStatus, persisted.Status);
        Assert.True(WorkflowDispatchLifecycle.IsCancellationRequested(persisted));
    }

    [Fact]
    public async Task One_hundred_concurrent_races_select_exactly_one_durable_winner()
    {
        for (var iteration = 0; iteration < 100; iteration++)
        {
            var documentStore = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.Create());
            var store = new GroundworkWorkflowDispatchStore(
                documentStore,
                GroundworkTestSerialization.Serializer,
                GroundworkTestAccess.DefaultAccessContextAccessor,
                documentStore);
            var pending = WaitPending($"parent-race-{iteration:D3}", "activity-1");
            await store.SaveAsync(pending);
            var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var admission = AdmitAfterAsync(store, pending.DispatchId, barrier.Task);
            var cancellation = ApplyCancellationAfterAsync(store, pending, barrier.Task);
            barrier.SetResult();
            await Task.WhenAll(admission, cancellation);

            var admissionResult = await admission;
            var cancellationResult = await cancellation;
            var persisted = Assert.IsType<WorkflowDispatchRecord>(await store.FindAsync(pending.DispatchId));
            if (persisted.Status == WorkflowDispatchStatus.Cancelled)
            {
                Assert.Equal(WorkflowDispatchAdmissionDisposition.CancelledBeforeAdmission, admissionResult.Disposition);
                Assert.Equal(WorkflowDispatchCancellationDisposition.AppliedBeforeAdmission, cancellationResult.Disposition);
                Assert.True(WorkflowDispatchLifecycle.WasCancelledBeforeAdmission(persisted));
            }
            else
            {
                Assert.Equal(WorkflowDispatchStatus.Started, persisted.Status);
                Assert.Contains(
                    admissionResult.Disposition,
                    new[]
                    {
                        WorkflowDispatchAdmissionDisposition.Admitted,
                        WorkflowDispatchAdmissionDisposition.AlreadyAdmitted
                    });
                Assert.Equal(WorkflowDispatchCancellationDisposition.CancellationRequestedAfterAdmission, cancellationResult.Disposition);
                Assert.True(WorkflowDispatchLifecycle.IsCancellationRequested(persisted));
            }
        }
    }

    [Fact]
    public async Task Legacy_wait_record_without_policy_metadata_defaults_to_propagation_enabled()
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create("memory");
        var store = CreateStore(fixture);
        var legacy = WaitPending("parent-legacy-wait", "activity-1", persistPolicy: false);

        await store.SaveAsync(legacy);
        var cancellation = await store.ApplyCancellationAsync(Request(legacy));

        Assert.DoesNotContain(WorkflowDispatchLifecycle.CancellationPolicyMetadataKey, legacy.Metadata.Keys);
        Assert.True(WorkflowDispatchLifecycle.IsCancellationPropagationEnabled(legacy));
        Assert.Equal(WorkflowDispatchCancellationDisposition.AppliedBeforeAdmission, cancellation.Disposition);
    }

    private static async Task<WorkflowDispatchAdmissionResult> AdmitAfterAsync(
        GroundworkWorkflowDispatchStore store,
        string dispatchId,
        Task gate)
    {
        await gate;
        return await store.TryAdmitAsync(dispatchId, AdmittedAt);
    }

    private static async Task<WorkflowDispatchCancellationResult> ApplyCancellationAfterAsync(
        GroundworkWorkflowDispatchStore store,
        WorkflowDispatchRecord record,
        Task gate)
    {
        await gate;
        return await store.ApplyCancellationAsync(Request(record));
    }

    private static WorkflowDispatchCancellationRequest Request(
        WorkflowDispatchRecord record,
        DateTimeOffset? requestedAt = null) =>
        new(
            record.DispatchId,
            record.ParentWorkflowExecutionId,
            record.ParentActivityExecutionId,
            record.ChildWorkflowExecutionId,
            requestedAt ?? CancelledAt);

    private static GroundworkWorkflowDispatchStore CreateStore(GroundworkDocumentStoreFixture fixture) => new(
        fixture.DocumentStore,
        GroundworkTestSerialization.Serializer,
        GroundworkTestAccess.DefaultAccessContextAccessor,
        fixture.BoundedDocumentStore);

    private static WorkflowDispatchRecord WaitPending(
        string parentWorkflowExecutionId,
        string parentActivityExecutionId,
        bool persistPolicy = true) =>
        GroundworkWorkflowDispatchStoreTests.Pending(
            parentWorkflowExecutionId,
            parentActivityExecutionId,
            mode: WorkflowDispatchMode.WaitForCompletion,
            cancellationPropagation: persistPolicy ? true : null);
}
