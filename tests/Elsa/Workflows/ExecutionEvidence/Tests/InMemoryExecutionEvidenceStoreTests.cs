using System.Collections;
using Elsa.Workflows.ExecutionEvidence.Models;
using Elsa.Workflows.ExecutionEvidence.Services;

namespace Elsa.Workflows.ExecutionEvidence.Tests;

public sealed class InMemoryExecutionEvidenceStoreTests
{
    private const string OtherWorkflowId = "wf-2";

    private readonly InMemoryExecutionEvidenceStore _store = new();

    [Fact]
    public void Sequences_are_strictly_increasing_per_workflow_execution()
    {
        Append(_store, "cp-1");
        Append(_store, "cp-2");
        Append(_store, "cp-3", workflowExecutionId: OtherWorkflowId);

        Assert.Equal([1, 2], Sequences(_store.List(EvidenceFactory.WorkflowId, 0)));
        Assert.Equal([1], Sequences(_store.List(OtherWorkflowId, 0)));
    }

    [Fact]
    public void List_treats_the_cursor_as_exclusive()
    {
        Append(_store, "cp-1");
        Append(_store, "cp-2");
        Append(_store, "cp-3");

        var page = _store.List(EvidenceFactory.WorkflowId, 1);

        Assert.Equal([2, 3], Sequences(page));
        Assert.Equal(3, page.LastSequence);
    }

    [Fact]
    public void A_page_for_a_known_workflow_reports_one_matched_workflow_and_no_gap()
    {
        Append(_store, "cp-1");
        Append(_store, "cp-2");

        var page = _store.List(EvidenceFactory.WorkflowId, 0);

        Assert.Equal(1, page.MatchedWorkflows);
        // Nothing was evicted, so the lowest retained sequence is exactly the one following the cursor.
        Assert.Equal(1, page.FirstSequence);
    }

    [Fact]
    public void List_of_an_unknown_workflow_echoes_the_cursor_on_an_empty_page()
    {
        var page = _store.List("missing", 7);

        Assert.Empty(page.Records);
        Assert.Equal(7, page.LastSequence);
        Assert.Equal(0, page.FirstSequence);
        Assert.Equal(0, page.MatchedWorkflows);
        Assert.False(page.Terminal);
    }

    [Fact]
    public void ListByCorrelation_spans_workflow_executions_sharing_a_correlation_id()
    {
        Append(_store, "cp-1", correlationId: "corr-1");
        Append(_store, "cp-2", workflowExecutionId: OtherWorkflowId, correlationId: "corr-1");
        Append(_store, "cp-3", workflowExecutionId: "wf-3", correlationId: "corr-2");

        var page = _store.ListByCorrelation("corr-1", 0);

        Assert.Equal(
            [EvidenceFactory.WorkflowId, OtherWorkflowId],
            page.Records.Select(record => record.WorkflowExecutionId));
        // The fan-out count is what tells a caller the child it expected actually showed up.
        Assert.Equal(2, page.MatchedWorkflows);
    }

    [Fact]
    public void ListByCorrelation_applies_the_cursor_to_every_matched_workflow_execution()
    {
        // Sequences are per workflow execution, so a correlated cursor is applied independently to each match rather
        // than to one global order — a caller resuming at 1 skips the first fact of BOTH workflows.
        Append(_store, "cp-1", correlationId: "corr-1");
        Append(_store, "cp-2", correlationId: "corr-1");
        Append(_store, "cp-3", workflowExecutionId: OtherWorkflowId, correlationId: "corr-1");
        Append(_store, "cp-4", workflowExecutionId: OtherWorkflowId, correlationId: "corr-1");

        var page = _store.ListByCorrelation("corr-1", 1);

        Assert.Equal(
            [(EvidenceFactory.WorkflowId, 2L), (OtherWorkflowId, 2L)],
            page.Records.Select(record => (record.WorkflowExecutionId, record.Sequence)));
        Assert.Equal(2, page.LastSequence);
    }

    [Fact]
    public void ListByCorrelation_of_an_unknown_correlation_echoes_the_cursor_on_an_empty_page()
    {
        var page = _store.ListByCorrelation("missing", 4);

        Assert.Empty(page.Records);
        Assert.Equal(4, page.LastSequence);
        Assert.Equal(0, page.MatchedWorkflows);
    }

    [Fact]
    public void ListByCorrelation_is_terminal_only_once_every_matched_workflow_is()
    {
        Append(_store, "cp-1", correlationId: "corr-1", terminal: true);
        Append(_store, "cp-2", workflowExecutionId: OtherWorkflowId, correlationId: "corr-1");

        Assert.False(_store.ListByCorrelation("corr-1", 0).Terminal);

        Append(_store, "cp-3", workflowExecutionId: OtherWorkflowId, correlationId: "corr-1", terminal: true);

        Assert.True(_store.ListByCorrelation("corr-1", 0).Terminal);
    }

