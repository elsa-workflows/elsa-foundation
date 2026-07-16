using System.Text.Json;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.DispatchWorkflow.Tests;

public sealed class DispatchWorkflowEndToEndTests
{
    [Theory]
    [InlineData(null, "correlation-parent")]
    [InlineData("correlation-override", "correlation-override")]
    public async Task Global_sweep_executes_the_exact_pinned_child_with_inputs_and_parent_lineage(
        string? correlationOverride,
        string expectedCorrelation)
    {
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync();
        await AddDistractorChildSourceAsync(fixture.Services);

        var run = await fixture.StartParentAsync(
            caseId: correlationOverride is null ? "inherit" : "override",
            parentWorkflowExecutionId: correlationOverride is null ? "parent-inherit" : "parent-override",
            parentCorrelationId: "correlation-parent",
            correlationOverride: correlationOverride);

        var parent = await fixture.FindWorkflowAsync(run.Start.WorkflowExecutionId);
        Assert.NotNull(parent);
        Assert.Equal(WorkflowExecutionStatus.Completed, parent.Status);
        Assert.Equal(ActivityExecutionStatus.Completed, run.Activity.Status);
        Assert.Equal(WorkflowDispatchStatus.Pending, run.Dispatch.Status);
        Assert.Equal(run.Identity.ChildWorkflowExecutionId, run.Dispatch.ChildWorkflowExecutionId);
        Assert.Equal(DispatchWorkflowRuntimeTestFixture.ChildIdentity, run.Dispatch.ChildExecutable);
        Assert.Equal("source-child", run.Dispatch.ChildSource.SourceReferenceId);
        Assert.Equal(expectedCorrelation, run.Dispatch.CorrelationId);
        Assert.Equal("tenant-42", run.Dispatch.TenantId);
        Assert.Equal(new WorkflowExecutionPartition("partition-eu"), run.Dispatch.Partition);
        Assert.Equal(WorkflowRunKind.BackgroundWeaverRun, run.Dispatch.RunKind);
        Assert.Equal(run.Start.WorkflowExecutionId, run.Dispatch.Authority.SystemIdentity);
        Assert.Equal("root-initiator", run.Dispatch.Authority.RootInitiator);
        Assert.Equal("root-request", run.Dispatch.Authority.Metadata["authority.source"]);
        Assert.Contains(
            run.CompletionCommit.PostCommitIntents,
            intent => intent.Kind == DispatchWorkflowConstants.StartChildIntentKind &&
                      intent.IntentId == run.Identity.StartIntentId);

        var childIdValue = Assert.Single(
            await fixture.ListDurableValuesAsync(run.Start.WorkflowExecutionId),
            value => value.DurableValueId.Contains("dispatch-child-id", StringComparison.Ordinal));
        Assert.Equal(run.Identity.ChildWorkflowExecutionId, childIdValue.InlineValue!.Value.GetString());

        // The parent is durably complete before global delivery has activated or materialized the child.
        Assert.Null(await fixture.FindWorkflowAsync(run.Identity.ChildWorkflowExecutionId));
        Assert.Empty(fixture.ChildProbe.Observations);
        Assert.DoesNotContain(
            fixture.Actors.Activations,
            activation => activation.WorkflowExecutionId == run.Identity.ChildWorkflowExecutionId);
        Assert.DoesNotContain(
            fixture.Actors.Envelopes,
            envelope => envelope.WorkflowExecutionId == run.Identity.ChildWorkflowExecutionId);

        var sweep = await fixture.SweepAsync();

        Assert.Equal(1, sweep.OutboxAttemptedCount);
        Assert.Equal(1, sweep.OutboxDeliveredCount);
        Assert.Equal(0, sweep.OutboxFailedCount);
        var child = await fixture.FindWorkflowAsync(run.Identity.ChildWorkflowExecutionId);
        Assert.NotNull(child);
        Assert.Equal(WorkflowExecutionStatus.Completed, child.Status);

        var childActivation = Assert.Single(
            fixture.Actors.Activations,
            activation => activation.WorkflowExecutionId == run.Identity.ChildWorkflowExecutionId &&
                          activation.Reason == WorkflowExecutionActorActivationReason.Start);
        Assert.Equal(run.Start.WorkflowExecutionId, childActivation.RequestedBy);
        Assert.Equal(new WorkflowExecutionPartition("partition-eu"), childActivation.Partition);

        var childEnvelope = Assert.Single(
            fixture.Actors.Envelopes,
            envelope => envelope.WorkflowExecutionId == run.Identity.ChildWorkflowExecutionId &&
                        envelope.Command.Kind == WorkflowExecutionCommandKind.Start);
        Assert.Equal(run.Identity.StartIdempotencyKey, childEnvelope.IdempotencyKey);
        Assert.Equal(new WorkflowExecutionPartition("partition-eu"), childEnvelope.Partition);
        var start = childEnvelope.Command.Payload!.Value.Deserialize<WorkflowExecutionStartCommandPayload>()!;
        Assert.Equal(DispatchWorkflowRuntimeTestFixture.ChildIdentity, start.PinnedExecutable);
        Assert.Equal("source-child", start.PinnedSource!.SourceReferenceId);
        Assert.Equal("publication-child", start.PinnedSource.PublicationId);
        Assert.Equal("slot-child", start.PinnedSource.SlotId);
        Assert.Equal(run.Start.WorkflowExecutionId, start.ParentWorkflowExecutionId);
        Assert.Equal(expectedCorrelation, start.CorrelationId);
        Assert.Equal("tenant-42", start.TenantId);
        Assert.Equal(new WorkflowExecutionPartition("partition-eu"), start.Partition);
        Assert.Equal(WorkflowRunKind.BackgroundWeaverRun, start.RunKind);
        Assert.Equal(run.Start.WorkflowExecutionId, start.Authority!.SystemIdentity);
        Assert.Equal("root-initiator", start.Authority.RootInitiator);
        Assert.Equal("root-request", start.Authority.Metadata["authority.source"]);
        Assert.Empty(start.Variables);
        Assert.Equal(2, start.Inputs.Count);
        Assert.Equal("hello child", start.Inputs["message"].GetString());
        Assert.Equal(7, start.Inputs["count"].GetInt32());
        Assert.Null(start.StimulusInput);
        Assert.Null(start.TriggerNodeId);

        var observation = Assert.Single(fixture.ChildProbe.Observations);
        Assert.Equal(run.Identity.ChildWorkflowExecutionId, observation.WorkflowExecutionId);
        Assert.Equal("hello child", Assert.IsType<JsonElement>(observation.WorkflowInputs["message"]).GetString());
        Assert.Equal(7, Assert.IsType<JsonElement>(observation.WorkflowInputs["count"]).GetInt32());
        Assert.Equal(2, observation.WorkflowInputs.Count);
    }

