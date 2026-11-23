using Elsa.Agent.Core.Models;
using Elsa.Agent.Services;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Agent.Tests.Runtime;

public sealed partial class AgentNativeTests
{
    [Theory]
    [InlineData("numbering")]
    [InlineData("append")]
    [InlineData("remove")]
    [InlineData("reorder")]
    [InlineData("base")]
    [InlineData("writer")]
    [InlineData("missing")]
    public async Task Settlement_preserves_write_evidence_across_committer_numbering(string change)
    {
        await using var provider = await ServicesAsync(new FixtureFactory("{\"Article\":\"unused\"}"));
        var executable = await ExecutableAsync(provider);
        await StartAsync(provider, executable);
        var workflow = (await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("run"))!;
        var parent = (await provider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync("run"))
            .Single(state => state.ParentActivityExecutionId is null);
        var now = DateTimeOffset.UtcNow;
        var writer = new VariableWriter(VariableWriteSource.SessionChange, parent.Execution.ExecutableNodeId, parent.InvocationId);
        VariableWriteRecord Write(string key, long baseVersion = 1, VariableWriter? selectedWriter = null) =>
            new("workflow", key, selectedWriter ?? writer, VariableCommitOutcome.Current, baseVersion, 1, 2, 1, now)
            { CommitSequence = VariableWriteRecord.PendingCommitSequence };
        var first = Write("first");
        var second = Write("second");
        var terminal = parent with { Status = ActivityExecutionStatus.Cancelled, CompletedAt = now, VariableWrites = [first, second] };
        var changes = new RuntimeCheckpointStateChangeSet(
            new("run", RuntimeStateChangeOperation.Upsert, workflow with { Status = WorkflowExecutionStatus.Cancelled, CompletedAt = now }, new Dictionary<string, string>()),
            null, [new(parent.InvocationId, RuntimeStateChangeOperation.Upsert, terminal, new Dictionary<string, string>())], [], [], [], []);
        var commit = new RuntimeCheckpointCommit("numbered-cancel", new("numbered-cancel-checkpoint", RuntimeCheckpointNames.ActivityCancelled,
            "run", now, [], new Dictionary<string, string>()), changes, [], new Dictionary<string, string>());
        var enricher = provider.GetRequiredService<IEnumerable<IRuntimeCheckpointCommitEnricher>>().OfType<AgentCheckpointEnricher>().Single();
        var enriched = await enricher.EnrichAsync(commit);

        var numberedFirst = first with { CommitSequence = 42 };
        var numberedSecond = second with { CommitSequence = 42 };
        IReadOnlyList<VariableWriteRecord>? writes = change switch
        {
            "append" => [numberedFirst, numberedSecond, Write("third") with { CommitSequence = 42 }],
            "remove" => [numberedFirst],
            "reorder" => [numberedSecond, numberedFirst],
            "base" => [Write("first", 0) with { CommitSequence = 42 }, numberedSecond],
            "writer" => [Write("first", selectedWriter: new(VariableWriteSource.Alteration, "operator", parent.InvocationId)) with { CommitSequence = 42 }, numberedSecond],
            "missing" => null,
            _ => [numberedFirst, numberedSecond]
        };
        var numbered = terminal with { VariableWrites = writes, Transitions = [new(ActivityLifecycleTransitionKind.Cancelled, 42)],
            FirstCommitSequence = 42, LastCommitSequence = 42, LostUpdateWarningCount = 0 };
        var final = enriched with { StateChanges = enriched.StateChanges.WithActivityExecutions(
            [new(parent.InvocationId, RuntimeStateChangeOperation.Upsert, numbered, new Dictionary<string, string>())]) };
        if (change == "numbering") await enricher.ValidateFinalAsync(final);
        else await Assert.ThrowsAsync<AgentTurnException>(() => enricher.ValidateFinalAsync(final).AsTask());
    }
}
