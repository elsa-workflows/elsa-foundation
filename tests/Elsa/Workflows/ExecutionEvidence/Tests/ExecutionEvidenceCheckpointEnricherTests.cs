using Elsa.Workflows.ExecutionEvidence.Contracts;
using Elsa.Workflows.ExecutionEvidence.Models;
using Elsa.Workflows.ExecutionEvidence.Services;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.ExecutionEvidence.Tests;

/// <summary>
/// Drives synthetic checkpoint commits through the enricher against a real store: the cross-checkpoint variable diff
/// lives in the store, so a mocked sink would assert the enricher's intermediate shape rather than the observable
/// evidence a test framework actually reads.
/// </summary>
public sealed class ExecutionEvidenceCheckpointEnricherTests
{
    private readonly InMemoryExecutionEvidenceStore _store = new();
    private readonly CapturingLogger _logger = new();
    private readonly ExecutionEvidenceCheckpointEnricher _enricher;

    public ExecutionEvidenceCheckpointEnricherTests() => _enricher = new(_store, logger: _logger);

    [Fact]
    public async Task Lifecycle_checkpoint_is_recorded_under_its_runtime_checkpoint_name()
    {
        await EnrichAsync(EvidenceFactory.Commit(name: RuntimeCheckpointNames.WorkflowStarted));

        var record = Assert.Single(Records());
        Assert.Equal(RuntimeCheckpointNames.WorkflowStarted, record.Kind);
        Assert.Equal(EvidenceFactory.WorkflowId, record.WorkflowExecutionId);
        Assert.Equal("cp-1", record.CheckpointId);
        Assert.Equal(nameof(WorkflowExecutionStatus.Running), record.Status);
        Assert.Equal(EvidenceFactory.Now, record.OccurredAt);
        // A lifecycle fact carries no variable, so it must not claim a value disposition either.
        Assert.Null(record.ValueDisposition);
    }

    [Fact]
    public async Task Activity_inspection_change_becomes_an_activity_record()
    {
        await EnrichAsync(EvidenceFactory.Commit(
            name: RuntimeCheckpointNames.ActivityCompleted,
            inspections:
            [
                EvidenceFactory.Inspection(
                    activityExecutionId: "act-7",
                    activityType: "Elsa.WriteLine",
                    authoredActivityId: "write-hello",
                    status: ActivityExecutionStatus.Completed,
                    outcomes: ["Done", "Next"])
            ]));

        var record = Assert.Single(Records(ExecutionEvidenceKinds.Activity));
        Assert.Equal("act-7", record.ActivityExecutionId);
        Assert.Equal("Elsa.WriteLine", record.ActivityType);
        Assert.Equal("write-hello", record.AuthoredActivityId);
        Assert.Equal(nameof(ActivityExecutionStatus.Completed), record.Status);
        Assert.Equal(["Done", "Next"], record.Outcomes!);
    }

    [Fact]
    public async Task Incident_change_becomes_an_incident_record()
    {
        await EnrichAsync(EvidenceFactory.Commit(
            name: RuntimeCheckpointNames.IncidentRecorded,
            incidents: [EvidenceFactory.Incident(activityExecutionId: "act-9", message: "Boom.")]));

        var record = Assert.Single(Records(ExecutionEvidenceKinds.Incident));
        Assert.Equal("act-9", record.ActivityExecutionId);
        Assert.Equal("Boom.", record.Message);
    }

    [Fact]
    public async Task New_root_variable_is_captured_with_its_inline_json_value()
    {
        await EnrichAsync(EvidenceFactory.Commit(variables: EvidenceFactory.InlineVariables(("greeting", "hello"))));

        var record = Assert.Single(Records(ExecutionEvidenceKinds.VariableSet));
        Assert.Equal("greeting", record.Name);
        Assert.Equal("hello", record.Value?.GetString());
        Assert.Equal(ExecutionEvidenceValueDisposition.Captured, record.ValueDisposition);
    }

    [Fact]
    public async Task Unchanged_root_variable_is_not_recorded_again()
    {
        var variables = EvidenceFactory.InlineVariables(("greeting", "hello"));

        await EnrichAsync(EvidenceFactory.Commit(checkpointId: "cp-1", variables: variables));
        await EnrichAsync(EvidenceFactory.Commit(checkpointId: "cp-2", variables: variables));

        Assert.Single(Records(ExecutionEvidenceKinds.VariableSet));
    }

    [Fact]
    public async Task Changed_root_variable_is_recorded_again_with_the_new_value()
    {
        await EnrichAsync(EvidenceFactory.Commit(
            checkpointId: "cp-1",
            variables: EvidenceFactory.InlineVariables(("greeting", "hello"))));
        await EnrichAsync(EvidenceFactory.Commit(
            checkpointId: "cp-2",
            variables: EvidenceFactory.InlineVariables(("greeting", "goodbye"))));

        Assert.Equal(
            ["hello", "goodbye"],
            Records(ExecutionEvidenceKinds.VariableSet).Select(record => record.Value?.GetString()));
    }

