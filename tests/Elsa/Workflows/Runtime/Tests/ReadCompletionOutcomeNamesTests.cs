using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class ReadCompletionOutcomeNamesTests
{
    [Fact]
    public void PersistedOutcomeNames_AreNormalizedWithoutDoneDefault()
    {
        var state = State(metadata: new Dictionary<string, string>
        {
            [RuntimeMetadataKeys.CompletionOutcomeNames] = JsonSerializer.Serialize(new[] { "Approved", "Escalated" })
        });

        Assert.Equal(["Approved", "Escalated"], SchedulerWorkHandlerHelpers.ReadCompletionOutcomeNames(state, skippedSubStatus: null));
    }

    [Fact]
    public void PersistedNull_Throws()
    {
        var state = State(metadata: new Dictionary<string, string>
        {
            [RuntimeMetadataKeys.CompletionOutcomeNames] = "null"
        });

        Assert.Throws<InvalidOperationException>(() => SchedulerWorkHandlerHelpers.ReadCompletionOutcomeNames(state, skippedSubStatus: null));
    }

    [Fact]
    public void NothingPersisted_FallsBackToDone()
    {
        Assert.Equal([ActivityOutcomes.Done], SchedulerWorkHandlerHelpers.ReadCompletionOutcomeNames(State(), skippedSubStatus: null));
    }

    [Fact]
    public void NothingPersisted_SkippedSubStatus_YieldsNoOutcomesOnlyWhenOptedIn()
    {
        var skipped = State(subStatus: "Skipped");

        Assert.Empty(SchedulerWorkHandlerHelpers.ReadCompletionOutcomeNames(skipped, skippedSubStatus: "Skipped"));
        Assert.Equal([ActivityOutcomes.Done], SchedulerWorkHandlerHelpers.ReadCompletionOutcomeNames(skipped, skippedSubStatus: null));
    }

    [Fact]
    public void NullSubStatus_NeverMatchesTheNullOptOut()
    {
        // The guard is load-bearing: without it a null SubStatus would "match" a null opt-out and
        // silently turn the Done fallback into no outcomes for every handler that opts out.
        Assert.Equal([ActivityOutcomes.Done], SchedulerWorkHandlerHelpers.ReadCompletionOutcomeNames(State(subStatus: null), skippedSubStatus: null));
    }

    private static ActivityExecutionState State(IReadOnlyDictionary<string, string>? metadata = null, string? subStatus = null)
    {
        var now = DateTimeOffset.Parse("2026-07-13T10:00:00Z");
        return new ActivityExecutionState(
            new ActivityExecution(
                "activity-1",
                "execution-1",
                "node-1",
                "authored-1",
                "Test.Activity",
                "1"),
            ActivityExecutionStatus.Completed,
            subStatus,
            1,
            now,
            now,
            now,
            null,
            null,
            null,
            null,
            ActivitySchedulingProvenance.From(
                "execution-1",
                parentActivityExecutionId: null,
                schedulingActivityExecutionId: null,
                branchId: null,
                iterationId: null,
                executionPathId: null,
                executionScopeId: null,
                schedulingCause: "test"),
            null,
            [],
            [],
            0,
            0,
            metadata ?? new Dictionary<string, string>());
    }
}
