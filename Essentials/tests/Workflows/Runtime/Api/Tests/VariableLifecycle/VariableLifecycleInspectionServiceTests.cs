using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Alterations;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Values;
using Microsoft.Extensions.Time.Testing;
using Xunit;
using static Elsa.Workflows.Runtime.Api.Tests.VariableLifecycle.VariableLifecycleRecords;

namespace Elsa.Workflows.Runtime.Api.Tests.VariableLifecycle;

/// <summary>
/// Spec 1229 T009, T018, T023 (SC-005): the service over in-memory stores: value states for each permission, the payload
/// read of written, overwritten and refused values under the payload permission, the variable list of a legacy
/// instance, the comparison refusal, and the lost-update count on the instance details.
/// </summary>
public sealed class VariableLifecycleInspectionServiceTests
{
    private static readonly ValueTypeDescriptor Int32 = new("Int32");
    private static readonly string Sentinel = $"secret{Guid.NewGuid():N}";
    private readonly InMemoryWorkflowExecutionStateStore _workflows = new();
    private readonly InMemoryActivityExecutionStateStore _activities = new();
    private readonly InMemoryActivityExecutionInspectionStore _inspections = new();
    private readonly InMemoryWorkflowAlterationStore _alterations = new();
    private readonly InMemoryDurableValueStateStore _durableValues = new();
    private readonly RecordingAuditSink _audit = new();

    [Fact]
    public async Task A_captured_written_value_shows_captured_with_a_payload_reference_and_resolves_under_the_payload_permission()
    {
        await SeedLostUpdateAsync(captureWrittenValue: true);
        var service = Service(sensitive: true, resolve: true);

        var page = (await service.ListWritesAsync(new VariableWriteHistoryQuery(WorkflowExecutionId), default))!;
        var right = page.Items.Single(item => item.Writer.ActivityExecutionId == "r");

        Assert.Equal("captured", right.Value.State);
        Assert.Equal("written", right.Value.PayloadRef!.Value);
        Assert.Equal("lostUpdateWarning", right.Outcome);
        Assert.NotNull(right.ConcurrentOverwrite);
        Assert.True(right.ConcurrentOverwrite!.IsLostUpdate);
        Assert.Equal("captured", right.OverwrittenValue!.State);
        Assert.True(right.OverwrittenValue.IsOperational);
        var written = await service.ReadPayloadAsync(WorkflowExecutionId, right.EntryId, "written", default);
        var overwritten = await service.ReadPayloadAsync(WorkflowExecutionId, right.EntryId, "overwritten", default);
        Assert.Equal(2, written.Value!.Payload.GetInt32());
        Assert.Equal(1, overwritten.Value!.Payload.GetInt32());
        Assert.Equal(["resolved", "resolved"], _audit.Outcomes);
    }

