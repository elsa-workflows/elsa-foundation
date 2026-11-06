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
        await using var host = WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesSequenceFeature().ConfigureServices(services))
            .ConfigureServices(services =>
            {
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

        var run = await host.RunAsync(WorkflowExecutionHarness.NewExecutable(root));

        run.AssertWorkflowCompleted();
        var owner = Assert.Single(run.States("container"));
        Assert.Equal(VariableFrameStatus.Closed, owner.VariableFrame!.Status);
        Assert.Equal(4097, owner.VariableFrame.Values.Count);
        var inspection = (await host.Services.GetRequiredService<IActivityExecutionInspectionStore>()
            .FindAsync(host.ExecutionId, owner.Execution.ActivityExecutionId))!;
        var values = inspection.ValueSnapshots.Where(value => value.Subject == ActivityExecutionInspectionValueSubject.ContainerVariable).ToArray();
        Assert.Equal(4097, values.Length);
        Assert.Equal(variables.Select(variable => variable.Name).Order(), values.Select(value => value.Name).Order());
        var writer = host.Services.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>();
        var completion = Assert.Single(writer.ListCommits(), record => record.Commit.Checkpoint.Name == RuntimeCheckpointNames.ActivityCompleted && record.Commit.StateChanges.ActivityExecutions.Any(change =>
            change.StateId == owner.Execution.ActivityExecutionId && change.State.Status == ActivityExecutionStatus.Completed)).Commit;
        Assert.Equal(RuntimeCheckpointNames.ActivityCompleted, completion.Checkpoint.Name);
        var projection = Assert.Single(completion.StateChanges.ActivityExecutionInspections,
            change => change.StateId == owner.Execution.ActivityExecutionId).State;
        if (!capture)
        {
            Assert.Null(run.WorkflowState!.HistoryCapturePolicy);
            Assert.Null(projection.CheckpointValueCapture); Assert.Null(completion.HistoryBatches);
            return;
        }

        var evidenceIds = values.Select(value => value.EvidenceId!).ToArray();
        Assert.All(evidenceIds, id => Assert.False(string.IsNullOrWhiteSpace(id)));
        Assert.Equal(4097, evidenceIds.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(evidenceIds.Order(), projection.CheckpointValueCapture!.EvidenceIds.Order());
        var batch = Assert.Single(completion.HistoryBatches!);
        Assert.Equal(completion.Checkpoint.CheckpointId, batch.LogicalCheckpointId);
        Assert.Equal(4097, batch.Candidates.Count(candidate => candidate.Event.Name == "ValueCaptured"));
        var reader = host.Services.GetRequiredService<IRuntimeHistoryStore>();
        var before = await ReadValuesAsync();
        Assert.Equal(4097, before.Count);
        Assert.Equal(evidenceIds.Order(), before.SelectMany(item => JsonSerializer.Deserialize<string[]>(item.Metadata["history.valueEvidenceIds"])!).Order());
        var commitCount = writer.ListCommits().Count;
        var sequence = (await reader.FindHeadAsync(host.ExecutionId))!.LastSequence;

        await writer.CommitAsync(completion, new(RuntimeCheckpointPersistenceMode.Immediate));

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
