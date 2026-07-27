using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Services.Alterations;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests.Alterations;

public sealed class WorkflowAlterationJobExecutorTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExecuteAsync_PreflightsEveryAlterationBeforeOneSuccessfulCheckpoint()
    {
        var first = new RecordingHandler("Contoso.First", stageValue: "first");
        var second = new RecordingHandler("Contoso.Second", stageValue: "second");
        var checkpointWriter = new RecordingCheckpointWriter();
        var executor = new WorkflowAlterationJobExecutor(new DictionaryHandlerResolver(first, second), checkpointWriter, TimeProvider.System);

        var result = await executor.ExecuteAsync(NewRequest("job-1", first.Envelope, second.Envelope));

        Assert.Equal(WorkflowAlterationJobStatus.Succeeded, result.Job.Status);
        Assert.Equal(["first", "second"], checkpointWriter.AppliedValues);
        Assert.Equal(1, checkpointWriter.CommitCount);
        Assert.Equal([WorkflowAlterationOutcomeStatus.Succeeded, WorkflowAlterationOutcomeStatus.Succeeded], result.Job.Outcomes.Select(outcome => outcome.Status));
        Assert.Equal([0, 1], result.Job.Outcomes.Select(outcome => outcome.Ordinal));
        Assert.Equal(1, first.PreflightCount);
        Assert.Equal(1, second.PreflightCount);
    }

    [Fact]
    public async Task ExecuteAsync_OnPreflightFailure_DiscardsEveryStagedMutationAndRecordsOrderedSafeOutcomes()
    {
        var first = new RecordingHandler("Contoso.First", stageValue: "first");
        var failing = new RecordingHandler("Contoso.Failing", failureCode: "FakePreflightRejected");
        var later = new RecordingHandler("Contoso.Later", stageValue: "later");
        var checkpointWriter = new RecordingCheckpointWriter();
        var executor = new WorkflowAlterationJobExecutor(new DictionaryHandlerResolver(first, failing, later), checkpointWriter, TimeProvider.System);

        var result = await executor.ExecuteAsync(NewRequest("job-1", first.Envelope, failing.Envelope, later.Envelope));

        Assert.Equal(WorkflowAlterationJobStatus.Failed, result.Job.Status);
        Assert.Empty(checkpointWriter.AppliedValues);
        Assert.Equal(1, checkpointWriter.CommitCount);
        Assert.Equal(
            [WorkflowAlterationOutcomeStatus.Skipped, WorkflowAlterationOutcomeStatus.Failed, WorkflowAlterationOutcomeStatus.Skipped],
            result.Job.Outcomes.Select(outcome => outcome.Status));
        Assert.Equal("NotAppliedDueToPreflightFailure", result.Job.Outcomes[0].Code);
        Assert.Equal("FakePreflightRejected", result.Job.Outcomes[1].Code);
        Assert.Equal("SkippedAfterPreflightFailure", result.Job.Outcomes[2].Code);
        Assert.Equal(1, first.PreflightCount);
        Assert.Equal(1, failing.PreflightCount);
        Assert.Equal(0, later.PreflightCount);
    }

    [Fact]
    public async Task ExecuteAsync_KeepsTargetsIndependentAndRejectsAmbiguousHandlerResolutionSafely()
    {
        var first = new RecordingHandler("Contoso.First", stageValue: "first");
        var checkpointWriter = new RecordingCheckpointWriter();
        var executor = new WorkflowAlterationJobExecutor(new DictionaryHandlerResolver(first), checkpointWriter, TimeProvider.System);

        var firstResult = await executor.ExecuteAsync(NewRequest("job-1", first.Envelope));
        var secondResult = await executor.ExecuteAsync(NewRequest("job-2", first.Envelope));

        Assert.Equal(WorkflowAlterationJobStatus.Succeeded, firstResult.Job.Status);
        Assert.Equal(WorkflowAlterationJobStatus.Succeeded, secondResult.Job.Status);
        Assert.Equal(["first", "first"], checkpointWriter.AppliedValues);

        var conflicting = new WorkflowAlterationJobExecutor(new ThrowingResolver(), checkpointWriter, TimeProvider.System);
        var failed = await conflicting.ExecuteAsync(NewRequest("job-3", first.Envelope));

        Assert.Equal(WorkflowAlterationJobStatus.Failed, failed.Job.Status);
        Assert.Equal("HandlerResolutionFailed", Assert.Single(failed.Job.Outcomes).Code);
        Assert.Equal(3, checkpointWriter.CommitCount);
    }

    [Fact]
    public async Task ExecuteAsync_ReconcilesLostCommitAcknowledgementWithoutASecondMutation()
    {
        var handler = new RecordingHandler("Contoso.First", stageValue: "first");
        var checkpointWriter = new RecordingCheckpointWriter(loseAcknowledgement: true);
        var executor = new WorkflowAlterationJobExecutor(new DictionaryHandlerResolver(handler), checkpointWriter, TimeProvider.System);

        var result = await executor.ExecuteAsync(NewRequest("job-1", handler.Envelope));

        Assert.True(result.ReconciledAfterAcknowledgementLoss);
        Assert.Equal(WorkflowAlterationJobStatus.Succeeded, result.Job.Status);
        Assert.Equal(["first"], checkpointWriter.AppliedValues);
        Assert.Equal(1, checkpointWriter.CommitCount);
    }

    private static WorkflowAlterationJobExecutionRequest NewRequest(string jobId, params WorkflowAlterationEnvelope[] envelopes)
    {
        var job = new WorkflowAlterationJobState(
            jobId,
            "plan-1",
            $"workflow-{jobId}",
            "tenant-a",
            0,
            WorkflowAlterationJobStatus.Running,
            new WorkflowAlterationJobClaim("worker-1", "claim-1", Now.AddMinutes(1)),
            1,
            [],
            null,
            null,
            Now,
            Now,
            null,
            0);

        return new WorkflowAlterationJobExecutionRequest(job, envelopes, new WorkflowAlterationProjectedState());
    }

    private sealed class RecordingHandler : IWorkflowAlterationPreflightHandler
    {
        private readonly string? _failureCode;
        private readonly string? _stageValue;

        public RecordingHandler(string kind, string? stageValue = null, string? failureCode = null)
        {
            Envelope = new WorkflowAlterationEnvelope(kind, 1, JsonSerializer.SerializeToElement(new { }));
            _stageValue = stageValue;
            _failureCode = failureCode;
        }

        public WorkflowAlterationEnvelope Envelope { get; }
        public int PreflightCount { get; private set; }

        public ValueTask<WorkflowAlterationPreflightResult> PreflightAsync(WorkflowAlterationPreflightContext context, CancellationToken cancellationToken = default)
        {
            PreflightCount++;
            if (_failureCode is not null)
                return ValueTask.FromResult(WorkflowAlterationPreflightResult.Rejected(_failureCode, "Rejected by the fake handler."));

            context.Staging.Stage(new TestStagedChange(_stageValue!));
            return ValueTask.FromResult(WorkflowAlterationPreflightResult.Accepted());
        }
    }

    private sealed class DictionaryHandlerResolver(params IWorkflowAlterationPreflightHandler[] handlers) : IWorkflowAlterationHandlerResolver
    {
        private readonly IReadOnlyDictionary<(string Kind, int Version), IWorkflowAlterationPreflightHandler> _handlers = handlers.ToDictionary(
            handler => ((RecordingHandler)handler).Envelope is var envelope ? (envelope.Kind, envelope.SchemaVersion) : throw new InvalidOperationException(),
            handler => handler);

        public IWorkflowAlterationPreflightHandler Resolve(WorkflowAlterationEnvelope envelope) =>
            _handlers.TryGetValue((envelope.Kind, envelope.SchemaVersion), out var handler)
                ? handler
                : throw new InvalidOperationException("No exact handler was registered.");
    }

    private sealed class ThrowingResolver : IWorkflowAlterationHandlerResolver
    {
        public IWorkflowAlterationPreflightHandler Resolve(WorkflowAlterationEnvelope envelope) =>
            throw new InvalidOperationException("Multiple exact handlers were registered.");
    }

    private sealed record TestStagedChange(string Value) : IWorkflowAlterationStagedChange
    {
        public string ChangeKey => Value;
    }

    private sealed class RecordingCheckpointWriter(bool loseAcknowledgement = false) : IWorkflowAlterationCheckpointWriter
    {
        private readonly Dictionary<string, WorkflowAlterationCheckpointWriteResult> _committed = new(StringComparer.Ordinal);
        private bool _loseAcknowledgement = loseAcknowledgement;

        public List<string> AppliedValues { get; } = [];
        public int CommitCount { get; private set; }

        public ValueTask<WorkflowAlterationCheckpointWriteResult> CommitAsync(WorkflowAlterationCheckpointWriteRequest request, CancellationToken cancellationToken = default)
        {
            CommitCount++;
            foreach (var change in request.StagedChanges.OfType<TestStagedChange>())
                AppliedValues.Add(change.Value);

            var result = new WorkflowAlterationCheckpointWriteResult(request.TerminalJobChange.CheckpointCommitId, request.TerminalJobChange);
            _committed.Add(result.CheckpointCommitId, result);
            if (_loseAcknowledgement)
            {
                _loseAcknowledgement = false;
                throw new WorkflowAlterationCheckpointAcknowledgementLostException(result.CheckpointCommitId);
            }

            return ValueTask.FromResult(result);
        }

        public ValueTask<WorkflowAlterationCheckpointWriteResult?> FindCommittedAsync(string checkpointCommitId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_committed.GetValueOrDefault(checkpointCommitId));
    }
}