    [Fact]
    public async Task Without_the_sensitive_value_permission_values_show_redacted_and_metadata_stays()
    {
        await SeedLostUpdateAsync(captureWrittenValue: true);

        var page = (await Service(sensitive: false, resolve: false).ListWritesAsync(new VariableWriteHistoryQuery(WorkflowExecutionId), default))!;

        var right = page.Items.Single(item => item.Writer.ActivityExecutionId == "r");
        Assert.Equal("redacted", right.Value.State);
        Assert.Null(right.Value.PayloadRef);
        Assert.Equal("redacted", right.OverwrittenValue!.State);
        Assert.Equal(1, right.CurrentVersion);
        Assert.Equal("node-right", right.Writer.ExecutableNodeId);
        Assert.DoesNotContain(Sentinel, JsonSerializer.Serialize(page), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_value_that_was_not_captured_shows_omitted_not_null()
    {
        await SeedLostUpdateAsync(captureWrittenValue: false);

        var page = (await Service(sensitive: true, resolve: true).ListWritesAsync(new VariableWriteHistoryQuery(WorkflowExecutionId), default))!;

        Assert.All(page.Items, item => Assert.Equal("omitted", item.Value.State));
    }

    [Fact]
    public async Task A_captured_null_is_a_value_read_through_the_payload()
    {
        await SeedLostUpdateAsync(captureWrittenValue: true, writtenPayload: JsonSerializer.SerializeToElement<object?>(null));
        var service = Service(sensitive: true, resolve: true);
        var right = (await service.ListWritesAsync(new VariableWriteHistoryQuery(WorkflowExecutionId), default))!.Items.Single(item => item.Writer.ActivityExecutionId == "r");

        var read = await service.ReadPayloadAsync(WorkflowExecutionId, right.EntryId, "written", default);

        Assert.Equal("captured", right.Value.State);
        Assert.Equal(JsonValueKind.Null, read.Value!.Payload.ValueKind);
    }

    [Fact]
    public async Task The_payload_route_refuses_without_the_payload_permission_and_never_releases_the_value()
    {
        await SeedLostUpdateAsync(captureWrittenValue: true);
        var service = Service(sensitive: true, resolve: false);
        var right = (await service.ListWritesAsync(new VariableWriteHistoryQuery(WorkflowExecutionId), default))!.Items.Single(item => item.Writer.ActivityExecutionId == "r");

        var read = await service.ReadPayloadAsync(WorkflowExecutionId, right.EntryId, "overwritten", default);

        Assert.Equal(VariableWritePayloadOutcome.Denied, read.Outcome);
        Assert.Null(read.Value);
        Assert.Equal(["denied"], _audit.Outcomes);
    }

    [Fact]
    public async Task A_refused_Fault_value_resolves_as_the_refused_value()
    {
        await _workflows.SaveAsync(Workflow());
        await _activities.SaveAsync(Row("r", "node-right", stamps: [Scheduled(2), Faulted(4)], status: ActivityExecutionStatus.Faulted,
            writes: [Write("r", "node-right", 4, 1, 1, VariableCommitOutcome.LostUpdateFault, baseVersion: 0, rejected: Inline(7))]));
        var service = Service(sensitive: true, resolve: true);

        var entry = Assert.Single((await service.ListWritesAsync(new VariableWriteHistoryQuery(WorkflowExecutionId), default))!.Items);
        var read = await service.ReadPayloadAsync(WorkflowExecutionId, entry.EntryId, "refused", default);

        Assert.False(entry.IsApplied);
        Assert.Equal("captured", entry.RefusedValue!.State);
        Assert.Equal(7, read.Value!.Payload.GetInt32());
    }

    [Fact]
    public async Task Filters_on_overwrites_return_the_total_and_unknown_entries_are_not_found()
    {
        await SeedLostUpdateAsync(captureWrittenValue: false);
        var service = Service(sensitive: true, resolve: true);

        var overwrites = (await service.ListWritesAsync(new VariableWriteHistoryQuery(WorkflowExecutionId, Overwrites: true), default))!;
        var lostUpdates = (await service.ListWritesAsync(new VariableWriteHistoryQuery(WorkflowExecutionId, Outcome: "lostUpdateWarning"), default))!;
        var missing = await service.ReadPayloadAsync(WorkflowExecutionId, "not-an-entry", "written", default);

        Assert.Equal(1, overwrites.ConcurrentOverwriteTotal);
        Assert.Single(overwrites.Items);
        Assert.Single(lostUpdates.Items);
        Assert.Null(lostUpdates.ConcurrentOverwriteTotal);
        Assert.Equal(VariableWritePayloadOutcome.NotFound, missing.Outcome);
    }

    [Fact]
    public async Task History_pages_continue_where_the_previous_page_ended()
    {
        await _workflows.SaveAsync(Workflow());
        await _activities.SaveAsync(Row("a", "node-a", stamps: [Scheduled(1)], writes:
        [
            Write("a", "node-a", 2, 0, 1, key: "x"),
            Write("a", "node-a", 2, 0, 1, key: "y"),
            Write("a", "node-a", 2, 0, 1, key: "z")
        ]));
        var service = Service(sensitive: true, resolve: true);

        var first = (await service.ListWritesAsync(new VariableWriteHistoryQuery(WorkflowExecutionId, Take: 2), default))!;
        var second = (await service.ListWritesAsync(new VariableWriteHistoryQuery(WorkflowExecutionId, Cursor: first.Next, Take: 2), default))!;

        Assert.Equal(["x", "y"], first.Items.Select(item => item.VariableKey));
        Assert.Equal(["z"], second.Items.Select(item => item.VariableKey));
        Assert.Null(second.Next);
    }

    [Fact]
    public async Task A_legacy_instance_lists_its_variables_at_version_0_with_no_history_recorded()
    {
        var frame = new VariableFrameState(RootFrame, RootScope, WorkflowExecutionId, null, VariableFrameKind.Root,
            new Dictionary<string, ValueEnvelope> { ["count"] = Inline(3) });
        await _workflows.SaveAsync(Workflow() with { RootVariableFrame = frame });
        await _activities.SaveAsync(Row("legacy", "node-legacy"));

        var variable = Assert.Single((await Service(sensitive: true, resolve: true).ListVariablesAsync(WorkflowExecutionId, default))!);

        Assert.Equal(0, variable.Version);
        Assert.False(variable.HistoryRecorded);
        Assert.Equal("omitted", variable.Value.State);
        Assert.Equal("root", variable.FrameKind);
    }

    [Fact]
    public async Task Instances_of_different_versions_are_refused_and_a_missing_one_is_not_found()
    {
        await _workflows.SaveAsync(Workflow());
        await _workflows.SaveAsync(Workflow("wfexec-2") with { PinnedExecutable = new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-2", "2.0.0", "sha256:other") });
        var service = Service(sensitive: true, resolve: true);

        var refused = await service.CompareAsync(WorkflowExecutionId, "wfexec-2", default);
        var missing = await service.CompareAsync(WorkflowExecutionId, "wfexec-missing", default);

        Assert.Equal(InstanceComparisonOutcome.DifferentExecutable, refused.Outcome);
        Assert.Equal("differentExecutable", refused.View!.RefusalReason);
        Assert.False(refused.View.Comparable);
        Assert.Equal(InstanceComparisonOutcome.NotFound, missing.Outcome);
    }

    [Fact]
    public async Task Two_runs_that_committed_in_a_different_order_diverge_at_the_first_differing_commit_without_values()
    {
        await _workflows.SaveAsync(Workflow() with { Status = WorkflowExecutionStatus.Completed });
        await _workflows.SaveAsync(Workflow("wfexec-2") with { Status = WorkflowExecutionStatus.Completed });
        foreach (var (instance, first, second) in new[] { (WorkflowExecutionId, "node-left", "node-right"), ("wfexec-2", "node-right", "node-left") })
        {
            await _activities.SaveAsync(InInstance(instance, Row($"{instance}-p", "node-parallel", stamps: [Scheduled(1), Completed(6)])));
            await _activities.SaveAsync(InInstance(instance, Row($"{instance}-1", first, parent: $"{instance}-p", stamps: [Scheduled(2), Completed(3)], writes: [Write($"{instance}-1", first, 3, 0, 1, digest: $"d-{first}")])));
            await _activities.SaveAsync(InInstance(instance, Row($"{instance}-2", second, parent: $"{instance}-p", stamps: [Scheduled(2), Completed(4)], writes: [Write($"{instance}-2", second, 4, 1, 2, digest: $"d-{second}")])));
        }

        var compared = await Service(sensitive: true, resolve: true).CompareAsync(WorkflowExecutionId, "wfexec-2", default);
        var identical = await Service(sensitive: true, resolve: true).CompareAsync(WorkflowExecutionId, WorkflowExecutionId, default);

        Assert.Equal(InstanceComparisonOutcome.Compared, compared.Outcome);
        Assert.Equal(2, compared.View!.FirstDivergence!.CommitIndex);
        var count = Assert.Single(compared.View.Variables);
        Assert.True(count.FinalDigestDiffers);
        Assert.True(count.WrittenDigestsDiffer);
        Assert.All(compared.View.FirstDivergence.Left!.Writes, write => Assert.Null(write.Value.PayloadRef));
        Assert.Null(identical.View!.FirstDivergence);
        Assert.All(identical.View.Variables, variable => Assert.False(variable.FinalDigestDiffers || variable.WrittenDigestsDiffer));
    }

    [Fact]
    public async Task The_instance_lost_update_warning_count_sums_the_rows_and_counts_a_row_without_the_member()
    {
        await SeedLostUpdateAsync(captureWrittenValue: false);
        // A row saved without the member (a direct save) is counted from its records.
        await _activities.SaveAsync(Row("s", "node-set", parent: "p", stamps: [Scheduled(2), Completed(5)],
            writes: [Write("s", "node-set", 5, 2, 3, VariableCommitOutcome.LostUpdateWarning, baseVersion: 1)]));

        Assert.Equal(2, await ((Elsa.Workflows.Runtime.Core.Contracts.IActivityExecutionStateStore)_activities).SumLostUpdateWarningCountAsync(WorkflowExecutionId));
    }

    // Left wrote 1 at commit 3; right, on the stale base 0, wrote 2 at commit 4 over it with a lost-update warning.
    private async Task SeedLostUpdateAsync(bool captureWrittenValue, JsonElement? writtenPayload = null)
    {
        await _workflows.SaveAsync(Workflow());
        await _activities.SaveAsync(Row("p", "node-parallel", stamps: [Scheduled(1)], status: ActivityExecutionStatus.Running));
        await _activities.SaveAsync(Row("l", "node-left", parent: "p", stamps: [Scheduled(2), Completed(3)], writes: [Write("l", "node-left", 3, 0, 1, VariableCommitOutcome.Current, baseVersion: 0)]));
        var lost = Write("r", "node-right", 4, 1, 2, VariableCommitOutcome.LostUpdateWarning, baseVersion: 0, overwritten: Inline(1));
        await _activities.SaveAsync(Row("r", "node-right", parent: "p", stamps: [Scheduled(2), Completed(4)], writes: [lost]) with { LostUpdateWarningCount = 1 });
        if (captureWrittenValue)
            await _inspections.SaveAsync(Projection("r", lost, writtenPayload ?? JsonSerializer.SerializeToElement(2)));
    }

    private static ActivityExecutionState InInstance(string workflowExecutionId, ActivityExecutionState row) =>
        row with { Execution = row.Execution with { WorkflowExecutionId = workflowExecutionId } };

    private VariableLifecycleInspectionService Service(bool sensitive, bool resolve) =>
        new(_workflows, _activities, _inspections, _alterations, _durableValues, new Access(sensitive, resolve), _audit, new FakeTimeProvider(Now));

    private static WorkflowExecutionState Workflow(string id = WorkflowExecutionId) =>
        new(id, new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"), WorkflowExecutionStatus.Running,
            null, Now, Now, Now, null, null, null, null, new Dictionary<string, string>());

    private static ValueEnvelope Inline(int value) => ValueEnvelope.Inline(Int32, JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline);

    private static ActivityExecutionInspectionProjection Projection(string rowId, VariableWriteRecord record, JsonElement payload) =>
        new(rowId, WorkflowExecutionId, "node-right", "authored-node-right", "Test", "1.0.0", ActivityExecutionStatus.Completed, null, 1,
            Now, Now, Now, "checkpoint-1", "checkpoint-1", Now, ActivitySchedulingProvenance.Empty, [], [], [],
            [
                ActivityExecutionInspectionValueSnapshot.FromDecision(
                    record.VariableKey,
                    ActivityExecutionInspectionValueSubject.VariableWrite,
                    new RuntimePayloadCaptureDecision(RuntimePayloadCaptureMode.Payload, "captured"),
                    new RuntimeValueTypeDescriptor("alias", "Int32", null),
                    Now,
                    payload,
                    false,
                    phase: record.ScopeId,
                    sequence: record.NewVersion)
            ],
            new Dictionary<string, string>());

    private sealed class Access(bool sensitive, bool resolve) : IActivityInspectionContextAsync
    {
        public string TenantScope => "tenant-a";
        public string AuditSubject => "subject-1";
        public string RequestCorrelationId => "request-1";
        public ValueTask<string> GetAuthorizationProfileAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult("structure");
        public ValueTask<bool> CanInspectStructureAsync(WorkflowExecutionState workflowExecution, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
        public ValueTask<bool> CanInspectSensitiveValuesAsync(WorkflowExecutionState workflowExecution, CancellationToken cancellationToken = default) => ValueTask.FromResult(sensitive);
        public ValueTask<bool> CanResolveSensitiveValuePayloadsAsync(WorkflowExecutionState workflowExecution, CancellationToken cancellationToken = default) => ValueTask.FromResult(resolve);
    }

    private sealed class RecordingAuditSink : IActivityExecutionValuePayloadAuditSink
    {
        public List<string> Outcomes { get; } = [];

        public ValueTask RecordAsync(ActivityExecutionValuePayloadAuditRecord record, CancellationToken cancellationToken = default)
        {
            Outcomes.Add(record.Outcome);
            return ValueTask.CompletedTask;
        }
    }
}
