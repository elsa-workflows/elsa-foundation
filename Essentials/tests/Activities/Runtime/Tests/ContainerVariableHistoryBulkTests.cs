using System.Text.Json;
using Elsa.Activities.Sequence;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Activities.Runtime.Tests;

public sealed class ContainerVariableHistoryBulkTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [Trait("Feature", "RunHistory")]
    public async Task Actual_container_completion_retains_all_4097_variables_in_one_native_boundary(bool capture, bool childCompletion)
    {
        var recorder = new HistoryCheckpointRecorder();
        await using var host = WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesSequenceFeature().ConfigureServices(services))
            .ConfigureServices(services =>
            {
                services.AddSingleton<IRuntimeCheckpointCommitEnricher>(recorder);
                if (capture) services.AddRuntimeHistory();
                services.Configure<RuntimeRecoveryContinuationOptions>(options => options.SigningKey = "wide-container-history-at-least32bytes");
            }).Build("wide-container", "wide-child");
        var children = childCompletion ? new[] { WorkflowExecutionHarness.NewProbeNode("child") } : [];
        var variables = Enumerable.Range(0, 4097)
            .Select(index => RuntimeVariableDeclarationTestData.Create($"variable-{index}", $"Variable {index}", "Int32", index)).ToArray();
        var root = new ExecutableNode("container", "authored-container", typeof(SequenceActivity).FullName!, "1.0.0",
            typeof(ClrActivityDescriptor).FullName!, JsonSerializer.SerializeToElement(new ClrActivityDescriptor(typeof(SequenceActivity).FullName!)),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(SequenceActivity.ActivitiesSlotName, children)],
            structure: new(SequenceActivity.StructureKind, SequenceActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new { activities = children.Select(child => child.ExecutableNodeId).ToArray(), variables },
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))));

        var published = await host.PublishAsync(WorkflowExecutionHarness.NewExecutable(root), "wide-container-source");
        await host.StartPublishedAsync(published, host.ExecutionId, partition: new(PersistenceScope.DefaultValue));
        var run = new WorkflowExecutionRun(await host.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(host.ExecutionId),
            await host.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(host.ExecutionId));
        Assert.Empty(await host.Services.GetRequiredService<IWorkflowSchedulerWorkQueue>().ListAllAsync(new RuntimeSchedulerWorkQuery(host.ExecutionId)));
        Assert.Equal(PersistenceScope.DefaultValue, host.Services.GetRequiredService<IPersistenceAccessContextAccessor>().Current.RequireScope().Value);
        Assert.Equal(PersistenceScope.DefaultValue, run.WorkflowState!.Partition!.Value);

        run.AssertWorkflowCompleted();
        var owner = Assert.Single(run.States("container"));
        Assert.Equal(VariableFrameStatus.Closed, owner.VariableFrame!.Status);
        Assert.Equal(4097, owner.VariableFrame.Values.Count);
        Assert.All(Enumerable.Range(0, 4097), index => Assert.Equal(index, owner.VariableFrame.Values[$"variable-{index}"].InlineValue!.Value.GetInt32()));
        var inspection = (await host.Services.GetRequiredService<IActivityExecutionInspectionStore>()
            .FindAsync(host.ExecutionId, owner.Execution.ActivityExecutionId))!;
        var values = inspection.ValueSnapshots.Where(value => value.Subject == ActivityExecutionInspectionValueSubject.ContainerVariable).ToArray();
        Assert.Equal(4097, values.Length);
        Assert.Equal(variables.Select(variable => variable.Name).Order(), values.Select(value => value.Name).Order());
        var writer = host.Services.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>();
        var completion = Assert.Single(recorder.Commits, commit => commit.Checkpoint.Name == RuntimeCheckpointNames.ActivityCompleted && commit.StateChanges.ActivityExecutions.Any(change =>
            change.StateId == owner.Execution.ActivityExecutionId && change.State.Status == ActivityExecutionStatus.Completed));
        Assert.Equal(RuntimeCheckpointNames.ActivityCompleted, completion.Checkpoint.Name);
        var projection = Assert.Single(completion.StateChanges.ActivityExecutionInspections,
            change => change.StateId == owner.Execution.ActivityExecutionId).State;
        if (!capture)
        {
            Assert.Null(run.WorkflowState!.HistoryCapturePolicy);
            Assert.False(run.WorkflowState.HistoryCaptureEnabledAtAdmission);
            Assert.Null(projection.CheckpointValueCapture); Assert.Null(completion.HistoryBatches);
            Assert.All(writer.ListCommits(), record => Assert.Null(record.Commit.HistoryBatches));
            return;
        }

        Assert.NotNull(run.WorkflowState!.HistoryCapturePolicy);
        Assert.True(run.WorkflowState.HistoryCapturePolicy.Required);
        Assert.True(run.WorkflowState.HistoryCaptureEnabledAtAdmission);
        var evidenceIds = values.Select(value => value.EvidenceId!).ToArray();
        Assert.All(evidenceIds, id => Assert.False(string.IsNullOrWhiteSpace(id)));
        Assert.Equal(4097, evidenceIds.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(evidenceIds.Order(), projection.CheckpointValueCapture!.EvidenceIds.Order());
        var batch = Assert.Single(completion.HistoryBatches!);
        Assert.Equal(completion.Checkpoint.CheckpointId, batch.LogicalCheckpointId);
        Assert.Equal(RuntimeCheckpointCommitFingerprint.Compute(completion with { HistoryBatches = null }), batch.SourceFingerprint);
        Assert.Equal(4097, batch.Candidates.Count(candidate => candidate.Event.Name == "ValueCaptured"));
        Assert.Single(batch.Candidates, item => item.Event.Name == "ActivityCompleted" && item.Event.ActivityExecutionId == owner.Execution.ActivityExecutionId);
        var physical = Assert.Single(writer.ListCommits(), record => record.Commit.HistoryBatches?.Any(item => item.LogicalCheckpointId == batch.LogicalCheckpointId) == true).Commit;
        var retainedBatch = Assert.Single(physical.HistoryBatches!, item => item.LogicalCheckpointId == batch.LogicalCheckpointId);
        Assert.Equal(batch.SourceFingerprint, retainedBatch.SourceFingerprint);
        Assert.Equal(batch.Candidates.Select(item => item.Event.HistoryEventId), retainedBatch.Candidates.Select(item => item.Event.HistoryEventId));
        var reader = host.Services.GetRequiredService<IRuntimeHistoryStore>();
        var before = await ReadValuesAsync();
        Assert.Equal(4097, before.Count);
        Assert.Equal(batch.Candidates.Where(item => item.Event.Name == "ValueCaptured").Select(item => item.Event.HistoryEventId), before.Select(item => item.HistoryEventId));
        Assert.All(before, item => { Assert.Equal(batch.LogicalCheckpointId, item.CheckpointId); Assert.Equal(physical.CommitId, item.PhysicalCommitId); });
        Assert.Equal(evidenceIds.Order(), before.SelectMany(item => JsonSerializer.Deserialize<string[]>(item.Metadata["history.valueEvidenceIds"])!).Order());
        var commitCount = writer.ListCommits().Count;
        var sequence = (await reader.FindHeadAsync(host.ExecutionId))!.LastSequence;

        await writer.CommitAsync(physical, new(RuntimeCheckpointPersistenceMode.Immediate));

        Assert.Equal(commitCount, writer.ListCommits().Count);
        Assert.Equal(sequence, (await reader.FindHeadAsync(host.ExecutionId))!.LastSequence);
        Assert.Equal(before.Select(item => (item.HistoryEventId, item.Sequence)), (await ReadValuesAsync()).Select(item => (item.HistoryEventId, item.Sequence)));

        async Task<List<RuntimeHistoryEvent>> ReadValuesAsync()
        {
            var result = new List<RuntimeHistoryEvent>(); string? continuation = null;
            for (var window = 0; window < 100; window++)
            {
                var page = await reader.ReadPageAsync(new(host.ExecutionId, 100, continuation,
                    activityExecutionId: owner.Execution.ActivityExecutionId, name: "ValueCaptured"));
                Assert.True(page.Items.Count <= 100); result.AddRange(page.Items); continuation = page.NextContinuationToken;
                if (continuation is null) return result;
            }
            throw new InvalidOperationException("The finite container fixture exceeded its history window budget.");
        }
    }

}
