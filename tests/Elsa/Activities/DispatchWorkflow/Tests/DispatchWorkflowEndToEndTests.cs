using System.Text.Json;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Models;
using Elsa.Activities.DispatchWorkflow.Runtime.Services;
using Elsa.Persistence.Core;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.DispatchWorkflow.Tests;

public sealed class DispatchWorkflowEndToEndTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Detached_test_child_survives_parent_completion_until_scope_teardown()
    {
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync();
        var testScope = NewTestScope("scope-detached-parent-complete");
        var run = await fixture.StartParentAsync(
            caseId: "test-detached-parent-complete",
            parentWorkflowExecutionId: "parent-test-detached-parent-complete",
            parentCorrelationId: "correlation-parent",
            runKind: WorkflowRunKind.TestRun,
            testScope: testScope);

        Assert.Equal(WorkflowExecutionStatus.Completed, (await fixture.FindWorkflowAsync(run.Start.WorkflowExecutionId))?.Status);
        Assert.Equal(WorkflowDispatchStatus.Pending, run.Dispatch.Status);

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<IWorkflowTestScopeCleaner>().CloseAsync(
                testScope.ScopeId,
                WorkflowTestScopeCloseReason.ExplicitTeardown,
                DispatchWorkflowRuntimeTestFixture.Now.AddMinutes(1));
            Assert.Equal(1, result.CancelledBeforeAdmission);
        }

        Assert.Equal(WorkflowDispatchStatus.Cancelled, Assert.Single(await fixture.ListDispatchesAsync(run.Start.WorkflowExecutionId)).Status);
        Assert.Null(await fixture.FindWorkflowAsync(run.Identity.ChildWorkflowExecutionId));
        Assert.Equal(1, (await fixture.SweepAsync()).OutboxDeliveredCount);
        Assert.Null(await fixture.FindWorkflowAsync(run.Identity.ChildWorkflowExecutionId));
    }

    [Fact]
    public async Task Waited_test_child_preserves_production_success_and_is_not_direct_scope_cleanup_target()
    {
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync();
        var testScope = NewTestScope("scope-waited-parity");
        var run = await fixture.StartParentAsync(
            caseId: "test-waited-parity",
            parentWorkflowExecutionId: "parent-test-waited-parity",
            parentCorrelationId: "correlation-parent",
            waitForCompletion: true,
            runKind: WorkflowRunKind.TestRun,
            testScope: testScope);

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<IWorkflowTestScopeCleaner>().CloseAsync(
                testScope.ScopeId,
                WorkflowTestScopeCloseReason.ExplicitTeardown,
                DispatchWorkflowRuntimeTestFixture.Now.AddMinutes(1));
            Assert.Equal(0, result.CancelledBeforeAdmission);
            Assert.Equal(0, result.CancellationQueued);
        }

        Assert.Equal(WorkflowDispatchStatus.Pending, Assert.Single(await fixture.ListDispatchesAsync(run.Start.WorkflowExecutionId)).Status);
        Assert.Equal(1, (await fixture.SweepAsync()).OutboxDeliveredCount);
        Assert.Equal(1, (await fixture.SweepAsync()).OutboxDeliveredCount);
        Assert.Equal(WorkflowExecutionStatus.Completed, (await fixture.FindWorkflowAsync(run.Start.WorkflowExecutionId))?.Status);
        Assert.Equal(WorkflowExecutionStatus.Completed, (await fixture.FindWorkflowAsync(run.Identity.ChildWorkflowExecutionId))?.Status);
        Assert.Equal(WorkflowDispatchStatus.Completed, Assert.Single(await fixture.ListDispatchesAsync(run.Start.WorkflowExecutionId)).Status);
    }

    [Theory]
    [InlineData(true, null, true)]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, null, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public async Task Dispatch_PersistsEffectiveCancelPolicy(
        bool waitForCompletion,
        bool? authoredPolicy,
        bool expectedPolicy)
    {
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync();
        var run = await fixture.StartParentAsync(
            caseId: $"cancel-policy-{waitForCompletion}-{authoredPolicy}",
            parentWorkflowExecutionId: $"parent-cancel-policy-{waitForCompletion}-{authoredPolicy}",
            parentCorrelationId: "correlation-parent",
            waitForCompletion: waitForCompletion,
            cancelChildOnParentCancellation: authoredPolicy);

        Assert.Equal(expectedPolicy, WorkflowDispatchLifecycle.IsCancellationPropagationEnabled(run.Dispatch));
        Assert.Equal(
            expectedPolicy.ToString().ToLowerInvariant(),
            run.Dispatch.Metadata[WorkflowDispatchLifecycle.CancellationPolicyMetadataKey]);
    }

    [Theory]
    [InlineData(WorkflowExecutionStatus.Faulted, WorkflowDispatchStatus.Faulted, DispatchWorkflowOutcomes.Faulted)]
    [InlineData(WorkflowExecutionStatus.Cancelled, WorkflowDispatchStatus.Cancelled, DispatchWorkflowOutcomes.Cancelled)]
    public async Task NonSuccessChild_CompletesParentActivityWithOrdinarySafeOutcome(
        WorkflowExecutionStatus childStatus,
        WorkflowDispatchStatus dispatchStatus,
        string expectedOutcome)
    {
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync();
        var run = await fixture.StartParentAsync(
            caseId: $"terminal-{expectedOutcome.ToLowerInvariant()}",
            parentWorkflowExecutionId: $"parent-terminal-{expectedOutcome.ToLowerInvariant()}",
            parentCorrelationId: "correlation-parent",
            waitForCompletion: true);
        if (childStatus == WorkflowExecutionStatus.Faulted)
        {
            await fixture.Services.GetRequiredService<IIncidentStateStore>().TryAddAsync(new IncidentState(
                "incident-child",
                run.Identity.ChildWorkflowExecutionId,
                "activity-child",
                "node-child",
                IncidentSeverity.Error,
                IncidentStatus.Blocking,
                IncidentResolutionAction.FaultWorkflow,
                "SecretException",
                "secret child message",
                DispatchWorkflowRuntimeTestFixture.Now,
                null,
                new Dictionary<string, string> { ["unsafe"] = "secret diagnostic payload" }));
        }

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<RuntimeCheckpointCommitter>()
                .CommitAsync(NewTerminalCommit(run, childStatus));
            Assert.True(result.Succeeded);
        }

        var resumeIntent = Assert.Single(
            fixture.Checkpoints.SingleIntentCommit(DispatchWorkflowConstants.ResumeParentIntentKind).PostCommitIntents,
            intent => intent.Kind == DispatchWorkflowConstants.ResumeParentIntentKind);
        await using (var scope = fixture.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<ParentResumeExecutor>().HandleAsync(resumeIntent);

        Assert.Equal(WorkflowExecutionStatus.Completed, (await fixture.FindWorkflowAsync(run.Start.WorkflowExecutionId))?.Status);
        var activity = Assert.Single(await fixture.ListActivitiesAsync(run.Start.WorkflowExecutionId));
        Assert.Equal(ActivityExecutionStatus.Completed, activity.Status);
        var outcomes = JsonSerializer.Deserialize<string[]>(activity.Metadata[RuntimeMetadataKeys.CompletionOutcomeNames]);
        Assert.Equal(new[] { expectedOutcome }, outcomes);
        Assert.Empty(await fixture.Services.GetRequiredService<IIncidentStateStore>().ListAsync(run.Start.WorkflowExecutionId));

        var values = await fixture.ListDurableValuesAsync(run.Start.WorkflowExecutionId);
        var resultValue = Assert.Single(values, value => value.DurableValueId.Contains("dispatch-result-terminal", StringComparison.Ordinal));
        var dispatchResult = resultValue.InlineValue!.Value.Deserialize<DispatchWorkflowResult>(SerializerOptions);
        Assert.NotNull(dispatchResult);
        Assert.Equal(dispatchStatus, dispatchResult.Status);
        Assert.Empty(dispatchResult.Outputs);
        Assert.Equal(
            childStatus == WorkflowExecutionStatus.Faulted
                ? DispatchWorkflowDiagnostics.FaultedCode
                : DispatchWorkflowDiagnostics.CancelledCode,
            dispatchResult.DiagnosticMetadata[DispatchWorkflowDiagnostics.CodeKey]);
        Assert.DoesNotContain("secret", resultValue.InlineValue.Value.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Wait_mode_starts_child_then_resumes_parent_once_with_completed_result()
    {
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync();
        var run = await fixture.StartParentAsync(
            caseId: "wait-success",
            parentWorkflowExecutionId: "parent-wait-success",
            parentCorrelationId: "correlation-parent",
            waitForCompletion: true);

        Assert.Equal(ActivityExecutionStatus.Suspended, run.Activity.Status);
        Assert.NotNull(await fixture.FindBookmarkAsync(run.Start.WorkflowExecutionId, run.Identity.WaitBookmarkId));
        Assert.Null(await fixture.FindWorkflowAsync(run.Identity.ChildWorkflowExecutionId));

        var childSweep = await fixture.SweepAsync();
        Assert.Equal(1, childSweep.OutboxDeliveredCount);
        Assert.Equal(WorkflowExecutionStatus.Completed, (await fixture.FindWorkflowAsync(run.Identity.ChildWorkflowExecutionId))?.Status);
        Assert.Equal(WorkflowDispatchStatus.Completed, Assert.Single(await fixture.ListDispatchesAsync(run.Start.WorkflowExecutionId)).Status);

        var resumeSweep = await fixture.SweepAsync();
        Assert.Equal(1, resumeSweep.OutboxDeliveredCount);
        Assert.Null(await fixture.FindBookmarkAsync(run.Start.WorkflowExecutionId, run.Identity.WaitBookmarkId));
        Assert.Equal(WorkflowExecutionStatus.Completed, (await fixture.FindWorkflowAsync(run.Start.WorkflowExecutionId))?.Status);
        var activity = Assert.Single(await fixture.ListActivitiesAsync(run.Start.WorkflowExecutionId));
        Assert.Equal(ActivityExecutionStatus.Completed, activity.Status);

        var values = await fixture.ListDurableValuesAsync(run.Start.WorkflowExecutionId);
        var resultValue = Assert.Single(values, value => value.DurableValueId.Contains("dispatch-result-wait-success", StringComparison.Ordinal));
        var result = resultValue.InlineValue!.Value.Deserialize<DispatchWorkflowResult>(SerializerOptions);
        Assert.NotNull(result);
        Assert.Equal(run.Identity.ChildWorkflowExecutionId, result.ChildWorkflowExecutionId);
        Assert.Equal(WorkflowDispatchStatus.Completed, result.Status);
        Assert.Empty(result.Outputs);

        var duplicateSweep = await fixture.SweepAsync();
        Assert.Equal(0, duplicateSweep.OutboxAttemptedCount);
        Assert.Single(fixture.Actors.Envelopes, envelope =>
            envelope.WorkflowExecutionId == run.Start.WorkflowExecutionId &&
            envelope.Command.Kind == WorkflowExecutionCommandKind.ResumeBookmark);
    }

    [Fact]
    public async Task In_memory_wait_converges_in_process_but_process_recreation_has_no_durable_state()
    {
        const string parentWorkflowExecutionId = "parent-process-local";
        string bookmarkId;

        await using (var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync())
        {
            var run = await fixture.StartParentAsync(
                caseId: "process-local",
                parentWorkflowExecutionId: parentWorkflowExecutionId,
                parentCorrelationId: "correlation-parent",
                waitForCompletion: true);
            bookmarkId = run.Identity.WaitBookmarkId;

            await fixture.SweepAsync();
            await fixture.SweepAsync();

            Assert.Equal(
                WorkflowExecutionStatus.Completed,
                (await fixture.FindWorkflowAsync(parentWorkflowExecutionId))?.Status);
            Assert.Null(await fixture.FindBookmarkAsync(parentWorkflowExecutionId, bookmarkId));
        }

        await using var recreated = await DispatchWorkflowRuntimeTestFixture.CreateAsync();
        Assert.Null(await recreated.FindWorkflowAsync(parentWorkflowExecutionId));
        Assert.Null(await recreated.FindBookmarkAsync(parentWorkflowExecutionId, bookmarkId));
        Assert.Empty(await recreated.ListDispatchesAsync(parentWorkflowExecutionId));
    }

    private static RuntimeCheckpointCommit NewTerminalCommit(
        ParentDispatchRun run,
        WorkflowExecutionStatus status)
    {
        var terminalAt = DispatchWorkflowRuntimeTestFixture.Now.AddMinutes(1);
        var child = new WorkflowExecutionState(
            run.Identity.ChildWorkflowExecutionId,
            run.Dispatch.ChildExecutable,
            status,
            null,
            DispatchWorkflowRuntimeTestFixture.Now,
            DispatchWorkflowRuntimeTestFixture.Now,
            terminalAt,
            terminalAt,
            run.Dispatch.CorrelationId,
            run.Start.WorkflowExecutionId,
            run.Dispatch.TenantId,
            new Dictionary<string, string>())
        {
            RunKind = run.Dispatch.RunKind,
            PinnedSource = run.Dispatch.ChildSource,
            Partition = run.Dispatch.Partition,
            Authority = run.Dispatch.Authority,
            DispatchNestingDepth = 1
        };
        var checkpointName = status == WorkflowExecutionStatus.Faulted
            ? RuntimeCheckpointNames.WorkflowFaulted
            : RuntimeCheckpointNames.WorkflowCancelled;
        return new RuntimeCheckpointCommit(
            $"commit:{child.WorkflowExecutionId}:{checkpointName}",
            new RuntimeCheckpoint(
                $"checkpoint:{child.WorkflowExecutionId}:{checkpointName}",
                checkpointName,
                child.WorkflowExecutionId,
                terminalAt,
                [],
                new Dictionary<string, string>()),
            new RuntimeCheckpointStateChangeSet(
                new RuntimeStateChange<WorkflowExecutionState>(
                    child.WorkflowExecutionId,
                    RuntimeStateChangeOperation.Upsert,
                    child,
                    new Dictionary<string, string>()),
                null,
                [], [], [], [], []),
            [],
            new Dictionary<string, string>());
    }

    [Fact]
    public async Task Threefold_wait_start_terminal_and_resume_replays_converge_once()
    {
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync();
        var run = await fixture.StartParentAsync(
            caseId: "triple-replay",
            parentWorkflowExecutionId: "parent-triple-replay",
            parentCorrelationId: "correlation-parent",
            waitForCompletion: true);

        for (var attempt = 0; attempt < 3; attempt++)
            await fixture.ReplayAsync(run.CompletionCommit);

        var startIntent = Assert.Single(
            run.CompletionCommit.PostCommitIntents,
            intent => intent.Kind == DispatchWorkflowConstants.StartChildIntentKind);
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var childStart = scope.ServiceProvider.GetRequiredService<ChildStartExecutor>();
            for (var attempt = 0; attempt < 3; attempt++)
                await childStart.HandleAsync(startIntent);
        }

        var terminalCommit = fixture.Checkpoints.SingleIntentCommit(DispatchWorkflowConstants.ResumeParentIntentKind);
        for (var attempt = 0; attempt < 3; attempt++)
            await fixture.ReplayAsync(terminalCommit);

        var resumeIntent = Assert.Single(
            terminalCommit.PostCommitIntents,
            intent => intent.Kind == DispatchWorkflowConstants.ResumeParentIntentKind);
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var parentResume = scope.ServiceProvider.GetRequiredService<ParentResumeExecutor>();
            for (var attempt = 0; attempt < 3; attempt++)
                await parentResume.HandleAsync(resumeIntent);
        }

        var acknowledgementSweep = await fixture.SweepAsync();
        Assert.True(acknowledgementSweep.OutboxDeliveredCount >= 2);
        for (var attempt = 0; attempt < 3; attempt++)
            Assert.Equal(0, (await fixture.SweepAsync()).OutboxAttemptedCount);

        Assert.Single(fixture.ChildProbe.Observations);
        Assert.Null(await fixture.FindBookmarkAsync(run.Start.WorkflowExecutionId, run.Identity.WaitBookmarkId));
        Assert.Equal(
            WorkflowExecutionStatus.Completed,
            (await fixture.FindWorkflowAsync(run.Start.WorkflowExecutionId))?.Status);
        var activity = Assert.Single(await fixture.ListActivitiesAsync(run.Start.WorkflowExecutionId));
        Assert.Equal(ActivityExecutionStatus.Completed, activity.Status);
        Assert.Equal(
            WorkflowDispatchStatus.Completed,
            Assert.Single(await fixture.ListDispatchesAsync(run.Start.WorkflowExecutionId)).Status);
    }

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
    public async Task Three_transient_start_failures_then_success_preserve_every_logical_identity()
    {
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync();
        var run = await fixture.StartParentAsync(
            caseId: "transient-recovery",
            parentWorkflowExecutionId: "parent-transient-recovery",
            parentCorrelationId: "correlation-parent");
        await using var scope = fixture.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(
            PersistenceAccessContext.Scoped(new PersistenceScope(run.Dispatch.TenantId!)));
        var timeProvider = fixture.Services.GetRequiredService<TimeProvider>();
        var outboxStore = scope.ServiceProvider.GetRequiredService<IRuntimePostCommitOutboxStore>();
        var dispatchStore = scope.ServiceProvider.GetRequiredService<IWorkflowDispatchStore>();
        var flakyStartDispatcher = new FlakyStartDispatcher(
            scope.ServiceProvider.GetRequiredService<IWorkflowStartDispatcher>(),
            transientFailureCount: 3);
        var processor = new RuntimePostCommitOutboxProcessor(
            outboxStore,
            new HandlerIntentDispatcher(new ChildStartExecutor(flakyStartDispatcher, dispatchStore, timeProvider)),
            timeProvider,
            DefaultRuntimeFaultCapturePolicy.CreateDefault(),
            dispatchStore);
        var observedOutboxIds = new List<string>();
        var observedIntentIds = new List<string>();

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            var pending = Assert.Single(await outboxStore.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(
                timeProvider.GetUtcNow(),
                limit: 10,
                intentKind: DispatchWorkflowConstants.StartChildIntentKind)));
            observedOutboxIds.Add(pending.OutboxItemId);
            observedIntentIds.Add(pending.Intent.IntentId);

            var result = await processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(
                limit: 10,
                intentKind: DispatchWorkflowConstants.StartChildIntentKind));
            Assert.Equal(
                attempt < 4 ? RuntimePostCommitOutboxStatus.FailedRetryable : RuntimePostCommitOutboxStatus.Delivered,
                Assert.Single(result.Items).RequestedDeliveryResultStatus);
            if (attempt < 4)
                fixture.AdvanceTime(TimeSpan.FromSeconds(1));
        }

        Assert.All(observedOutboxIds, id => Assert.Equal(observedOutboxIds[0], id));
        Assert.All(observedIntentIds, id => Assert.Equal(run.Identity.StartIntentId, id));
        Assert.Equal(4, flakyStartDispatcher.Requests.Count);
        Assert.All(flakyStartDispatcher.Requests, request =>
        {
            Assert.Equal(run.Identity.ChildWorkflowExecutionId, request.WorkflowExecutionId);
            Assert.Equal(run.Identity.StartIdempotencyKey, request.IdempotencyKey);
            Assert.Equal(run.Dispatch.ChildExecutable.ArtifactId, request.ArtifactId);
            Assert.Equal(run.Dispatch.ParentWorkflowExecutionId, request.ParentWorkflowExecutionId);
        });
        Assert.Single(fixture.ChildProbe.Observations);
        Assert.Single(
            fixture.Actors.Envelopes,
            envelope => envelope.WorkflowExecutionId == run.Identity.ChildWorkflowExecutionId &&
                        envelope.Command.Kind == WorkflowExecutionCommandKind.Start);
    }

    [Fact]
    public async Task Detached_exhaustion_redrive_and_success_reuse_the_original_child_without_reopening_parent()
    {
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync();
        var run = await fixture.StartParentAsync(
            caseId: "detached-redrive",
            parentWorkflowExecutionId: "parent-detached-redrive",
            parentCorrelationId: "correlation-parent");
        var parentBefore = Assert.IsType<WorkflowExecutionState>(await fixture.FindWorkflowAsync(run.Start.WorkflowExecutionId));
        var parentEnvelopeCount = fixture.Actors.Envelopes.Count(item =>
            item.WorkflowExecutionId == run.Start.WorkflowExecutionId);

        await using var scope = fixture.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(
            PersistenceAccessContext.Scoped(new PersistenceScope(run.Dispatch.TenantId!)));
        var timeProvider = fixture.Services.GetRequiredService<TimeProvider>();
        var outboxStore = scope.ServiceProvider.GetRequiredService<IRuntimePostCommitOutboxStore>();
        var dispatchStore = scope.ServiceProvider.GetRequiredService<IWorkflowDispatchStore>();
        var original = Assert.Single(await outboxStore.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(
            timeProvider.GetUtcNow(),
            10,
            intentKind: DispatchWorkflowConstants.StartChildIntentKind)));
        var unavailable = new FlakyStartDispatcher(
            scope.ServiceProvider.GetRequiredService<IWorkflowStartDispatcher>(),
            transientFailureCount: int.MaxValue);
        var failingProcessor = new RuntimePostCommitOutboxProcessor(
            outboxStore,
            new HandlerIntentDispatcher(new ChildStartExecutor(unavailable, dispatchStore, timeProvider)),
            timeProvider,
            DefaultRuntimeFaultCapturePolicy.CreateDefault(),
            dispatchStore,
            [new WorkflowDispatchDeliveryFailureProjector(dispatchStore)],
            logger: null);

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            var result = await failingProcessor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(
                10,
                intentKind: DispatchWorkflowConstants.StartChildIntentKind));
            Assert.Equal(
                attempt < 4 ? RuntimePostCommitOutboxStatus.FailedRetryable : RuntimePostCommitOutboxStatus.FailedFinal,
                Assert.Single(result.Items).RequestedDeliveryResultStatus);
            if (attempt < 4)
                fixture.AdvanceTime(TimeSpan.FromSeconds(1));
        }

        var failed = Assert.IsType<WorkflowDispatchRecord>(await dispatchStore.FindAsync(run.Dispatch.DispatchId));
        Assert.Equal(WorkflowDispatchStatus.DispatchFailed, failed.Status);
        Assert.True(WorkflowDispatchLifecycle.IsRedriveEligible(failed));
        var deadLetter = Assert.IsType<RuntimePostCommitOutboxItem>(await ((IPostCommitOutboxLookupStore)outboxStore)
            .FindAsync(original.OutboxItemId));
        Assert.Equal(RuntimePostCommitOutboxStatus.FailedFinal, deadLetter.Status);

        var redrive = await scope.ServiceProvider.GetRequiredService<IWorkflowDispatchRedriveStore>().RedriveAsync(
            new WorkflowDispatchRedriveRequest(run.Dispatch.DispatchId, "operator-request-1", timeProvider.GetUtcNow()));
        Assert.Equal(WorkflowDispatchRedriveDisposition.Accepted, redrive.Disposition);
        Assert.Equal(1, redrive.Generation);
        var redriven = Assert.IsType<RuntimePostCommitOutboxItem>(await ((IPostCommitOutboxLookupStore)outboxStore)
            .FindAsync(original.OutboxItemId));
        Assert.Equal(RuntimePostCommitOutboxStatus.Pending, redriven.Status);
        Assert.Equal(original.Intent.IntentId, redriven.Intent.IntentId);
        Assert.Equal(original.Intent.IdempotencyKey, redriven.Intent.IdempotencyKey);
        Assert.Equal(original.Intent.Payload, redriven.Intent.Payload);
        Assert.True(original.RetryPolicy.IsEquivalentTo(redriven.RetryPolicy));

        var successfulProcessor = new RuntimePostCommitOutboxProcessor(
            outboxStore,
            new HandlerIntentDispatcher(new ChildStartExecutor(
                scope.ServiceProvider.GetRequiredService<IWorkflowStartDispatcher>(),
                dispatchStore,
                timeProvider)),
            timeProvider);
        var delivered = await successfulProcessor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(
            10,
            intentKind: DispatchWorkflowConstants.StartChildIntentKind));

        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, Assert.Single(delivered.Items).RequestedDeliveryResultStatus);
        Assert.NotNull(await fixture.FindWorkflowAsync(run.Identity.ChildWorkflowExecutionId));
        Assert.Single(
            fixture.Actors.Envelopes,
            envelope => envelope.WorkflowExecutionId == run.Identity.ChildWorkflowExecutionId &&
                        envelope.Command.Kind == WorkflowExecutionCommandKind.Start);
        var parentAfter = Assert.IsType<WorkflowExecutionState>(await fixture.FindWorkflowAsync(run.Start.WorkflowExecutionId));
        Assert.Equal(parentBefore.Status, parentAfter.Status);
        Assert.Equal(parentBefore.UpdatedAt, parentAfter.UpdatedAt);
        Assert.Equal(parentEnvelopeCount, fixture.Actors.Envelopes.Count(item =>
            item.WorkflowExecutionId == run.Start.WorkflowExecutionId));
    }

    [Fact]
    public async Task Wait_exhaustion_resumes_parent_once_as_safe_dispatch_failed_and_is_never_redrivable()
    {
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync();
        var run = await fixture.StartParentAsync(
            caseId: "wait-delivery-exhaustion",
            parentWorkflowExecutionId: "parent-wait-delivery-exhaustion",
            parentCorrelationId: "correlation-parent",
            waitForCompletion: true);
        await using var scope = fixture.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(
            PersistenceAccessContext.Scoped(new PersistenceScope(run.Dispatch.TenantId!)));
        var timeProvider = fixture.Services.GetRequiredService<TimeProvider>();
        var outboxStore = scope.ServiceProvider.GetRequiredService<IRuntimePostCommitOutboxStore>();
        var dispatchStore = scope.ServiceProvider.GetRequiredService<IWorkflowDispatchStore>();
        var unavailable = new FlakyStartDispatcher(
            scope.ServiceProvider.GetRequiredService<IWorkflowStartDispatcher>(),
            transientFailureCount: int.MaxValue);
        var processor = new RuntimePostCommitOutboxProcessor(
            outboxStore,
            new HandlerIntentDispatcher(new ChildStartExecutor(unavailable, dispatchStore, timeProvider)),
            timeProvider,
            DefaultRuntimeFaultCapturePolicy.CreateDefault(),
            dispatchStore,
            [new WorkflowDispatchDeliveryFailureProjector(dispatchStore)],
            logger: null);

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            await processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(
                10,
                intentKind: DispatchWorkflowConstants.StartChildIntentKind));
            if (attempt < 4)
                fixture.AdvanceTime(TimeSpan.FromSeconds(1));
        }

        var failed = Assert.IsType<WorkflowDispatchRecord>(await dispatchStore.FindAsync(run.Dispatch.DispatchId));
        Assert.Equal(WorkflowDispatchStatus.DispatchFailed, failed.Status);
        Assert.False(WorkflowDispatchLifecycle.IsRedriveEligible(failed));
        var beforeResumeRedrive = await scope.ServiceProvider.GetRequiredService<IWorkflowDispatchRedriveStore>().RedriveAsync(
            new WorkflowDispatchRedriveRequest(run.Dispatch.DispatchId, "wait-redrive-before", timeProvider.GetUtcNow()));
        Assert.Equal(WorkflowDispatchRedriveDisposition.NotEligible, beforeResumeRedrive.Disposition);
        var resumeItem = Assert.Single(await outboxStore.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(
            timeProvider.GetUtcNow(),
            10,
            intentKind: DispatchWorkflowConstants.ResumeParentIntentKind)));

        var sweep = await fixture.SweepAsync();
        Assert.Equal(1, sweep.OutboxDeliveredCount);
        Assert.Equal(WorkflowExecutionStatus.Completed, (await fixture.FindWorkflowAsync(run.Start.WorkflowExecutionId))?.Status);
        Assert.Null(await fixture.FindWorkflowAsync(run.Identity.ChildWorkflowExecutionId));
        var activity = Assert.Single(await fixture.ListActivitiesAsync(run.Start.WorkflowExecutionId));
        Assert.Equal(ActivityExecutionStatus.Completed, activity.Status);
        Assert.Equal(
            new[] { DispatchWorkflowOutcomes.DispatchFailed },
            JsonSerializer.Deserialize<string[]>(activity.Metadata[RuntimeMetadataKeys.CompletionOutcomeNames]));
        var values = await fixture.ListDurableValuesAsync(run.Start.WorkflowExecutionId);
        var resultValue = Assert.Single(values, value =>
            value.DurableValueId.Contains("dispatch-result", StringComparison.Ordinal));
        var result = Assert.IsType<DispatchWorkflowResult>(resultValue.InlineValue!.Value.Deserialize<DispatchWorkflowResult>(SerializerOptions));
        Assert.Equal(WorkflowDispatchStatus.DispatchFailed, result.Status);
        Assert.Empty(result.Outputs);
        Assert.Equal(
            WorkflowDispatchLifecycle.ReadDeliveryIncidentId(failed),
            result.DiagnosticMetadata[DispatchWorkflowDiagnostics.DeliveryIncidentIdKey]);
        Assert.DoesNotContain("provider", resultValue.InlineValue.Value.GetRawText(), StringComparison.OrdinalIgnoreCase);

        var parentResume = scope.ServiceProvider.GetRequiredService<ParentResumeExecutor>();
        await parentResume.HandleAsync(resumeItem.Intent);
        await parentResume.HandleAsync(resumeItem.Intent);
        Assert.Equal(WorkflowExecutionStatus.Completed, (await fixture.FindWorkflowAsync(run.Start.WorkflowExecutionId))?.Status);
        Assert.Single(await fixture.ListActivitiesAsync(run.Start.WorkflowExecutionId));
        var afterResumeRedrive = await scope.ServiceProvider.GetRequiredService<IWorkflowDispatchRedriveStore>().RedriveAsync(
            new WorkflowDispatchRedriveRequest(run.Dispatch.DispatchId, "wait-redrive-after", timeProvider.GetUtcNow()));
        Assert.Equal(WorkflowDispatchRedriveDisposition.NotEligible, afterResumeRedrive.Disposition);
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

    private static WorkflowTestScope NewTestScope(string scopeId) =>
        new(
            scopeId,
            DispatchWorkflowRuntimeTestFixture.Now.AddMinutes(30),
            "tenant-42",
            new WorkflowExecutionPartition("partition-eu"));

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

    private sealed class HandlerIntentDispatcher(ChildStartExecutor handler) : IRuntimePostCommitIntentDispatcher
    {
        public ValueTask DispatchAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default) =>
            handler.HandleAsync(intent, cancellationToken);
    }

    private sealed class FlakyStartDispatcher(
        IWorkflowStartDispatcher inner,
        int transientFailureCount) : IWorkflowStartDispatcher
    {
        internal List<WorkflowExecutionStartDispatchRequest> Requests { get; } = [];

        public ValueTask<WorkflowExecutionStartDispatchResult> DispatchAsync(
            WorkflowExecutionStartDispatchRequest request,
            WorkflowExecutableReferenceScope requiredScope = WorkflowExecutableReferenceScope.Published,
            WorkflowExecutionCommandDispatchOptions? dispatchOptions = null,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (Requests.Count <= transientFailureCount)
            {
                return ValueTask.FromException<WorkflowExecutionStartDispatchResult>(
                    new InvalidOperationException("transient provider outage"));
            }

            return inner.DispatchAsync(request, requiredScope, dispatchOptions, cancellationToken);
        }
    }
}
