using System.Text.Json;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed class OutcomeHistoryCaptureTests
{
    [Theory]
    [InlineData(false, "short")]
    [InlineData(true, "short")]
    [InlineData(false, "231-ascii")]
    [InlineData(true, "231-ascii")]
    [InlineData(false, "2045-ascii")]
    [InlineData(true, "2045-ascii")]
    [InlineData(false, "escaped-unicode")]
    [InlineData(true, "escaped-unicode")]
    [Trait("Feature", "RunHistory")]
    public async Task Actual_selected_outcome_is_unchanged_by_bounded_history_metadata(bool capture, string example)
    {
        var recorder = new HistoryCheckpointRecorder();
        var outcome = example switch
        {
            "short" => "Done",
            "231-ascii" => new string('a', 231),
            "2045-ascii" => new string('a', 2045),
            "escaped-unicode" => string.Concat(Enumerable.Repeat("\u03bb\"", 300)),
            _ => throw new ArgumentOutOfRangeException(nameof(example))
        };
        await using var host = WorkflowExecutionHarness.Create().ConfigureServices(services =>
        {
            services.AddSingleton<IRuntimeCheckpointCommitEnricher>(recorder);
            if (capture) services.AddRuntimeHistory();
            services.Configure<RuntimeRecoveryContinuationOptions>(options => options.SigningKey = "outcome-history-capture-at-least32bytes");
        }).Build("outcome-owner");

        var published = await host.PublishAsync(WorkflowExecutionHarness.NewExecutable(WorkflowExecutionHarness.NewProbeNode("node", [outcome])), "outcome-source");
        await host.StartPublishedAsync(published, host.ExecutionId, partition: new(PersistenceScope.DefaultValue));
        var run = new WorkflowExecutionRun(await host.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(host.ExecutionId),
            await host.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(host.ExecutionId));
        Assert.Empty(await host.Services.GetRequiredService<IWorkflowSchedulerWorkQueue>().ListAllAsync(new RuntimeSchedulerWorkQuery(host.ExecutionId)));
        Assert.Equal(PersistenceScope.DefaultValue, host.Services.GetRequiredService<IPersistenceAccessContextAccessor>().Current.RequireScope().Value);
        Assert.Equal(PersistenceScope.DefaultValue, run.WorkflowState!.Partition!.Value);

        run.AssertWorkflowCompleted();
        var owner = run.AssertOutcomes("node", outcome);
        var writer = host.Services.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>();
        var completion = Assert.Single(recorder.Commits, commit => commit.Checkpoint.Name == RuntimeCheckpointNames.ActivityCompleted &&
            commit.StateChanges.ActivityExecutions.Any(change => change.StateId == owner.Execution.ActivityExecutionId &&
                change.State.Status == ActivityExecutionStatus.Completed));
        if (!capture)
        {
            Assert.Null(run.WorkflowState!.HistoryCapturePolicy);
            Assert.False(run.WorkflowState.HistoryCaptureEnabledAtAdmission);
            Assert.Null(completion.HistoryBatches);
            Assert.All(writer.ListCommits(), record => Assert.Null(record.Commit.HistoryBatches));
            return;
        }

        Assert.NotNull(run.WorkflowState!.HistoryCapturePolicy);
        Assert.True(run.WorkflowState.HistoryCapturePolicy.Required);
        Assert.True(run.WorkflowState.HistoryCaptureEnabledAtAdmission);
        var fact = Assert.Single(completion.HistorySourceFacts!, item => item.Kind == RuntimeHistorySourceFactKind.ActivityContinuationCompleted);
        Assert.Equal(new[] { outcome }, fact.OutcomeNames);
        var batch = Assert.Single(completion.HistoryBatches!);
        Assert.Equal(RuntimeCheckpointCommitFingerprint.Compute(completion with { HistoryBatches = null }), batch.SourceFingerprint);
        var physical = Assert.Single(writer.ListCommits(), record => record.Commit.HistoryBatches?.Any(item => item.LogicalCheckpointId == batch.LogicalCheckpointId) == true).Commit;
        var retainedBatch = Assert.Single(physical.HistoryBatches!, item => item.LogicalCheckpointId == batch.LogicalCheckpointId);
        Assert.Equal(batch.SourceFingerprint, retainedBatch.SourceFingerprint);
        Assert.Equal(batch.Candidates.Select(item => item.Event.HistoryEventId), retainedBatch.Candidates.Select(item => item.Event.HistoryEventId));
        var reader = host.Services.GetRequiredService<IRuntimeHistoryStore>();
        var page = await reader.ReadPageAsync(new(host.ExecutionId, 100));
        Assert.Null(page.NextContinuationToken);
        var recorded = Assert.Single(page.Items, item => item.Name == "ActivityContinuationCompleted");
        Assert.Equal(owner.Execution.ActivityExecutionId, recorded.ActivityExecutionId);
        Assert.Equal(completion.Checkpoint.CheckpointId, recorded.CheckpointId);
        Assert.Equal(fact.AttemptId, recorded.AttemptId);
        Assert.Equal(fact.Causation, recorded.Causation);
        Assert.Equal(physical.CommitId, recorded.PhysicalCommitId);
        Assert.Equal(recorded.PhysicalCommitId, Assert.Single(page.Items, item => item.Name == "ActivityCompleted").PhysicalCommitId);
        var serialized = JsonSerializer.Serialize(new[] { outcome });
        if (serialized.Length <= 2048)
        {
            Assert.Equal(serialized, recorded.Metadata["history.outcomeNames"]);
            Assert.False(recorded.Metadata.ContainsKey("history.outcomeCaptureState"));
        }
        else
        {
            Assert.False(recorded.Metadata.ContainsKey("history.outcomeNames"));
            Assert.Equal("size-omitted", recorded.Metadata["history.outcomeCaptureState"]);
            Assert.Equal("1", recorded.Metadata["history.outcomeCount"]);
            Assert.Equal(RuntimeHistoryIdentity.CreateBinding([outcome]), recorded.Metadata["history.outcomeContentHash"]);
        }
        RuntimeHistorySafety.ValidateMetadata(recorded);
        var commitCount = writer.ListCommits().Count;
        var sequence = (await reader.FindHeadAsync(host.ExecutionId))!.LastSequence;

        await writer.CommitAsync(physical, new(RuntimeCheckpointPersistenceMode.Immediate));

        Assert.Equal(commitCount, writer.ListCommits().Count);
        Assert.Equal(sequence, (await reader.FindHeadAsync(host.ExecutionId))!.LastSequence);
        Assert.Equal(page.Items.Select(item => (item.HistoryEventId, item.Sequence)),
            (await reader.ReadPageAsync(new(host.ExecutionId, 100))).Items.Select(item => (item.HistoryEventId, item.Sequence)));
    }

}
