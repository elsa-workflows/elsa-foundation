using System.Text.Json;
using Elsa.Activities.ControlFlow.Results;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.ControlFlow.Tests;

public sealed class DeterministicCollectionTests
{
    private const string WorkflowExecutionId = "wfexec-collection";
    private const string ParentExecutionId = "actexec-structured";
    private static readonly DateTimeOffset Now = new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Parallel_results_follow_authored_branch_identity_across_randomized_completion_orders()
    {
        string[] authoredBranchOrder = ["node-zeta", "node-alpha", "node-middle"];
        var branchValues = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["node-zeta"] = "first",
            ["node-alpha"] = "second",
            ["node-middle"] = "third"
        };

        for (var seed = 0; seed < 64; seed++)
        {
            var random = new Random(seed);
            var completionOrder = authoredBranchOrder
                .Select((nodeId, sourceIndex) => CompletedChild(
                    $"actexec-branch-{sourceIndex}",
                    nodeId,
                    branchValues[nodeId],
                    branchId: StructuredExecutionIdentity.Branch(ParentExecutionId, nodeId),
                    iterationId: null,
                    completedAt: Now.AddTicks(random.NextInt64(1, 10_000))))
                .OrderBy(_ => random.Next())
                .ToArray();

            var collected = DeterministicResultCollector.CollectBranches(
                completionOrder,
                ParentExecutionId,
                authoredBranchOrder);

            Assert.Equal(new[] { "first", "second", "third" }, StringValues(collected));
        }
    }

    [Fact]
    public void Loop_results_follow_source_iteration_identity_across_randomized_completion_orders()
    {
        var sourceValues = Enumerable.Range(0, 24).Select(index => $"item-{index:00}").ToArray();

        for (var seed = 0; seed < 64; seed++)
        {
            var random = new Random(seed);
            var completionOrder = sourceValues
                .Select((value, sourceIndex) => CompletedChild(
                    $"actexec-iteration-{sourceIndex}",
                    "node-body",
                    value,
                    branchId: null,
                    iterationId: StructuredExecutionIdentity.Iteration(ParentExecutionId, sourceIndex),
                    completedAt: Now.AddTicks(random.NextInt64(1, 10_000))))
                .OrderBy(_ => random.Next())
                .ToArray();

            var collected = DeterministicResultCollector.CollectIterations(
                completionOrder,
                ParentExecutionId,
                sourceValues.Length);

            Assert.Equal(sourceValues, StringValues(collected));
        }
    }

    private static ActivityExecutionState CompletedChild(
        string activityExecutionId,
        string executableNodeId,
        string value,
        string? branchId,
        string? iterationId,
        DateTimeOffset completedAt)
    {
        var result = ValueEnvelope.Inline(
            new ValueTypeDescriptor("String"),
            JsonSerializer.SerializeToElement(value),
            ValueProtectionPolicy.InstanceInline);
        var completion = new ActivityCompletion(
            activityExecutionId,
            $"attempt:{activityExecutionId}",
            result,
            "Done",
            completedAt,
            "test.result:1.0.0");
        return new ActivityExecutionState(
            new ActivityExecution(activityExecutionId, WorkflowExecutionId, executableNodeId, executableNodeId, "test.result", "1.0.0"),
            ActivityExecutionStatus.Completed,
            SubStatus: null,
            ScheduledAt: completedAt.AddSeconds(-1),
            StartedAt: completedAt.AddMilliseconds(-1),
            CompletedAt: completedAt,
            SchedulingActivityExecutionId: ParentExecutionId,
            ParentActivityExecutionId: ParentExecutionId,
            BranchId: branchId,
            IterationId: iterationId,
            CallStackDepth: 1,
            BookmarkIds: [],
            IncidentIds: [],
            FaultCount: 0,
            AggregateFaultCount: 0,
            Metadata: new Dictionary<string, string>())
        {
            Attempts = [],
            Completion = completion
        };
    }

    private static string[] StringValues(IEnumerable<ValueEnvelope> values) =>
        values.Select(value => value.InlineValue!.Value.GetString()!).ToArray();
}