    [Fact]
    public async Task Equivalent_activity_replay_and_repeated_sweeps_converge_on_one_logical_child()
    {
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync();
        var run = await fixture.StartParentAsync(
            caseId: "replay",
            parentWorkflowExecutionId: "parent-replay",
            parentCorrelationId: "correlation-parent");

        await fixture.ReplayAsync(run.CompletionCommit);
        Assert.Single(await fixture.ListDispatchesAsync(run.Start.WorkflowExecutionId));
        Assert.Empty(fixture.ChildProbe.Observations);

        var firstSweep = await fixture.SweepAsync();
        var secondSweep = await fixture.SweepAsync();
        await fixture.ReplayAsync(run.CompletionCommit);
        var postReplaySweep = await fixture.SweepAsync();

        Assert.Equal(1, firstSweep.OutboxDeliveredCount);
        Assert.Equal(0, secondSweep.OutboxAttemptedCount);
        Assert.Equal(0, postReplaySweep.OutboxAttemptedCount);
        Assert.Single(await fixture.ListDispatchesAsync(run.Start.WorkflowExecutionId));
        Assert.Single(fixture.ChildProbe.Observations);
        Assert.Single(
            fixture.Actors.Envelopes,
            envelope => envelope.WorkflowExecutionId == run.Identity.ChildWorkflowExecutionId &&
                        envelope.Command.Kind == WorkflowExecutionCommandKind.Start);
        Assert.NotNull(await fixture.FindWorkflowAsync(run.Identity.ChildWorkflowExecutionId));
    }

    private static ValueTask AddDistractorChildSourceAsync(IServiceProvider services) =>
        services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().SaveAsync(
            new WorkflowExecutableSourceReference(
                SourceReferenceId: "source-child-distractor",
                ArtifactId: DispatchWorkflowRuntimeTestFixture.ChildIdentity.ArtifactId,
                SourceKind: "WorkflowDefinitionVersion",
                SourceId: "version-child-distractor",
                SourceVersion: "99.0.0",
                DefinitionId: DispatchWorkflowRuntimeTestFixture.ChildIdentity.DefinitionId,
                DefinitionVersionId: "version-child-distractor",
                ArtifactVersion: "99.0.0",
                CreatedAt: DispatchWorkflowRuntimeTestFixture.Now,
                PublishedAt: DispatchWorkflowRuntimeTestFixture.Now,
                Scope: WorkflowExecutableReferenceScope.Published,
                PublicationId: "publication-child-distractor",
                SlotId: "slot-child-distractor"));
}
