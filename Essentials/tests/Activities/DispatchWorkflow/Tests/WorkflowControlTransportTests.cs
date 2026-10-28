using System.Text.Json;
using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Events;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.Testing;
using Elsa.Activities.DispatchWorkflow.Runtime.Services;
using Elsa.Activities.DispatchWorkflow.Runtime.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using Fixture = Elsa.Activities.DispatchWorkflow.Tests.WorkflowControlBoundaryTests.Fixture;

namespace Elsa.Activities.DispatchWorkflow.Tests;

/// <summary>Immutable source-authority/staging controls; caller inbox execution and durable recovery are separate gates.</summary>
public sealed class WorkflowControlTransportTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_terminal_checkpoint_carries_one_resume_with_exact_source_and_authority(bool authoredFault)
    {
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync(authoredFaultChild: authoredFault);
        var run = await fixture.StartParentAsync("terminal-source", "terminal-source-parent", "correlation",
            waitForCompletion: true, controlConsumerVersion: 1);
        await fixture.SweepAsync();
        await fixture.SweepAsync();
        var checkpoint = Assert.Single(fixture.Checkpoints.Commits, commit =>
            commit.WorkflowExecutionId == run.Dispatch.ChildWorkflowExecutionId &&
            commit.StateChanges.WorkflowExecution?.State.Status == (authoredFault ? WorkflowExecutionStatus.Faulted : WorkflowExecutionStatus.Completed));
        var intent = Assert.Single(CheckpointCommitIntents.IntentsOf(checkpoint), intent => intent.Kind == DispatchWorkflowConstants.ResumeParentIntentKind);
        var payload = intent.Payload!.Value.Deserialize<WorkflowDispatchParentResumePayload>(Json)!;
        var carried = Assert.IsType<RuntimeChildControlTransport>(payload.Result.Control);
        Assert.Equal(checkpoint.CommitId, carried.SourceCommitId);
        Assert.Equal(intent.IntentId, carried.SourceIntentId);
        Assert.Equal(RuntimePostCommitOutboxIdentity.CreateLogicalValue(checkpoint.CommitId, intent.IntentId), carried.SourceOutboxItemId);
        Assert.Equal(authoredFault ? WorkflowDispatchStatus.Faulted : WorkflowDispatchStatus.Completed, payload.Result.Status);
        if (authoredFault)
        {
            Assert.Equal(DispatchWorkflowDiagnostics.FaultedCode, payload.Result.DiagnosticMetadata["code"]);
            Assert.Equal("authored-error", carried.Batch.AuthoredFailureCode);
            Assert.Equal(["before-error", "alongside-error"], carried.Batch.Notifications.Select(item => item.Code));
            var incident = Assert.Single(await fixture.Services.GetRequiredService<IIncidentStateStore>().ListAsync(run.Dispatch.ChildWorkflowExecutionId));
            Assert.True(incident.IsBlocking);
            Assert.DoesNotContain("authored-error", JsonSerializer.Serialize(payload.Result.DiagnosticMetadata));
        }
        else
            Assert.Empty(payload.Result.DiagnosticMetadata);
        Assert.Equal(0, carried.Batch.Ordinal);
        Assert.True(carried.Batch.IsTerminal);
        await using var scope = fixture.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IRuntimeChildControlAuthorizer>().ValidateAsync(carried);
        var parentLeaf = (await fixture.ListActivitiesAsync(run.Dispatch.ParentWorkflowExecutionId))
            .Single(state => state.InvocationId == run.Dispatch.ParentActivityExecutionId);
        Assert.Equal(ActivityExecutionStatus.Completed, parentLeaf.Status);
        Assert.Null(parentLeaf.Fault);
        Assert.Equal(JsonSerializer.Serialize(carried, Json), JsonSerializer.Serialize(parentLeaf.ChildControl, Json));
        var leafCompletion = Assert.Single(fixture.Checkpoints.Commits, commit => commit.StateChanges.ActivityExecutions.Any(change =>
            change.State.InvocationId == parentLeaf.InvocationId && change.State.Status == ActivityExecutionStatus.Completed && change.State.ChildControlInbox is null));
        Assert.Equal(JsonSerializer.Serialize(carried, Json), JsonSerializer.Serialize(Assert.Single(leafCompletion.StateChanges.ActivityExecutions,
            change => change.State.InvocationId == parentLeaf.InvocationId).State.ChildControl, Json));
        Assert.True(parentLeaf.ChildControlInbox!.IsTerminalConsumed);
        Assert.Equal(1, parentLeaf.ChildControlInbox.NextOrdinal);
        Assert.Contains(fixture.Checkpoints.Commits, commit =>
            commit.StateChanges.ActivityExecutions.Any(change => change.State.InvocationId == parentLeaf.InvocationId && change.State.ChildControlInbox?.IsTerminalConsumed == true) &&
            commit.StateChanges.ActivityExecutions.Any(change => change.State.InvocationId == carried.ConsumerActivityExecutionId && change.State.Status == ActivityExecutionStatus.Completed));
    }

    [Fact]
    public async Task Actual_running_root_commits_one_whole_nonterminal_intent_and_its_source_anchor()
    {
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync(authoredNotificationChild: true);
        var run = await fixture.StartParentAsync("running-source", "running-source-parent", "correlation",
            waitForCompletion: true, controlConsumerVersion: 1);
        await fixture.SweepAsync();
        var workflow = (await fixture.FindWorkflowAsync(run.Dispatch.ChildWorkflowExecutionId))!;
        var incidents = await fixture.Services.GetRequiredService<IIncidentStateStore>().ListAsync(run.Dispatch.ChildWorkflowExecutionId);
        Assert.True(workflow.Status == WorkflowExecutionStatus.Running, string.Join(" | ", incidents.Select(incident => incident.FailureType + ":" + incident.Message)));
        var checkpoints = fixture.Checkpoints.Commits.Where(commit => commit.WorkflowExecutionId == run.Dispatch.ChildWorkflowExecutionId).ToArray();
        var intents = CheckpointCommitIntents.IntentsOf(checkpoints).Where(intent => intent.Kind.EndsWith(".NotifyControl", StringComparison.Ordinal)).ToArray();
        var intent = Assert.Single(intents);
        var carried = intent.Payload!.Value.Deserialize<RuntimeChildControlTransport>(Json)!;
        Assert.False(carried.Batch.IsTerminal);
        Assert.Equal(["first", "second"], carried.Batch.Notifications.Select(item => item.Code));
        await using var scope = fixture.Services.CreateAsyncScope();
        var source = (await scope.ServiceProvider.GetRequiredService<IPostCommitOutboxLookupStore>().FindAsync(carried.SourceOutboxItemId))!;
        Assert.Equal(source.Metadata[RuntimeMetadataKeys.SchedulerWorkItemId], carried.Batch.SourceWorkItemId);
        Assert.Equal(RuntimePostCommitOutboxIdentity.CreateLogicalValue(carried.SourceCommitId, intent.IntentId), carried.SourceOutboxItemId);
        Assert.Equal(RuntimePostCommitOutboxStatus.Pending, source.Status);
        // Coalescing may fold the logical source checkpoint into a later outer commit. The exact source row
        // and its matching root anchor must still share one provider commit.
        Assert.Contains(checkpoints, commit => commit.StateChanges.PostCommitOutbox.Any(change => change.StateId == carried.SourceOutboxItemId) &&
            commit.StateChanges.ActivityExecutions.Any(change => change.State.ProducedControlSourceCommitId == carried.SourceCommitId));
        var root = Assert.Single(await fixture.ListActivitiesAsync(run.Dispatch.ChildWorkflowExecutionId), state => state.ParentActivityExecutionId is null);
        Assert.Equal(carried.SourceCommitId, root.ProducedControlSourceCommitId);
        Assert.Equal(JsonSerializer.Serialize(carried.Batch, Json), JsonSerializer.Serialize(root.ProducedControl, Json));
        await scope.ServiceProvider.GetRequiredService<IRuntimeChildControlAuthorizer>().ValidateAsync(carried);
        Assert.DoesNotContain(intents, item => item.Kind == DispatchWorkflowConstants.ResumeParentIntentKind);
    }

    [Theory]
    [InlineData("omitted")]
    [InlineData("outputs")]
    [InlineData("batch")]
    [InlineData("dispatch-failed")]
    public async Task Actual_leaf_refuses_an_omitted_or_forged_terminal_result_before_completing(string scenario)
    {
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync();
        var run = await fixture.StartParentAsync("forged-terminal-" + scenario, "forged-parent-" + scenario, "correlation",
            waitForCompletion: true, controlConsumerVersion: 1);
        await fixture.SweepAsync();
        var checkpoint = fixture.Checkpoints.SingleIntentCommit(DispatchWorkflowConstants.ResumeParentIntentKind);
        var original = Assert.Single(CheckpointCommitIntents.IntentsOf(checkpoint), intent => intent.Kind == DispatchWorkflowConstants.ResumeParentIntentKind);
        var payload = original.Payload!.Value.Deserialize<WorkflowDispatchParentResumePayload>(Json)!;
        var control = payload.Result.Control!;
        var result = new DispatchWorkflowResult(payload.ChildWorkflowExecutionId, scenario == "dispatch-failed" ? WorkflowDispatchStatus.DispatchFailed : payload.Result.Status,
            scenario == "outputs" ? [new DispatchWorkflowOutput("forged-output", JsonSerializer.SerializeToElement(42), "Int32", false)] : [],
            scenario == "dispatch-failed" ? new Dictionary<string, string>
            {
                [DispatchWorkflowDiagnostics.CodeKey] = DispatchWorkflowDiagnostics.DispatchFailedCode,
                [DispatchWorkflowDiagnostics.CategoryKey] = DispatchWorkflowDiagnostics.DeliveryCategory,
                [DispatchWorkflowDiagnostics.SummaryKey] = DispatchWorkflowDiagnostics.DispatchFailedSummary,
                [DispatchWorkflowDiagnostics.DeliveryIncidentIdKey] = "forged-incident"
            } : payload.Result.DiagnosticMetadata, scenario is "omitted" or "dispatch-failed" ? null : scenario == "batch" ? Copy(control, "batch") : control);
        var forgedPayload = new WorkflowDispatchParentResumePayload(payload.DispatchId, payload.ParentWorkflowExecutionId,
            payload.ParentActivityExecutionId, payload.ChildWorkflowExecutionId, payload.BookmarkId, payload.StimulusType, payload.StimulusHash, result);
        var forged = new RuntimePostCommitIntent(original.IntentId, original.WorkflowExecutionId, original.Kind, original.RecordedAt,
            original.ActivityExecutionId, original.IdempotencyKey, JsonSerializer.SerializeToElement(forgedPayload, Json), original.Metadata);
        await using var scope = fixture.Services.CreateAsyncScope();
        var lookup = scope.ServiceProvider.GetRequiredService<IPostCommitOutboxLookupStore>();
        var before = (await lookup.FindAsync(control.SourceOutboxItemId))!;
        await Assert.ThrowsAsync<ParentResumeDeferredException>(async () =>
            await scope.ServiceProvider.GetRequiredService<ParentResumeExecutor>().HandleAsync(forged));
        var leaf = (await fixture.ListActivitiesAsync(payload.ParentWorkflowExecutionId)).Single(state => state.InvocationId == payload.ParentActivityExecutionId);
        Assert.NotEqual(ActivityExecutionStatus.Completed, leaf.Status);
        Assert.Null(leaf.ChildControlInbox);
        Assert.True(before.Intent.IsEquivalentTo((await lookup.FindAsync(control.SourceOutboxItemId))!.Intent));
        Assert.DoesNotContain(fixture.Checkpoints.Commits, commit => commit.StateChanges.ActivityExecutions.Any(change =>
            change.State.InvocationId == leaf.InvocationId && change.State.Status == ActivityExecutionStatus.Completed));
    }

    [Theory]
    [InlineData("io")]
    [InlineData("json")]
    [InlineData("cancel")]
    public async Task Pre_resume_source_read_preserves_failure_classification_and_original_cause(string failureKind)
    {
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync();
        var run = await fixture.StartParentAsync("guard-source-" + failureKind, "guard-source-parent-" + failureKind, "correlation",
            waitForCompletion: true, controlConsumerVersion: 1);
        await fixture.SweepAsync();
        var original = Assert.Single(CheckpointCommitIntents.IntentsOf(fixture.Checkpoints.SingleIntentCommit(DispatchWorkflowConstants.ResumeParentIntentKind)),
            intent => intent.Kind == DispatchWorkflowConstants.ResumeParentIntentKind);
        var message = original.Payload!.Value.Deserialize<WorkflowDispatchParentResumePayload>(Json)!;
        await using var scope = fixture.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IRuntimeChildControlAuthorizer>().ValidateAsync(message.Result.Control!);
        Exception cause = failureKind switch { "io" => new IOException("Source read failed."),
            "json" => new JsonException("Source read is malformed."), _ => new OperationCanceledException() };
        var lookup = new SourceLookup { Failure = cause };
        var template = Assert.Single(scope.ServiceProvider.GetServices<IEventHandler<ActivityResumeValidating>>());
        var guard = (IEventHandler<ActivityResumeValidating>)ActivatorUtilities.CreateInstance(scope.ServiceProvider, template.GetType(), lookup);
        var state = (await fixture.Services.GetRequiredService<IActivityExecutionStateStore>().FindAsync(message.ParentWorkflowExecutionId, message.ParentActivityExecutionId))!;
        var pin = (await fixture.FindWorkflowAsync(message.ParentWorkflowExecutionId))!.PinnedExecutable;
        var node = (await fixture.Services.GetRequiredService<IWorkflowExecutableStore>().FindAsync(pin.ArtifactId))!.NodesById[state.Execution.ExecutableNodeId];
        var bookmark = (await fixture.FindBookmarkAsync(message.ParentWorkflowExecutionId, message.BookmarkId))!;
        var payload = new RuntimeResumeBookmarkCommandPayload(pin, message.BookmarkId, state.InvocationId, node.ExecutableNodeId,
            bookmark.ResumeTargetId, message.StimulusType, message.StimulusHash, JsonSerializer.SerializeToElement(message, Json),
            RuntimeResumeBookmarkCommandPayload.StimulusMatchedReason);
        if (failureKind is "io" or "cancel")
        {
            var unavailable = await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(async () => await guard.Handle(new ActivityResumeValidating(state, node, payload), default));
            var failure = Assert.IsType<WorkflowControlBoundaryException>(unavailable.InnerException);
            Assert.True(failure.IsUnavailable);
            Assert.Same(cause, failure.InnerException);
        }
        else
        {
            var failure = await Assert.ThrowsAsync<WorkflowControlBoundaryException>(async () => await guard.Handle(new ActivityResumeValidating(state, node, payload), default));
            Assert.False(failure.IsUnavailable);
            Assert.Same(cause, failure.InnerException);
        }
    }

    [Fact]
    public async Task Nonterminal_notification_is_one_deterministic_intent_with_whole_ordered_batch()
    {
        var source = new SourceLookup();
        await using var fixture = await CreateAsync(source);
        var commit = await NotificationCommitAsync(fixture);
        var first = await NotificationEnricher(fixture).EnrichAsync(commit);
        var intent = Assert.Single(first.PostCommitIntents);
        var transport = intent.Payload!.Value.Deserialize<RuntimeChildControlTransport>(Json)!;
        Assert.False(transport.Batch.IsTerminal);
        Assert.Equal(["first", "second"], transport.Batch.Notifications.Select(notification => notification.Code));
        Assert.Equal(commit.CommitId, transport.SourceCommitId);
        Assert.Equal(intent.IntentId, transport.SourceIntentId);
        source.Item = Item(transport.SourceOutboxItemId, intent);
        await fixture.Services.GetRequiredService<IActivityExecutionStateStore>().SaveAsync(Assert.Single(first.StateChanges.ActivityExecutions).State);
        await Authorizer(fixture).ValidateAsync(transport);
        var replay = await NotificationEnricher(fixture).EnrichAsync(commit);
        Assert.True(intent.IsEquivalentTo(Assert.Single(replay.PostCommitIntents)));
        Assert.Equal(JsonSerializer.Serialize(first, Json), JsonSerializer.Serialize(replay, Json));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Re_enrichment_before_provider_commit_keeps_the_equivalent_staged_intent(bool afterPriorBatch)
    {
        await using var fixture = await CreateAsync(new SourceLookup());
        var commit = await NotificationCommitAsync(fixture);
        if (afterPriorBatch)
        {
            var prior = await NotificationEnricher(fixture).EnrichAsync(commit);
            var priorRoot = Assert.Single(prior.StateChanges.ActivityExecutions).State;
            await fixture.Services.GetRequiredService<IActivityExecutionStateStore>().SaveAsync(priorRoot);
            var batch = priorRoot.ProducedControl!;
            var next = new RuntimeChildControlBatch(1, batch.ConsumerKey, batch.ConsumerVersion, batch.ChildWorkflowExecutionId,
                batch.RootActivityExecutionId, "next-work", 1, false, [new("next")]);
            commit = commit with { CommitId = "next-commit", Metadata = new Dictionary<string, string> { [RuntimeMetadataKeys.SchedulerWorkItemId] = "next-work" },
                StateChanges = commit.StateChanges.WithActivityExecutions([new(priorRoot.InvocationId, RuntimeStateChangeOperation.Upsert,
                    priorRoot with { ProducedControl = next, ProducedControlSourceCommitId = null }, commit.Metadata)]) };
        }
        var first = await NotificationEnricher(fixture).EnrichAsync(commit);
        var repeated = await NotificationEnricher(fixture).EnrichAsync(first);
        Assert.Equal(JsonSerializer.Serialize(first, Json), JsonSerializer.Serialize(repeated, Json));
    }

    [Fact]
    public async Task A_new_projection_cannot_claim_an_uncommitted_foreign_source_anchor()
    {
        await using var fixture = await CreateAsync(new SourceLookup());
        var commit = await NotificationCommitAsync(fixture);
        var change = Assert.Single(commit.StateChanges.ActivityExecutions);
        commit = commit with { StateChanges = commit.StateChanges.WithActivityExecutions([change with
            { State = change.State with { ProducedControlSourceCommitId = "foreign-uncommitted-source" } }]) };
        await Assert.ThrowsAsync<WorkflowControlBoundaryException>(async () => await NotificationEnricher(fixture).EnrichAsync(commit));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Retained_nonterminal_projection_does_not_emit_again_in_a_later_commit(bool sameWork)
    {
        var source = new SourceLookup();
        await using var fixture = await CreateAsync(source);
        var commit = await NotificationCommitAsync(fixture);
        var first = await NotificationEnricher(fixture).EnrichAsync(commit);
        var intent = Assert.Single(first.PostCommitIntents);
        var carried = intent.Payload!.Value.Deserialize<RuntimeChildControlTransport>(Json)!;
        source.Item = Item(carried.SourceOutboxItemId, intent);
        await fixture.Services.GetRequiredService<IActivityExecutionStateStore>().SaveAsync(Assert.Single(first.StateChanges.ActivityExecutions).State);
        var later = commit with { CommitId = "later-commit", Metadata = new Dictionary<string, string>
            { [RuntimeMetadataKeys.SchedulerWorkItemId] = sameWork ? "source-work" : "later-work" } };
        var repeated = await NotificationEnricher(fixture).EnrichAsync(later);
        Assert.Empty(repeated.PostCommitIntents);
        Assert.Equal(commit.CommitId, Assert.Single(repeated.StateChanges.ActivityExecutions).State.ProducedControlSourceCommitId);
    }

    [Fact]
    public async Task An_anchored_source_cannot_be_reconstructed_when_its_outbox_authority_is_missing()
    {
        var source = new SourceLookup();
        await using var fixture = await CreateAsync(source);
        var commit = await NotificationCommitAsync(fixture);
        var first = await NotificationEnricher(fixture).EnrichAsync(commit);
        await fixture.Services.GetRequiredService<IActivityExecutionStateStore>().SaveAsync(Assert.Single(first.StateChanges.ActivityExecutions).State);
        await Assert.ThrowsAsync<WorkflowControlBoundaryException>(async () => await NotificationEnricher(fixture).EnrichAsync(commit));
    }

    [Fact]
    public async Task An_old_anchored_checkpoint_cannot_reconstruct_missing_authority_after_root_advancement()
    {
        await using var fixture = await CreateAsync(new SourceLookup());
        var original = await NotificationEnricher(fixture).EnrichAsync(await NotificationCommitAsync(fixture));
        var change = Assert.Single(original.StateChanges.ActivityExecutions);
        var old = change.State.ProducedControl!;
        await fixture.Services.GetRequiredService<IActivityExecutionStateStore>().SaveAsync(change.State with
        {
            ProducedControl = new RuntimeChildControlBatch(1, old.ConsumerKey, old.ConsumerVersion, old.ChildWorkflowExecutionId,
                old.RootActivityExecutionId, "next-work", 1, false, [new("later")]),
            ProducedControlSourceCommitId = "next-commit"
        });
        await Assert.ThrowsAsync<WorkflowControlBoundaryException>(async () => await NotificationEnricher(fixture).EnrichAsync(original));
    }

    [Fact]
    public async Task A_new_batch_with_a_foreign_source_work_is_refused_before_intent_staging()
    {
        await using var fixture = await CreateAsync(new SourceLookup());
        var commit = await NotificationCommitAsync(fixture);
        commit = commit with { Metadata = new Dictionary<string, string> { [RuntimeMetadataKeys.SchedulerWorkItemId] = "foreign-work" } };
        await Assert.ThrowsAsync<WorkflowControlBoundaryException>(async () => await NotificationEnricher(fixture).EnrichAsync(commit));
    }

    [Fact]
    public async Task Terminal_root_batch_never_emits_a_separate_notification()
    {
        await using var fixture = await CreateAsync(new SourceLookup());
        var commit = await NotificationCommitAsync(fixture, terminal: true);
        Assert.Empty((await NotificationEnricher(fixture).EnrichAsync(commit)).PostCommitIntents);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("batch")]
    [InlineData("ordinal")]
    [InlineData("consumer")]
    [InlineData("pin")]
    [InlineData("intent-kind")]
    [InlineData("outbox-id")]
    public async Task Source_authority_refuses_missing_or_forged_control(string scenario)
    {
        var source = new SourceLookup();
        await using var fixture = await CreateAsync(source);
        var enriched = await NotificationEnricher(fixture).EnrichAsync(await NotificationCommitAsync(fixture));
        var intent = Assert.Single(enriched.PostCommitIntents);
        var carried = intent.Payload!.Value.Deserialize<RuntimeChildControlTransport>(Json)!;
        source.Item = scenario == "missing" ? null : Item(scenario == "outbox-id" ? "foreign-source" : carried.SourceOutboxItemId,
            scenario == "intent-kind" ? new RuntimePostCommitIntent(intent.IntentId, intent.WorkflowExecutionId, "foreign.kind",
                intent.RecordedAt, intent.ActivityExecutionId, intent.IdempotencyKey, intent.Payload) : intent);
        if (scenario is "batch" or "ordinal" or "consumer" or "pin")
            carried = Copy(carried, scenario);
        await Assert.ThrowsAsync<WorkflowControlBoundaryException>(async () => await Authorizer(fixture).ValidateAsync(carried));
    }

    [Fact]
    public async Task Replay_refuses_a_conflicting_committed_notification()
    {
        var source = new SourceLookup();
        await using var fixture = await CreateAsync(source);
        var commit = await NotificationCommitAsync(fixture);
        var intent = Assert.Single((await NotificationEnricher(fixture).EnrichAsync(commit)).PostCommitIntents);
        var carried = intent.Payload!.Value.Deserialize<RuntimeChildControlTransport>(Json)!;
        source.Item = Item(carried.SourceOutboxItemId, new RuntimePostCommitIntent(intent.IntentId, intent.WorkflowExecutionId,
            intent.Kind, intent.RecordedAt, intent.ActivityExecutionId, intent.IdempotencyKey,
            JsonSerializer.SerializeToElement(Copy(carried, "batch"), Json)));
        await Assert.ThrowsAsync<WorkflowControlBoundaryException>(async () => await NotificationEnricher(fixture).EnrichAsync(commit));
    }

    [Fact]
    public async Task Source_store_failure_keeps_context_and_cancellation_passes_through()
    {
        var source = new SourceLookup();
        await using var fixture = await CreateAsync(source);
        var intent = Assert.Single((await NotificationEnricher(fixture).EnrichAsync(await NotificationCommitAsync(fixture))).PostCommitIntents);
        var carried = intent.Payload!.Value.Deserialize<RuntimeChildControlTransport>(Json)!;
        source.Failure = new IOException("Injected exact-source lookup failure.");
        var failure = await Assert.ThrowsAsync<WorkflowControlBoundaryException>(async () => await Authorizer(fixture).ValidateAsync(carried));
        Assert.Same(source.Failure, failure.InnerException);
        source.Failure = new OperationCanceledException();
        Assert.Same(source.Failure, await Assert.ThrowsAsync<OperationCanceledException>(async () => await Authorizer(fixture).ValidateAsync(carried)));
    }

    [Fact]
    public async Task Scoped_authorizer_override_is_preserved_and_default_is_single_per_scope()
    {
        await using var replaced = await Fixture.CreateAsync("valid", beforeFeature: services =>
            services.AddScoped<IRuntimeChildControlAuthorizer, ReplacementAuthorizer>());
        await replaced.InitializeControlAsync();
        Assert.IsType<ReplacementAuthorizer>(Authorizer(replaced));
        await using var fixture = await CreateAsync(new SourceLookup());
        using var first = fixture.Services.CreateScope();
        using var second = fixture.Services.CreateScope();
        var authorizer = first.ServiceProvider.GetRequiredService<IRuntimeChildControlAuthorizer>();
        Assert.Same(authorizer, first.ServiceProvider.GetRequiredService<IRuntimeChildControlAuthorizer>());
        Assert.NotSame(authorizer, second.ServiceProvider.GetRequiredService<IRuntimeChildControlAuthorizer>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Startup_refuses_missing_or_duplicate_authorizers(bool duplicate)
    {
        await using var fixture = await Fixture.CreateAsync("valid", afterFeature: services =>
        {
            if (duplicate) services.AddScoped<IRuntimeChildControlAuthorizer, ReplacementAuthorizer>();
            else services.RemoveAll<IRuntimeChildControlAuthorizer>();
        });
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.InitializeControlAsync());
        Assert.Contains("exactly one child-control authorizer", failure.Message);
    }

    private sealed class ReplacementAuthorizer : IRuntimeChildControlAuthorizer
    {
        public ReplacementAuthorizer() { }
        public ValueTask ValidateAsync(RuntimeChildControlTransport transport, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private static Task<Fixture> CreateAsync(SourceLookup source) => Fixture.CreateAsync("valid", afterFeature: services =>
        services.Replace(ServiceDescriptor.Singleton<IPostCommitOutboxLookupStore>(source)));
    private static IRuntimeCheckpointCommitEnricher NotificationEnricher(Fixture fixture) =>
        fixture.Services.GetServices<IRuntimeCheckpointCommitEnricher>().Single(enricher => enricher.Order == 90);
    private static IRuntimeChildControlAuthorizer Authorizer(Fixture fixture) => fixture.Services.GetRequiredService<IRuntimeChildControlAuthorizer>();
    private static RuntimePostCommitOutboxItem Item(string id, RuntimePostCommitIntent intent) =>
        new(id, intent, RuntimePostCommitOutboxStatus.Pending, intent.RecordedAt, intent.RecordedAt);

    private static async ValueTask<RuntimeCheckpointCommit> NotificationCommitAsync(Fixture fixture, bool terminal = false)
    {
        var root = (await fixture.Services.GetRequiredService<IActivityExecutionStateStore>().FindAsync(fixture.Identity.ChildWorkflowExecutionId, "child-root"))!;
        var control = new RuntimeChildControlBatch(1, "test.control", 1, fixture.Identity.ChildWorkflowExecutionId, root.InvocationId,
            "source-work", 0, terminal, [new("first"), new("second")]);
        var metadata = new Dictionary<string, string> { [RuntimeMetadataKeys.SchedulerWorkItemId] = "source-work" };
        return new RuntimeCheckpointCommit("source-commit", new RuntimeCheckpoint("source-checkpoint", "ControlProjection",
            fixture.Identity.ChildWorkflowExecutionId, DateTimeOffset.UnixEpoch, [root.InvocationId], metadata),
            new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], []).WithActivityExecutions([new(root.InvocationId, RuntimeStateChangeOperation.Upsert,
                root with { ProducedControl = control }, metadata)]), [], metadata);
    }

    private static RuntimeChildControlTransport Copy(RuntimeChildControlTransport carried, string scenario) => new(carried.DispatchId,
        carried.ParentWorkflowExecutionId, carried.CallingActivityExecutionId,
        scenario == "consumer" ? "foreign-consumer" : carried.ConsumerActivityExecutionId,
        scenario == "pin" ? carried.ParentExecutable with { ArtifactHash = "foreign-hash" } : carried.ParentExecutable,
        carried.SourceCommitId, carried.SourceIntentId, carried.SourceOutboxItemId,
        new RuntimeChildControlBatch(1, carried.Batch.ConsumerKey, carried.Batch.ConsumerVersion, carried.Batch.ChildWorkflowExecutionId,
            carried.Batch.RootActivityExecutionId, carried.Batch.SourceWorkItemId,
            scenario == "ordinal" ? 1 : carried.Batch.Ordinal, carried.Batch.IsTerminal,
            scenario == "batch" ? [new("forged")] : carried.Batch.Notifications));

    private sealed class SourceLookup : IPostCommitOutboxLookupStore
    {
        internal RuntimePostCommitOutboxItem? Item { get; set; }
        internal Exception? Failure { get; set; }
        public ValueTask<RuntimePostCommitOutboxItem?> FindAsync(string outboxItemId, CancellationToken cancellationToken = default) =>
            Failure is null ? ValueTask.FromResult(Item) : ValueTask.FromException<RuntimePostCommitOutboxItem?>(Failure);
    }
}