    [Fact]
    public async Task A_declared_but_unassigned_root_variable_is_recorded_as_absent()
    {
        await EnrichAsync(EvidenceFactory.Commit(
            variables: EvidenceFactory.Variables(("unset", EvidenceFactory.AbsentValue()))));

        AssertWithheld("unset", ExecutionEvidenceValueDisposition.Absent);
    }

    [Fact]
    public async Task A_sensitive_value_is_recorded_with_the_sensitive_disposition_and_no_value()
    {
        await EnrichAsync(EvidenceFactory.Commit(
            variables: EvidenceFactory.Variables(("apiKey", EvidenceFactory.Secret("s3cret")))));

        AssertWithheld("apiKey", ExecutionEvidenceValueDisposition.Sensitive);
    }

    [Fact]
    public async Task Rotating_a_sensitive_value_still_registers_as_a_write()
    {
        // Nothing about the two records differs on the wire — the value is withheld both times — so if the capture
        // did not carry a value-derived comparand, rotating a secret would silently produce no evidence at all.
        await EnrichAsync(EvidenceFactory.Commit(
            checkpointId: "cp-1",
            variables: EvidenceFactory.Variables(("apiKey", EvidenceFactory.Secret("s3cret")))));
        await EnrichAsync(EvidenceFactory.Commit(
            checkpointId: "cp-2",
            variables: EvidenceFactory.Variables(("apiKey", EvidenceFactory.Secret("rotated")))));

        var records = Records(ExecutionEvidenceKinds.VariableSet);

        Assert.Equal(2, records.Count);
        Assert.All(records, record => Assert.Equal(ExecutionEvidenceValueDisposition.Sensitive, record.ValueDisposition));
        Assert.All(records, record => Assert.Null(record.Value));
    }

    [Fact]
    public async Task An_unchanged_sensitive_value_is_not_recorded_again()
    {
        var variables = EvidenceFactory.Variables(("apiKey", EvidenceFactory.Secret("s3cret")));

        await EnrichAsync(EvidenceFactory.Commit(checkpointId: "cp-1", variables: variables));
        await EnrichAsync(EvidenceFactory.Commit(checkpointId: "cp-2", variables: variables));

        Assert.Single(Records(ExecutionEvidenceKinds.VariableSet));
    }

    [Fact]
    public async Task A_sensitive_value_is_captured_once_redaction_is_switched_off()
    {
        var enricher = NewEnricher(options => options.RedactSensitiveValues = false);

        await enricher.EnrichAsync(EvidenceFactory.Commit(
            variables: EvidenceFactory.Variables(("apiKey", EvidenceFactory.Secret("s3cret")))));

        var record = Assert.Single(Records(ExecutionEvidenceKinds.VariableSet));
        Assert.Equal(ExecutionEvidenceValueDisposition.Captured, record.ValueDisposition);
        Assert.Equal("s3cret", record.Value?.GetString());
    }

    [Fact]
    public async Task A_value_moving_to_external_storage_is_recorded_as_an_external_write()
    {
        // The write that used to vanish: neither state carries an inline value the store could compare, so a
        // value-only diff saw "nothing then nothing" and emitted nothing.
        await EnrichAsync(EvidenceFactory.Commit(
            checkpointId: "cp-1",
            variables: EvidenceFactory.InlineVariables(("payload", "small"))));
        await EnrichAsync(EvidenceFactory.Commit(
            checkpointId: "cp-2",
            variables: EvidenceFactory.Variables(("payload", EvidenceFactory.External()))));

        var records = Records(ExecutionEvidenceKinds.VariableSet);

        Assert.Equal(2, records.Count);
        Assert.Equal(ExecutionEvidenceValueDisposition.Captured, records[0].ValueDisposition);
        Assert.Equal(ExecutionEvidenceValueDisposition.External, records[1].ValueDisposition);
        Assert.Null(records[1].Value);
    }

    [Fact]
    public async Task A_value_moving_to_an_external_locator_is_recorded_again()
    {
        await EnrichAsync(EvidenceFactory.Commit(
            checkpointId: "cp-1",
            variables: EvidenceFactory.Variables(("payload", EvidenceFactory.External(locator: "blob-1")))));
        await EnrichAsync(EvidenceFactory.Commit(
            checkpointId: "cp-2",
            variables: EvidenceFactory.Variables(("payload", EvidenceFactory.External(locator: "blob-2")))));

        Assert.Equal(2, Records(ExecutionEvidenceKinds.VariableSet).Count);
    }