    [Fact]
    public void Clear_with_an_id_drops_only_that_workflow_execution()
    {
        Append(_store, "cp-1");
        Append(_store, "cp-2", workflowExecutionId: OtherWorkflowId);

        _store.Clear(EvidenceFactory.WorkflowId);

        Assert.Empty(_store.List(EvidenceFactory.WorkflowId, 0).Records);
        Assert.NotEmpty(_store.List(OtherWorkflowId, 0).Records);
    }

    [Fact]
    public void Clear_without_an_id_drops_every_workflow_execution()
    {
        Append(_store, "cp-1");
        Append(_store, "cp-2", workflowExecutionId: OtherWorkflowId);

        _store.Clear(null);

        Assert.Empty(_store.List(EvidenceFactory.WorkflowId, 0).Records);
        Assert.Empty(_store.List(OtherWorkflowId, 0).Records);
    }

    [Fact]
    public void A_replayed_checkpoint_is_ignored_whole()
    {
        Append(_store, "cp-1");
        Append(_store, "cp-1");

        Assert.Single(_store.List(EvidenceFactory.WorkflowId, 0).Records);
    }

    [Fact]
    public void A_replayed_checkpoint_older_than_the_dedupe_window_is_recorded_twice()
    {
        // The accepted trade-off for bounding the dedupe set: a workflow that loops for longer than the window can
        // have an old checkpoint replayed and recorded a second time. Recent replays — the ones a retry actually
        // produces — are still absorbed.
        var store = NewStore(options => options.MaxTrackedCheckpointsPerWorkflow = 2);

        Append(store, "cp-1");
        Append(store, "cp-2");
        Append(store, "cp-3");
        Append(store, "cp-1");
        Append(store, "cp-3");

        Assert.Equal(
            ["cp-1", "cp-2", "cp-3", "cp-1"],
            store.List(EvidenceFactory.WorkflowId, 0).Records.Select(record => record.CheckpointId));
    }

    [Fact]
    public void Records_beyond_the_per_workflow_cap_drop_the_oldest_and_report_the_gap_through_FirstSequence()
    {
        var store = NewStore(options => options.MaxRecordsPerWorkflow = 3);

        for (var index = 1; index <= 6; index++)
            Append(store, $"cp-{index}");

        var page = store.List(EvidenceFactory.WorkflowId, 0);

        Assert.Equal([4, 5, 6], Sequences(page));
        // The dropped evidence is what makes this cap dangerous, so the page has to say it happened: reading from
        // cursor 0 and getting a first sequence of 4 is the signal that sequences 1-3 are gone for good.
        Assert.Equal(4, page.FirstSequence);
        Assert.True(page.FirstSequence > 1, "A caller reading from cursor 0 must be able to detect the eviction gap.");
    }

    [Fact]
    public void Workflow_executions_beyond_the_cap_evict_the_least_recently_appended()
    {
        var store = NewStore(options => options.MaxWorkflows = 2);

        Append(store, "cp-1", workflowExecutionId: "wf-a");
        Append(store, "cp-2", workflowExecutionId: "wf-b");
        // Touching wf-a makes wf-b the least recently appended, so wf-b is the one wf-c displaces.
        Append(store, "cp-3", workflowExecutionId: "wf-a");
        Append(store, "cp-4", workflowExecutionId: "wf-c");

        Assert.NotEmpty(store.List("wf-a", 0).Records);
        Assert.Empty(store.List("wf-b", 0).Records);
        Assert.NotEmpty(store.List("wf-c", 0).Records);
    }

    [Fact]
    public void A_new_root_variable_is_recorded_with_the_captured_value_the_enricher_handed_over()
    {
        Append(_store, "cp-1", rootVariables: EvidenceFactory.CapturedVariables(("greeting", "hello")));

        var record = Assert.Single(VariableSets(_store));
        Assert.Equal("greeting", record.Name);
        Assert.Equal("hello", record.Value?.GetString());
        Assert.Equal(ExecutionEvidenceValueDisposition.Captured, record.ValueDisposition);
    }

    [Fact]
    public void An_unchanged_root_variable_is_not_recorded_again()
    {
        Append(_store, "cp-1", rootVariables: EvidenceFactory.CapturedVariables(("greeting", "hello")));
        Append(_store, "cp-2", rootVariables: EvidenceFactory.CapturedVariables(("greeting", "hello")));

        Assert.Single(VariableSets(_store));
    }

    [Fact]
    public void A_valueless_capture_whose_comparand_changed_is_still_recorded_as_a_write()
    {
        // The store diffs comparands, never values. That is the whole reason a withheld value can still register a
        // write: neither of these captures carries anything to compare.
        Append(
            _store,
            "cp-1",
            rootVariables: EvidenceFactory.Captures(
                ("secret", EvidenceFactory.Withheld(ExecutionEvidenceValueDisposition.Sensitive, "first"))));
        Append(
            _store,
            "cp-2",
            rootVariables: EvidenceFactory.Captures(
                ("secret", EvidenceFactory.Withheld(ExecutionEvidenceValueDisposition.Sensitive, "second"))));

        Assert.Equal(2, VariableSets(_store).Count);
        Assert.All(
            VariableSets(_store),
            record =>
            {
                Assert.Null(record.Value);
                Assert.Equal(ExecutionEvidenceValueDisposition.Sensitive, record.ValueDisposition);
            });
    }

