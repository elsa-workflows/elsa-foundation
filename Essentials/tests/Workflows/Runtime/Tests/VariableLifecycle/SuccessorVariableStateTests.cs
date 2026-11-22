using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Alterations;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests.VariableLifecycle;

/// <summary>
/// Spec 1231 FR-007, FR-010 and ADR 0092 decision 8: a successor row never carries the source row's session reads; a
/// retry or an acceptance is the same logical invocation and keeps the bases its own writes advanced, while a
/// re-execution is a new invocation with a new read set and starts with none.
/// </summary>
public sealed class SuccessorVariableStateTests
{
    [Theory]
    [InlineData("Retry", true)]
    [InlineData("Accept", true)]
    [InlineData("ReExecute", false)]
    public void A_successor_drops_the_source_session_reads_and_keeps_own_bases_only_for_the_same_invocation(string kindName, bool keepsBases)
    {
        var kind = Enum.Parse<ActivitySuccessorStaging.SuccessorKind>(kindName);
        var source = Source() with
        {
            OwnVariableBases = [new VariableReadSetEntry("workflow", "var-count", 1)],
            SessionReads = [new VariableSessionRead("actexec-source:attempt:1", new VariableReadSetEntry("workflow", "var-count", 0), ValueProtectionPolicy.InstanceInline)]
        };

        var successor = ActivitySuccessorStaging.NewSuccessor(
            source, Node(), "actexec-successor", "scope-successor", source.Provenance,
            new Dictionary<string, string>(), DateTimeOffset.UnixEpoch, kind);

        Assert.Null(successor.SessionReads);
        if (keepsBases)
            Assert.Equal(1, Assert.Single(successor.OwnVariableBases!).Version);
        else
            Assert.Null(successor.OwnVariableBases);
    }

    private static ActivityExecutionState Source() =>
        new(
            new ActivityExecution("actexec-source", "workflow-run", "node", "authored-node", "Test.Activity", "1.0.0"),
            ActivityExecutionStatus.Faulted, "ActivityFaulted", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
            null, null, null, null, null, [], [], 1, 1, new Dictionary<string, string>());

    private static ExecutableNode Node() =>
        new(
            executableNodeId: "node",
            authoredActivityId: "authored-node",
            activityType: "Test.Activity",
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>());
}