    [Fact]
    public async Task A_value_moving_to_explicit_null_is_recorded_as_a_null_write()
    {
        await EnrichAsync(EvidenceFactory.Commit(
            checkpointId: "cp-1",
            variables: EvidenceFactory.InlineVariables(("greeting", "hello"))));
        await EnrichAsync(EvidenceFactory.Commit(
            checkpointId: "cp-2",
            variables: EvidenceFactory.Variables(("greeting", EvidenceFactory.NullValue()))));

        var records = Records(ExecutionEvidenceKinds.VariableSet);

        Assert.Equal(2, records.Count);
        Assert.Equal(ExecutionEvidenceValueDisposition.Null, records[1].ValueDisposition);
        Assert.Null(records[1].Value);
    }

    [Fact]
    public async Task A_value_longer_than_the_inline_cap_is_recorded_as_truncated_without_its_value()
    {
        var enricher = NewEnricher(options => options.MaxInlineValueLength = 4);

        await enricher.EnrichAsync(EvidenceFactory.Commit(
            variables: EvidenceFactory.InlineVariables(("essay", new string('x', 64)))));

        AssertWithheld("essay", ExecutionEvidenceValueDisposition.Truncated);
    }

    [Fact]
    public async Task Rewriting_a_truncated_value_beyond_the_cap_still_registers_as_a_write()
    {
        // RED — genuine src defect in ExecutionEvidenceCheckpointEnricher.Capture.
        //
        // The Truncated comparand is "Truncated:{length}:{first MaxInlineValueLength chars}", so two different values
        // of the same length that agree on their prefix compare equal and the write is dropped without a trace. That
        // contradicts ExecutionEvidenceVariableCapture.Comparand's own contract ("two captures represent the same
        // variable state exactly when their comparands are ordinally equal") and it is the one failure mode this
        // module must never have: a silent false negative, indistinguishable from "the workflow never wrote".
        //
        // At the 8192-char default it takes only an equal-length edit past the cap — a flipped id, a timestamp, a
        // same-width number deep in a large payload. The sibling withheld path already gets this right by hashing the
        // whole raw text; Truncated should do the same instead of slicing a prefix.
        var enricher = NewEnricher(options => options.MaxInlineValueLength = 4);

        await enricher.EnrichAsync(EvidenceFactory.Commit(
            checkpointId: "cp-1",
            variables: EvidenceFactory.InlineVariables(("payload", "aaaabbbbbb"))));
        await enricher.EnrichAsync(EvidenceFactory.Commit(
            checkpointId: "cp-2",
            variables: EvidenceFactory.InlineVariables(("payload", "aaaacccccc"))));

        Assert.Equal(2, Records(ExecutionEvidenceKinds.VariableSet).Count);
    }

    [Fact]
    public async Task Replayed_checkpoint_does_not_duplicate_records()
    {
        var commit = EvidenceFactory.Commit(
            variables: EvidenceFactory.InlineVariables(("greeting", "hello")),
            inspections: [EvidenceFactory.Inspection()]);

        await EnrichAsync(commit);
        await EnrichAsync(commit);

        Assert.Equal(3, Records().Count);
    }

    [Fact]
    public async Task Enriching_returns_the_commit_unchanged()
    {
        var commit = EvidenceFactory.Commit();

        var result = await _enricher.EnrichAsync(commit);

        Assert.Same(commit, result);
    }