    [Fact]
    public void A_batch_that_fails_mid_projection_leaves_the_buffer_untouched()
    {
        Append(_store, "cp-1", rootVariables: EvidenceFactory.CapturedVariables(("greeting", "hello")));

        Assert.Throws<InvalidOperationException>(() => _store.Append(EvidenceFactory.Batch(
            "cp-2",
            records: new ExplodingRecords(EvidenceFactory.Record()),
            rootVariables: EvidenceFactory.CapturedVariables(("greeting", "goodbye")))));

        Assert.Single(_store.List(EvidenceFactory.WorkflowId, 0).Records);

        // Re-appending cp-2 proves both halves of atomicity: its lifecycle record lands, so the checkpoint was never
        // marked seen; and the unchanged variable emits nothing, so the snapshot was never advanced to "goodbye" —
        // had it been, this would emit a phantom change back to "hello".
        _store.Append(EvidenceFactory.Batch(
            "cp-2",
            records: [EvidenceFactory.Record(checkpointId: "cp-2")],
            rootVariables: EvidenceFactory.CapturedVariables(("greeting", "hello"))));

        Assert.Equal(2, _store.List(EvidenceFactory.WorkflowId, 0).Records.Count);
        Assert.Single(VariableSets(_store));
    }

    [Fact]
    public async Task Concurrent_appends_and_reads_keep_per_workflow_sequences_strictly_increasing()
    {
        // IExecutionEvidenceStore promises it is safe for concurrent capture and query; a QA collector is written to
        // from whichever thread committed the checkpoint while a test polls it from another.
        const int workflowCount = 4;
        const int checkpointsPerWorkflow = 250;
        var workflowIds = Enumerable.Range(0, workflowCount).Select(index => $"wf-concurrent-{index}").ToArray();
        using var readersDone = new CancellationTokenSource();

        // Materialized eagerly: a deferred LINQ sequence would only start the readers once the writers had finished.
        var writers = workflowIds.Select(workflowId => Task.Run(() =>
        {
            for (var index = 1; index <= checkpointsPerWorkflow; index++)
                Append(_store, $"cp-{index}", workflowExecutionId: workflowId);
        })).ToArray();

        var readers = workflowIds.Select(workflowId => Task.Run(() =>
        {
            while (!readersDone.Token.IsCancellationRequested)
                AssertStrictlyIncreasing(_store.List(workflowId, 0));
        })).ToArray();

        try
        {
            await Task.WhenAll(writers);
        }
        finally
        {
            await readersDone.CancelAsync();
        }

        await Task.WhenAll(readers);

        foreach (var workflowId in workflowIds)
        {
            var page = _store.List(workflowId, 0);
            Assert.Equal(Enumerable.Range(1, checkpointsPerWorkflow).Select(sequence => (long)sequence), Sequences(page));
        }
    }

    private static void AssertStrictlyIncreasing(ExecutionEvidencePage page)
    {
        var previous = 0L;

        foreach (var record in page.Records)
        {
            Assert.True(record.Sequence > previous, "Sequences must be strictly increasing within a workflow execution.");
            previous = record.Sequence;
        }
    }

    private static InMemoryExecutionEvidenceStore NewStore(Action<ExecutionEvidenceOptions> configure)
    {
        var options = new ExecutionEvidenceOptions();
        configure(options);
        return new(options);
    }

    private static void Append(
        InMemoryExecutionEvidenceStore store,
        string checkpointId,
        string workflowExecutionId = EvidenceFactory.WorkflowId,
        string? correlationId = null,
        bool terminal = false,
        IReadOnlyDictionary<string, ExecutionEvidenceVariableCapture>? rootVariables = null) =>
        store.Append(EvidenceFactory.Batch(
            checkpointId,
            workflowExecutionId,
            correlationId,
            terminal,
            // A variable-diff case wants only the derived VariableSet facts, so it contributes no explicit records.
            records: rootVariables is null ? null : [],
            rootVariables: rootVariables));

    private static IEnumerable<long> Sequences(ExecutionEvidencePage page) =>
        page.Records.Select(record => record.Sequence);

    private static IReadOnlyList<ExecutionEvidenceRecord> VariableSets(InMemoryExecutionEvidenceStore store) =>
        store.List(EvidenceFactory.WorkflowId, 0).Records
            .Where(record => record.Kind == ExecutionEvidenceKinds.VariableSet)
            .ToArray();

    /// <summary>A record list that fails part-way through enumeration, standing in for any mid-batch projection defect.</summary>
    private sealed class ExplodingRecords(ExecutionEvidenceRecord first) : IReadOnlyList<ExecutionEvidenceRecord>
    {
        public int Count => 2;

        public ExecutionEvidenceRecord this[int index] =>
            index == 0 ? first : throw new InvalidOperationException("Projection defect.");

        public IEnumerator<ExecutionEvidenceRecord> GetEnumerator()
        {
            yield return first;
            throw new InvalidOperationException("Projection defect.");
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
