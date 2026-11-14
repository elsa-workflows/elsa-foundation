using System.Text.Json;
using Elsa.Activities.Bpmn.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

public sealed partial class BpmnDirectExecutionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Authored_starts_capture_canonical_subject_and_preserve_pending_and_consumed_replay(bool capture)
    {
        await using var host = await OpenAsync();
        await using var scope = await ScopeAsync(host);
        var services = scope.ServiceProvider;
        var (material, activation) = await PrepareAuthoredCapacityModelAsync(services);
        var runtime = services.GetRequiredService<IBpmnModelRuntime>();
        var caller = new BpmnModelCaller("external-actor", null) { ExecutionSubjectId = capture ? "persisted-user-a" : null };
        var laterCaller = caller with { ExecutionSubjectId = "later-user" };
        var sourceId = activation.Reference!.SourceReferenceId;
        var queue = services.GetRequiredService<IWorkflowSchedulerWorkQueue>();

        var started = await runtime.StartAsync(sourceId, "authored-subject-start", null, caller);

        Assert.True(started.Pending);
        var original = Assert.Single((await queue.ListAsync(new(started.ExecutionId))).Items);
        var command = original.Payload!.Value.Deserialize<RuntimeModelExecutionCommand>(Web)!;
        Assert.Equal("bpmn-runtime", command.Authority.SystemIdentity);
        Assert.Equal("external-actor", command.Authority.RootInitiator);
        if (capture)
        {
            Assert.Equal("persisted-user", command.Authority.Metadata["elsa.identity.subject-kind"]);
            Assert.Equal("persisted-user-a", command.Authority.Metadata["elsa.identity.subject-id"]);
            Assert.Equal(2, command.Authority.Metadata.Count);
        }
        else Assert.Empty(command.Authority.Metadata);

        var pendingReplay = await runtime.StartAsync(sourceId, "authored-subject-start", null, laterCaller);

        Assert.True(pendingReplay.Replay);
        Assert.True(pendingReplay.Pending);
        var pending = Assert.Single((await queue.ListAsync(new(started.ExecutionId))).Items);
        Assert.Equal(original.Payload.Value.GetRawText(), pending.Payload!.Value.GetRawText());
        await DrainModelAsync(host, started.ExecutionId);
        var state = (await runtime.FindExecutionAsync(started.ExecutionId, caller))!;
        Assert.True(WorkflowExecutionAuthoritySnapshot.Matches(state.Authority, command.Authority.SystemIdentity,
            command.Authority.RootInitiator, command.Authority.Metadata));
        Assert.True((await services.GetRequiredService<IWorkflowActivationCoordinator>().DeactivateMaterialAsync(
            new(activation.Slot.WorkflowDefinitionId, material, "default", WorkflowActivationSource.Publishing, activation.Slot.Revision))).Succeeded);

        var consumedReplay = await runtime.StartAsync(sourceId, "authored-subject-start", null, laterCaller);

        Assert.True(consumedReplay.Replay);
        Assert.False(consumedReplay.Pending);
        Assert.Equal(started.ExecutionId, consumedReplay.ExecutionId);
        var retained = (await runtime.FindExecutionAsync(started.ExecutionId, caller))!;
        Assert.True(WorkflowExecutionAuthoritySnapshot.Matches(retained.Authority, command.Authority.SystemIdentity,
            command.Authority.RootInitiator, command.Authority.Metadata));
    }
}