    [Fact]
    public async Task A_null_commit_is_a_caller_defect_and_is_not_swallowed()
    {
        // Deliberately outside the capture try: swallowing it would hand the committer a null commit back.
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await _enricher.EnrichAsync(null!));
    }

    [Fact]
    public async Task A_commit_the_mapper_cannot_project_neither_throws_nor_records_anything()
    {
        // A null workflow execution id reaches the store as a null dictionary key: the collector must absorb its own
        // defects rather than fault the workflow it is observing, and must say so in the log.
        var commit = new RuntimeCheckpointCommit(
            CommitId: "commit-degenerate",
            Checkpoint: new RuntimeCheckpoint("cp-degenerate", null!, null!, EvidenceFactory.Now, [], new Dictionary<string, string>()),
            StateChanges: new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], []),
            PostCommitIntents: [],
            Metadata: new Dictionary<string, string>());

        var result = await _enricher.EnrichAsync(commit);

        Assert.Same(commit, result);
        // Asserted against the key the commit actually carried, not against an unrelated workflow id.
        Assert.Empty(_store.List(commit.Checkpoint.WorkflowExecutionId, 0).Records);
        Assert.Single(_logger.Warnings);
    }

    [Fact]
    public async Task Enricher_absorbs_a_store_failure_and_logs_it_as_a_warning()
    {
        var enricher = new ExecutionEvidenceCheckpointEnricher(
            new ThrowingStore(() => new InvalidOperationException("Collector defect.")),
            logger: _logger);
        var commit = EvidenceFactory.Commit();

        var result = await enricher.EnrichAsync(commit);

        Assert.Same(commit, result);
        var warning = Assert.Single(_logger.Warnings);
        Assert.IsType<InvalidOperationException>(warning);
    }

    [Fact]
    public async Task Cancellation_during_shutdown_is_dropped_silently()
    {
        var enricher = new ExecutionEvidenceCheckpointEnricher(
            new ThrowingStore(() => new OperationCanceledException()),
            logger: _logger);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var result = await enricher.EnrichAsync(EvidenceFactory.Commit(), cancellation.Token);

        Assert.NotNull(result);
        Assert.Empty(_logger.Warnings);
    }

    [Fact]
    public async Task Cancellation_without_a_cancelled_token_is_a_defect_and_is_logged()
    {
        var enricher = new ExecutionEvidenceCheckpointEnricher(
            new ThrowingStore(() => new OperationCanceledException()),
            logger: _logger);

        var result = await enricher.EnrichAsync(EvidenceFactory.Commit());

        Assert.NotNull(result);
        Assert.Single(_logger.Warnings);
    }

    [Theory]
    [InlineData(WorkflowExecutionStatus.Completed)]
    [InlineData(WorkflowExecutionStatus.Faulted)]
    [InlineData(WorkflowExecutionStatus.Cancelled)]
    public async Task Terminal_workflow_status_marks_the_page_terminal(WorkflowExecutionStatus status)
    {
        await EnrichAsync(EvidenceFactory.Commit(status: status));

        Assert.True(Page().Terminal);
    }

    [Theory]
    [InlineData(WorkflowExecutionStatus.Running)]
    [InlineData(WorkflowExecutionStatus.Suspended)]
    public async Task Non_terminal_workflow_status_leaves_the_page_open(WorkflowExecutionStatus status)
    {
        await EnrichAsync(EvidenceFactory.Commit(status: status));

        Assert.False(Page().Terminal);
    }

    [Fact]
    public async Task Correlation_id_travels_from_the_workflow_state_onto_every_record()
    {
        await EnrichAsync(EvidenceFactory.Commit(
            correlationId: "corr-1",
            variables: EvidenceFactory.InlineVariables(("greeting", "hello")),
            inspections: [EvidenceFactory.Inspection()]));

        Assert.All(Records(), record => Assert.Equal("corr-1", record.CorrelationId));
    }

    [Fact]
    public async Task A_commit_without_workflow_state_still_records_the_lifecycle_fact()
    {
        await EnrichAsync(EvidenceFactory.Commit(
            name: RuntimeCheckpointNames.ActivityScheduled,
            withWorkflowExecution: false));

        var record = Assert.Single(Records());
        Assert.Equal(RuntimeCheckpointNames.ActivityScheduled, record.Kind);
        Assert.Null(record.Status);
    }

    private async Task EnrichAsync(RuntimeCheckpointCommit commit) => await _enricher.EnrichAsync(commit);

    private ExecutionEvidenceCheckpointEnricher NewEnricher(Action<ExecutionEvidenceOptions> configure)
    {
        var options = new ExecutionEvidenceOptions();
        configure(options);
        return new(_store, options, _logger);
    }

    private void AssertWithheld(string name, ExecutionEvidenceValueDisposition disposition)
    {
        var record = Assert.Single(Records(ExecutionEvidenceKinds.VariableSet));
        Assert.Equal(name, record.Name);
        Assert.Equal(disposition, record.ValueDisposition);
        Assert.Null(record.Value);
    }

    private ExecutionEvidencePage Page() => _store.List(EvidenceFactory.WorkflowId, 0);

    private IReadOnlyList<ExecutionEvidenceRecord> Records(string? kind = null)
    {
        var records = Page().Records;
        return kind is null ? records : records.Where(record => record.Kind == kind).ToArray();
    }

    private sealed class ThrowingStore(Func<Exception> exception) : IExecutionEvidenceStore
    {
        public void Append(ExecutionEvidenceCommitBatch batch) => throw exception();

        public ExecutionEvidencePage List(string workflowExecutionId, long afterSequence) => throw new NotSupportedException();

        public ExecutionEvidencePage ListByCorrelation(string correlationId, long afterSequence) => throw new NotSupportedException();

        public void Clear(string? workflowExecutionId) => throw new NotSupportedException();
    }

    /// <summary>
    /// A capture failure must be visible: its only other symptom is a downstream test failing on missing evidence,
    /// which reads as a workflow bug rather than a collector bug.
    /// </summary>
    private sealed class CapturingLogger : ILogger<ExecutionEvidenceCheckpointEnricher>
    {
        public List<Exception?> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
                Warnings.Add(exception);
        }
    }
}
