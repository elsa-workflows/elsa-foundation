using System.Text.Json;
using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Api.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class ActivityExecutionValuePayloadReaderTests
{
    [Fact]
    public async Task Payload_resolution_requires_a_separate_permission_and_audits_denial_without_payload()
    {
        var harness = await Harness.CreateAsync(canResolve: false);

        var result = await harness.Reader.ReadAsync("wf", "outer", "value-1", default);

        Assert.Equal(ActivityExecutionValuePayloadReadOutcome.Denied, result.Outcome);
        var audit = Assert.Single(harness.Audit.Records);
        Assert.Equal("denied", audit.Outcome);
        Assert.Equal("value-1", audit.EvidenceId);
        Assert.Equal("subject-1", audit.AuditSubject);
        Assert.Equal("request-1", audit.RequestCorrelationId);
    }

    [Fact]
    public async Task Authorized_payload_resolution_returns_exact_capture_and_audits_success()
    {
        var harness = await Harness.CreateAsync(canResolve: true);

        var result = await harness.Reader.ReadAsync("wf", "outer", "value-1", default);

        Assert.Equal(ActivityExecutionValuePayloadReadOutcome.Resolved, result.Outcome);
        Assert.Equal(42, result.Value!.Payload.GetInt32());
        Assert.Equal("Payload", result.Value.CaptureMode);
        Assert.Equal("resolved", Assert.Single(harness.Audit.Records).Outcome);
    }

    [Fact]
    public async Task Payload_resolution_fails_closed_without_an_attributable_audit_subject()
    {
        var harness = await Harness.CreateAsync(canResolve: true, auditSubject: "");

        var result = await harness.Reader.ReadAsync("wf", "outer", "value-1", default);

        Assert.Equal(ActivityExecutionValuePayloadReadOutcome.Denied, result.Outcome);
        Assert.Null(result.Value);
        Assert.Empty(harness.Audit.Records);
    }

    [Theory]
    [InlineData(false, true, true, "value-1")]
    [InlineData(true, false, true, "value-1")]
    [InlineData(true, true, false, "value-1")]
    [InlineData(true, true, true, "missing")]
    public async Task Missing_or_hidden_workflow_projection_and_evidence_are_non_disclosing_not_found(
        bool saveWorkflow,
        bool canInspect,
        bool saveInspection,
        string evidenceId)
    {
        var harness = await Harness.CreateAsync(
            canResolve: true,
            saveWorkflow: saveWorkflow,
            canInspect: canInspect,
            saveInspection: saveInspection);

        var result = await harness.Reader.ReadAsync("wf", "outer", evidenceId, default);

        Assert.Equal(ActivityExecutionValuePayloadReadOutcome.NotFound, result.Outcome);
        Assert.Null(result.Value);
        Assert.Empty(harness.Audit.Records);
    }

    [Theory]
    [InlineData(RuntimePayloadCaptureMode.Payload, false)]
    [InlineData(RuntimePayloadCaptureMode.MetadataOnly, true)]
    public async Task Capture_without_a_resolvable_payload_is_unavailable_and_audited(
        RuntimePayloadCaptureMode captureMode,
        bool includePayload)
    {
        var harness = await Harness.CreateAsync(
            canResolve: true,
            captureMode: captureMode,
            includePayload: includePayload);

        var result = await harness.Reader.ReadAsync("wf", "outer", "value-1", default);

        Assert.Equal(ActivityExecutionValuePayloadReadOutcome.Unavailable, result.Outcome);
        Assert.Null(result.Value);
        Assert.Equal("unavailable", Assert.Single(harness.Audit.Records).Outcome);
    }

    [Fact]
    public async Task Cancellation_is_propagated_without_audit()
    {
        var harness = await Harness.CreateAsync(canResolve: true);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            harness.Reader.ReadAsync("wf", "outer", "value-1", cancellation.Token).AsTask());

        Assert.Empty(harness.Audit.Records);
    }

    [Theory]
    [InlineData(true, RuntimePayloadCaptureMode.Payload, true)]
    [InlineData(false, RuntimePayloadCaptureMode.Payload, true)]
    [InlineData(true, RuntimePayloadCaptureMode.MetadataOnly, true)]
    public async Task Audit_failure_prevents_any_payload_resolution_outcome_from_returning(
        bool canResolve,
        RuntimePayloadCaptureMode captureMode,
        bool includePayload)
    {
        var failure = new InvalidOperationException("audit unavailable");
        var harness = await Harness.CreateAsync(
            canResolve,
            captureMode: captureMode,
            includePayload: includePayload,
            auditFailure: failure);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Reader.ReadAsync("wf", "outer", "value-1", default).AsTask());

        Assert.Same(failure, exception);
        Assert.Empty(harness.Audit.Records);
    }

    private sealed record Harness(
        ActivityExecutionValuePayloadReader Reader,
        RecordingAuditSink Audit)
    {
        public static async ValueTask<Harness> CreateAsync(
            bool canResolve,
            string auditSubject = "subject-1",
            bool saveWorkflow = true,
            bool canInspect = true,
            bool saveInspection = true,
            RuntimePayloadCaptureMode captureMode = RuntimePayloadCaptureMode.Payload,
            bool includePayload = true,
            Exception? auditFailure = null)
        {
            var workflows = new InMemoryWorkflowExecutionStateStore();
            var inspections = new InMemoryActivityExecutionInspectionStore();
            if (saveWorkflow)
            {
                await workflows.SaveAsync(new(
                    "wf",
                    new("artifact", "definition", "version", "1", "hash"),
                    WorkflowExecutionStatus.Completed,
                    null,
                    DateTimeOffset.UnixEpoch,
                    DateTimeOffset.UnixEpoch,
                    DateTimeOffset.UnixEpoch,
                    DateTimeOffset.UnixEpoch,
                    null,
                    null,
                    "tenant-a",
                    new Dictionary<string, string>()));
            }
            var projection = ActivityExecutionInspectionProjectionTests.Projection(
                "outer",
                "outer",
                null,
                1,
                ActivityExecutionStatus.Completed,
                boundary: true) with
            {
                ValueSnapshots =
                [
                    new(
                        "result",
                        ActivityExecutionInspectionValueSubject.ActivityOutput,
                        captureMode,
                        null,
                        DateTimeOffset.UnixEpoch,
                        includePayload ? JsonSerializer.SerializeToElement(42) : null,
                        "captured",
                        false,
                        new Dictionary<string, string>(),
                        EvidenceId: "value-1")
                ]
            };
            if (saveInspection)
                await inspections.SaveAsync(projection);
            var audit = new RecordingAuditSink(auditFailure);
            var reader = new ActivityExecutionValuePayloadReader(
                workflows,
                inspections,
                new Authorization(canResolve, auditSubject, canInspect),
                audit,
                TimeProvider.System);
            return new(reader, audit);
        }
    }

    private sealed class Authorization(
        bool canResolve,
        string auditSubject,
        bool canInspect) : IActivityExecutionInspectionAuthorizationContext
    {
        public string TenantScope => "tenant:tenant-a";
        public string AuthorizationProfile => $"resolve:{canResolve}";
        public string AuditSubject => auditSubject;
        public string RequestCorrelationId => "request-1";
        public bool CanInspectStructure(WorkflowExecutionState workflowExecution) => canInspect;
        public bool CanInspectSensitiveValues(WorkflowExecutionState workflowExecution) => true;
        public bool CanResolveSensitiveValuePayloads(WorkflowExecutionState workflowExecution) => canResolve;
    }

    private sealed class RecordingAuditSink(Exception? failure) : IActivityExecutionValuePayloadAuditSink
    {
        public List<ActivityExecutionValuePayloadAuditRecord> Records { get; } = [];

        public ValueTask RecordAsync(
            ActivityExecutionValuePayloadAuditRecord record,
            CancellationToken cancellationToken = default)
        {
            if (failure is not null)
                return ValueTask.FromException(failure);
            Records.Add(record);
            return ValueTask.CompletedTask;
        }
    }
}
