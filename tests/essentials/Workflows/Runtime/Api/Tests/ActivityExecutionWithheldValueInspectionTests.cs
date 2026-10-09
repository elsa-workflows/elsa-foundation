using System.Text.Json;
using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Api.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Api.Tests;

/// <summary>
/// The run inspector's activity-execution and value-payload reads of a withheld input (spec 188, FR-011, T067). A withheld
/// input is rendered by its marker and secret reference, which are not values, for every caller that may inspect the
/// execution; it is always reported sensitive, and its payload is never released. The descendants read carries no value
/// evidence at all (<c>ActivityExecutionHierarchyTests</c> in <c>Elsa.Workflows.Runtime.Tests</c>).
/// </summary>
public sealed class ActivityExecutionWithheldValueInspectionTests
{
    private const string WorkflowExecutionId = "wfexec-1";
    private const string ActivityExecutionId = "actexec-1";
    private const string ReferenceName = "payments.api-key";
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Sentinel = $"plain{Guid.NewGuid():N}";
    private readonly InMemoryWorkflowExecutionStateStore _workflows = new();
    private readonly InMemoryActivityExecutionInspectionStore _inspections = new();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_activity_execution_view_renders_a_withheld_input_by_its_marker_and_reference(bool canInspectSensitiveValues)
    {
        await SaveAsync(WithheldInput(isSensitive: true));

        var input = Assert.Single((await ViewAsync(canInspectSensitiveValues, canResolve: true)).ValueSnapshots);

        Assert.Equal(nameof(WithheldValueKind.SecretReference), input.WithheldKind);
        Assert.Equal(ReferenceName, input.SecretReferenceName);
        Assert.True(input.IsSensitive);
        Assert.Equal("unavailable", input.AccessState);
        Assert.Null(input.Payload);
        Assert.Null(input.Snapshot);
    }

    [Fact]
    public async Task A_withheld_input_recorded_as_not_sensitive_is_still_reported_sensitive()
    {
        // An imported artifact can carry a secret read whose policy is not sensitive (research R13); the marker alone
        // decides what the inspector reports.
        await SaveAsync(WithheldInput(isSensitive: false));

        var input = Assert.Single((await ViewAsync(canInspectSensitiveValues: false, canResolve: false)).ValueSnapshots);

        Assert.True(input.IsSensitive);
        Assert.Equal(ReferenceName, input.SecretReferenceName);
    }

    [Fact]
    public async Task The_value_payload_read_of_a_withheld_input_is_unavailable()
    {
        await SaveAsync(WithheldInput(isSensitive: true));
        var evidenceId = Assert.Single((await ViewAsync(canInspectSensitiveValues: true, canResolve: true)).ValueSnapshots).EvidenceId;

        var read = await Reader(canResolve: true).ReadAsync(WorkflowExecutionId, ActivityExecutionId, evidenceId, default);

        Assert.Equal(ActivityExecutionValuePayloadReadOutcome.Unavailable, read.Outcome);
        Assert.Null(read.Value);
    }

    [Fact]
    public async Task A_payload_recorded_beside_a_withheld_marker_is_never_offered_or_released()
    {
        // No shipped producer records one: inspection renders a withheld input without a payload. This is the shape a
        // renderer that resolved the reference to show it would leave, and neither read may hand it on.
        await SaveAsync(WithheldInput(isSensitive: false, payload: JsonSerializer.SerializeToElement(Sentinel)));

        var view = await ViewAsync(canInspectSensitiveValues: true, canResolve: true);
        var input = Assert.Single(view.ValueSnapshots);
        var read = await Reader(canResolve: true).ReadAsync(WorkflowExecutionId, ActivityExecutionId, input.EvidenceId, default);

        Assert.Equal("unavailable", input.AccessState);
        Assert.Equal(ActivityExecutionValuePayloadReadOutcome.Unavailable, read.Outcome);
        Assert.Null(read.Value);
        Assert.DoesNotContain(Sentinel, JsonSerializer.Serialize(view), StringComparison.Ordinal);
        Assert.DoesNotContain(Sentinel, JsonSerializer.Serialize(read), StringComparison.Ordinal);
    }

    private async Task SaveAsync(ActivityExecutionInspectionValueSnapshot snapshot)
    {
        await _workflows.SaveAsync(new WorkflowExecutionState(
            WorkflowExecutionId,
            new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            WorkflowExecutionStatus.Running,
            null,
            Now,
            Now,
            Now,
            null,
            null,
            null,
            null,
            new Dictionary<string, string>()));
        await _inspections.SaveAsync(new ActivityExecutionInspectionProjection(
            ActivityExecutionId,
            WorkflowExecutionId,
            "node-1",
            "authored-1",
            "Test.Activity",
            "1.0.0",
            ActivityExecutionStatus.Running,
            null,
            1,
            Now,
            Now,
            null,
            "checkpoint-1",
            "checkpoint-1",
            Now,
            ActivitySchedulingProvenance.Empty,
            [],
            [],
            [],
            [snapshot],
            new Dictionary<string, string>()));
    }

    private async Task<ActivityExecutionInspectionView> ViewAsync(bool canInspectSensitiveValues, bool canResolve) =>
        (await new ActivityExecutionInspectionService(_workflows, _inspections, new InspectionAccess(canInspectSensitiveValues, canResolve))
            .GetAsync(new GetActivityExecution(WorkflowExecutionId, ActivityExecutionId), CancellationToken.None))!;

    private ActivityExecutionValuePayloadReader Reader(bool canResolve) =>
        new(_workflows, _inspections, new InspectionAccess(canInspectSensitiveValues: true, canResolve), new DiscardingAuditSink(), new FakeTimeProvider(Now));

    /// <summary>The value evidence record inspection writes for a withheld secret input, as the invoke path builds it.</summary>
    private static ActivityExecutionInspectionValueSnapshot WithheldInput(bool isSensitive, JsonElement? payload = null) =>
        ActivityExecutionInspectionValueSnapshot.FromDecision(
            "token",
            ActivityExecutionInspectionValueSubject.ActivityInput,
            new RuntimePayloadCaptureDecision(RuntimePayloadCaptureMode.Payload, "Full payload captured by runtime diagnostics policy."),
            new RuntimeValueTypeDescriptor("alias", "String", null),
            Now,
            payload,
            isSensitive,
            ActivityExecutionInspectionValueSnapshot.MarkWithheld(
                new Dictionary<string, string>(),
                WithheldValue.SecretReference(new RuntimeSecretReference(ReferenceName), null)),
            inputKey: "token");

    private sealed class InspectionAccess(bool canInspectSensitiveValues, bool canResolve) : IActivityInspectionContextAsync
    {
        public string TenantScope => "tenant-a";
        public string AuditSubject => "subject-1";
        public string RequestCorrelationId => "request-1";
        public ValueTask<string> GetAuthorizationProfileAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult("structure");
        public ValueTask<bool> CanInspectStructureAsync(WorkflowExecutionState workflowExecution, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
        public ValueTask<bool> CanInspectSensitiveValuesAsync(WorkflowExecutionState workflowExecution, CancellationToken cancellationToken = default) => ValueTask.FromResult(canInspectSensitiveValues);
        public ValueTask<bool> CanResolveSensitiveValuePayloadsAsync(WorkflowExecutionState workflowExecution, CancellationToken cancellationToken = default) => ValueTask.FromResult(canResolve);
    }

    private sealed class DiscardingAuditSink : IActivityExecutionValuePayloadAuditSink
    {
        public ValueTask RecordAsync(ActivityExecutionValuePayloadAuditRecord record, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
