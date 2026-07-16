using System.Text.Json;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
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
        Assert.Equal(1, run.Dispatch.DispatchNestingDepth);
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
        Assert.Equal(1, child.DispatchNestingDepth);

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
        Assert.Null(start.PinnedSource);
        Assert.Equal(WorkflowExecutableStartAuthorityKind.RetainedDependency, start.StartAuthority!.Kind);
        Assert.Equal(run.Start.PinnedExecutable.ArtifactId, start.StartAuthority.RetainedDependency!.ParentArtifactId);
        Assert.Equal(run.Start.PinnedExecutable.ArtifactHash, start.StartAuthority.RetainedDependency.ParentArtifactHash);
        Assert.Equal("node-dispatch", start.StartAuthority.RetainedDependency.DispatchNodeId);
        Assert.Equal(run.Start.WorkflowExecutionId, start.ParentWorkflowExecutionId);
        Assert.Equal(expectedCorrelation, start.CorrelationId);
        Assert.Equal("tenant-42", start.TenantId);
        Assert.Equal(new WorkflowExecutionPartition("partition-eu"), start.Partition);
        Assert.Equal(WorkflowRunKind.BackgroundWeaverRun, start.RunKind);
        Assert.Equal(1, start.DispatchNestingDepth);
        Assert.Equal(run.Start.WorkflowExecutionId, start.Authority!.SystemIdentity);
        Assert.Equal("root-initiator", start.Authority.RootInitiator);
        Assert.Equal("root-request", start.Authority.Metadata["authority.source"]);
        Assert.Empty(start.Variables);
        Assert.Equal(4, start.Inputs.Count);
        Assert.Equal("hello child", start.Inputs["message"].GetString());
        Assert.Equal(7, start.Inputs["count"].GetInt32());
        Assert.Equal("workflow-input-tenant", start.Inputs["tenant"].GetString());
        Assert.Equal("from-default", start.Inputs["defaulted"].GetString());
        Assert.Null(start.StimulusInput);
        Assert.Null(start.TriggerNodeId);

        var observation = Assert.Single(fixture.ChildProbe.Observations);
        Assert.Equal(run.Identity.ChildWorkflowExecutionId, observation.WorkflowExecutionId);
        Assert.Equal("hello child", Assert.IsType<JsonElement>(observation.WorkflowInputs["message"]).GetString());
        Assert.Equal(7, Assert.IsType<JsonElement>(observation.WorkflowInputs["count"]).GetInt32());
        Assert.Equal("workflow-input-tenant", Assert.IsType<JsonElement>(observation.WorkflowInputs["tenant"]).GetString());
        Assert.Equal("from-default", Assert.IsType<JsonElement>(observation.WorkflowInputs["defaulted"]).GetString());
        Assert.Equal(4, observation.WorkflowInputs.Count);
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
        var child = await fixture.FindWorkflowAsync(run.Identity.ChildWorkflowExecutionId);
        Assert.NotNull(child);
        Assert.Equal(1, child.DispatchNestingDepth);
    }

    [Fact]
    public async Task Default_limit_accepts_depths_1_through_32_and_rejects_attempted_33()
    {
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync();

        for (var childDepth = 1; childDepth <= 32; childDepth++)
        {
            var run = await fixture.StartParentAsync(
                caseId: $"depth-{childDepth}",
                parentWorkflowExecutionId: $"parent-depth-{childDepth}",
                parentCorrelationId: "correlation-depth",
                dispatchNestingDepth: childDepth - 1);

            Assert.Equal(childDepth, run.Dispatch.DispatchNestingDepth);
            var sweep = await fixture.SweepAsync();
            Assert.Equal(1, sweep.OutboxDeliveredCount);
            Assert.Equal(
                childDepth,
                (await fixture.FindWorkflowAsync(run.Identity.ChildWorkflowExecutionId))!.DispatchNestingDepth);
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await fixture.StartParentAsync(
                "depth-33",
                "parent-depth-33",
                "correlation-depth",
                dispatchNestingDepth: 32));

        Assert.Contains("nesting depth 33", exception.Message, StringComparison.Ordinal);
        Assert.Empty(await fixture.ListDispatchesAsync("parent-depth-33"));
    }

    [Fact]
    public async Task Custom_limit_uses_the_same_inclusive_boundary()
    {
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync(maxNestingDepth: 2);
        var run = await fixture.StartParentAsync(
            "custom-depth-2",
            "parent-custom-depth-2",
            "correlation-depth",
            dispatchNestingDepth: 1);

        Assert.Equal(2, run.Dispatch.DispatchNestingDepth);
        await fixture.SweepAsync();
        Assert.Equal(2, (await fixture.FindWorkflowAsync(run.Identity.ChildWorkflowExecutionId))!.DispatchNestingDepth);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await fixture.StartParentAsync(
                "custom-depth-3",
                "parent-custom-depth-3",
                "correlation-depth",
                dispatchNestingDepth: 2));
        Assert.Contains("maximum of 2", exception.Message, StringComparison.Ordinal);
        Assert.Empty(await fixture.ListDispatchesAsync("parent-custom-depth-3"));
    }

    [Fact]
    public async Task Same_definition_different_artifact_version_skew_is_bounded_but_allowed()
    {
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync();
        var run = await fixture.StartParentAsync(
            "version-skew",
            "parent-version-skew",
            "correlation-version-skew",
            parentDefinitionId: DispatchWorkflowRuntimeTestFixture.ChildIdentity.DefinitionId);

        Assert.NotEqual(run.Start.PinnedExecutable.ArtifactId, run.Dispatch.ChildExecutable.ArtifactId);
        Assert.Equal(run.Start.PinnedExecutable.DefinitionId, run.Dispatch.ChildExecutable.DefinitionId);
        Assert.Equal(1, run.Dispatch.DispatchNestingDepth);

        await fixture.SweepAsync();
        Assert.Equal(1, (await fixture.FindWorkflowAsync(run.Identity.ChildWorkflowExecutionId))!.DispatchNestingDepth);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Retained_parent_executes_original_child_after_unpublication_or_replacement(bool replace)
    {
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync();
        var run = await fixture.StartParentAsync(
            replace ? "child-replaced" : "child-unpublished",
            replace ? "parent-child-replaced" : "parent-child-unpublished",
            "correlation-retained-child");

        await fixture.ReplaceOrUnpublishChildAsync(replace);
        var sweep = await fixture.SweepAsync();

        Assert.Equal(1, sweep.OutboxDeliveredCount);
        var childStart = Assert.Single(
            fixture.Actors.Envelopes,
            envelope => envelope.WorkflowExecutionId == run.Identity.ChildWorkflowExecutionId &&
                        envelope.Command.Kind == WorkflowExecutionCommandKind.Start);
        var payload = childStart.Command.Payload!.Value.Deserialize<WorkflowExecutionStartCommandPayload>()!;
        Assert.Equal(DispatchWorkflowRuntimeTestFixture.ChildIdentity, payload.PinnedExecutable);
        Assert.Null(payload.PinnedSource);
        Assert.Equal(WorkflowExecutableStartAuthorityKind.RetainedDependency, payload.StartAuthority!.Kind);
        Assert.Single(fixture.ChildProbe.Observations);
    }

    [Fact]
    public async Task Retired_parent_publication_rejects_a_new_root_start_without_materializing_state()
    {
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync();
        var run = await fixture.StartParentAsync(
            "retired-parent",
            "parent-before-retirement",
            "correlation-retired-parent");
        var parentSource = run.Start.PinnedSource!;
        await fixture.Services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>()
            .RetireAsync(parentSource.SourceReferenceId, DispatchWorkflowRuntimeTestFixture.Now.AddMinutes(1), "parent-retired");

        await using var scope = fixture.Services.CreateAsyncScope();
        var exception = await Assert.ThrowsAsync<WorkflowExecutableReferenceRejectedException>(() =>
            scope.ServiceProvider.GetRequiredService<IWorkflowStartDispatcher>().DispatchAsync(
                new WorkflowExecutionStartDispatchRequest(
                    artifactId: run.Start.PinnedExecutable.ArtifactId,
                    requestedBy: "new-root-caller",
                    workflowExecutionId: "parent-after-retirement",
                    sourceSelection: new WorkflowExecutableSourceSelection(parentSource.SourceReferenceId),
                    provenanceRequirement: WorkflowExecutableProvenanceRequirement.RequireLiveReference),
                WorkflowExecutableReferenceScope.Published).AsTask());

        Assert.Equal(WorkflowExecutableReferenceRejectionReason.NoLiveReference, exception.Reason);
        Assert.Null(await fixture.FindWorkflowAsync("parent-after-retirement"));
        Assert.DoesNotContain(
            fixture.Actors.Activations,
            activation => activation.WorkflowExecutionId == "parent-after-retirement");
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
