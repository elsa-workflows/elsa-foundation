using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.WorkHandlers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeHistoryBoundaryRetryTests
{
    [Theory]
    [InlineData(0)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public async Task Native_retry_preparation_links_the_actual_successor_once_and_preserves_retained_profiles(int version)
    {
        var at = DateTimeOffset.UnixEpoch;
        var pin = new WorkflowExecutableIdentity("artifact", "definition", "version", "1", "hash");
        var services = new ServiceCollection(); services.AddLogging();
        services.AddSingleton<IPersistenceAccessContextAccessor>(new RuntimeHistoryTestAccess());
        services.AddWorkflowRuntime(); services.AddWorkflowRuntimeHistory();
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        var selected = scope.ServiceProvider;
        var policy = version == 0 ? null : new RuntimeHistoryCapturePolicy("runtime-history-v" + version, version, true, ["ActivityRetryScheduled"]);
        await selected.GetRequiredService<IWorkflowExecutionStateStore>().SaveAsync(new("run", pin, WorkflowExecutionStatus.Running,
            null, at, at, at, null, null, null, null, new Dictionary<string, string>()) { HistoryCapturePolicy = policy });
        await selected.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(new(pin,
            new("node", "authored", "test/activity", "1", "test", JsonSerializer.SerializeToElement(new { type = "test" }),
                new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>()),
            new Dictionary<string, WorkflowExecutableResumeTarget>(), at, new Dictionary<string, string>(), IncidentStrategyBuiltIns.FaultReference));
        var metadata = new Dictionary<string, string>
        {
            [RuntimeMetadataKeys.PinnedArtifactId] = pin.ArtifactId, [RuntimeMetadataKeys.PinnedArtifactVersion] = pin.ArtifactVersion,
            [RuntimeMetadataKeys.PinnedArtifactHash] = pin.ArtifactHash
        };
        await selected.GetRequiredService<IActivityExecutionStateStore>().SaveAsync(new(
            new("prior", "run", "node", "authored", "test/activity", "1"), ActivityExecutionStatus.Faulted, null, 1,
            at, at, at, null, null, null, null, ActivitySchedulingProvenance.From("run", null, null, null, null, null, "prior", "test"),
            null, [], [], 1, 1, metadata, ExecutionScopeId: "prior", Attempt: new(1, "prior", null)));
        var work = new RuntimeSchedulerWorkItem("work", "run", "command", WorkflowExecutionCommandKind.RetryActivityBoundary,
            "envelope", "key", at, at, payload: JsonSerializer.SerializeToElement(new RetryActivityBoundaryCommand("prior", "successor", pin, "policy-retry")));
        var handler = selected.GetServices<IWorkflowSchedulerWorkHandler>().Single(item => item is WorkflowRetryActivityBoundarySchedulerWorkHandler);
        await handler.HandleAsync(work); await handler.HandleAsync(work);
        var commit = Assert.Single(selected.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>().ListCommits()).Commit;
        var facts = commit.HistorySourceFacts ?? [];
        var events = (await selected.GetRequiredService<IRuntimeHistoryStore>().ReadPageAsync(new("run"))).Items;
        if (version == 5)
        {
            var fact = Assert.Single(facts); Assert.Equal(RuntimeHistorySourceFactKind.ActivityRetryScheduled, fact.Kind);
            Assert.Equal("prior", fact.ActivityExecutionId); Assert.Equal("successor", fact.RelatedActivityExecutionId); Assert.Equal(at, fact.OccurredAt);
            var e = Assert.Single(events); Assert.Equal("ActivityRetryScheduled", e.Name);
            Assert.Equal("successor", e.Metadata["history.relatedActivityExecutionId"]);
            Assert.Equal("boundary-retry-prepared", e.Metadata["history.reasonCode"]);
            Assert.Equal("command", e.Causation!.CommandId); Assert.Equal("work", e.Causation.SchedulerWorkItemId);
        }
        else { Assert.Empty(facts); Assert.Empty(events); }
        var scheduled = Assert.Single(commit.PostCommitIntents).Payload!.Value.Deserialize<RuntimeSchedulerWorkItem>()!;
        Assert.Equal("successor", scheduled.ExecutionScopeId); Assert.Equal("prior", scheduled.Attempt!.PreviousAttemptActivityExecutionId);
        Assert.Equal(2, scheduled.Attempt.AttemptNumber);
        Assert.Null(await selected.GetRequiredService<IActivityExecutionStateStore>().FindAsync("run", "successor"));
    }
}
