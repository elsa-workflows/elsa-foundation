using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Bookmarks;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Triggers;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Executables;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>Actual native source provider/router/dispatchers with a recording mailbox boundary; not callback/host proof.</summary>
public sealed class SourceOwnedStimulusRouterTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 4, 0, 0, TimeSpan.Zero);
    private static readonly PersistenceScope Scope = new("tenant");
    private static readonly WorkflowExecutableIdentity Pin = new("artifact", "definition", "version", "1.0.0", "hash");

    [Fact]
    public void Host_neutral_router_entrypoint_resolves_without_API_feature_or_copied_host_registration()
    {
        var services = new ServiceCollection().AddWorkflowRuntime().AddWorkflowStimulusRouter();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var first = provider.CreateScope(); using var second = provider.CreateScope();
        Assert.NotNull(first.ServiceProvider.GetRequiredService<IWorkflowTriggerBindingStore>());
        Assert.Same(first.ServiceProvider.GetRequiredService<IBookmarkStateStore>(), first.ServiceProvider.GetRequiredService<IBookmarkStimulusIndex>());
        Assert.NotSame(first.ServiceProvider.GetRequiredService<IStimulusRouter>(), second.ServiceProvider.GetRequiredService<IStimulusRouter>());
    }

    [Fact]
    public async Task Original_source_selector_default_literal_calls_remain_source_compatible()
    {
        var resolver = new WorkflowExecutableStartSourceResolver(new InMemoryWorkflowExecutableSourceReferenceStore(), new FakeTimeProvider(Now));
        var binding = new WorkflowTriggerBinding("binding", Pin.ArtifactId, Pin.DefinitionId, Pin.ArtifactVersion, Pin.ArtifactHash,
            "node", "event", "named", null, new Dictionary<string, string>(), Now);
        var captured = await resolver.CaptureAsync(binding, Pin, default);
        await resolver.ValidateCapturedAsync([captured], default);
        Assert.True(captured.SourceProvenanceCaptured); Assert.Null(captured.SourceProvenance);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Source_router_persists_all_bindings_and_exact_waits_before_effects_then_reuses_only_the_saved_winner(bool coalescing)
    {
        await using var harness = new Harness(coalescing); await harness.SeedMaterial();
        await harness.Bind("binding-a"); await harness.Bind("binding-b"); await harness.Wait("existing", "wait-a"); await harness.Wait("existing", "wait-b");
        var source = await harness.Source(); var created = false;
        harness.Actors.BeforeEnqueue = async envelope =>
        {
            var saved = (await harness.Store.FindAsync(source.SourceOutboxItemId))!.RecipientSnapshot!;
            Assert.Equal(2, saved.Starts.Count); Assert.Equal(2, saved.Resumes.Count);
            if (!created && envelope.Command.Kind == WorkflowExecutionCommandKind.Start)
            { created = true; await harness.Wait("fresh-start", "fresh-wait"); }
        };
        var first = await harness.Router.RouteSourceAsync(source);
        Assert.Equal(2, first.Starts.Count); Assert.Equal(2, first.Resumes.Count); Assert.Equal(1, first.StartedCount);
        Assert.Equal(1, harness.Lookup.Reads); Assert.Equal(1, harness.Actors.Accepted.Count(command => command.Command.Kind == WorkflowExecutionCommandKind.Start));
        Assert.Equal(2, harness.Actors.Accepted.Count(command => command.Command.Kind == WorkflowExecutionCommandKind.ResumeBookmark));
        var resumes = harness.Actors.Accepted.Where(command => command.Command.Kind == WorkflowExecutionCommandKind.ResumeBookmark)
            .Select(command => command.Command.Payload!.Value.Deserialize<RuntimeResumeBookmarkCommandPayload>()!).ToArray();
        Assert.Equal(new[] { "wait-a", "wait-b" }, resumes.Select(payload => payload.BookmarkId));
        Assert.All(resumes, payload => { Assert.NotNull(payload.ExpectedBookmarkResume!.RegistrationCapture); Assert.Equal("immutable-value", payload.Input!.Value.GetProperty("value").GetString()); });
        await harness.Bindings.DeleteByArtifactAsync(Pin.ArtifactId); await harness.Bind("new-binding");
        harness.Lookup.Fail = true;
        var retried = await harness.Router.RouteSourceAsync(source);
        Assert.Equal(2, retried.Starts.Count); Assert.All(retried.Starts, result => Assert.Equal(StimulusStartStatus.SkippedDuplicate, result.Status));
        Assert.Equal(2, retried.Resumes.Count); Assert.All(retried.Resumes, result => Assert.Equal(BookmarkResumeDispatchStatus.Duplicate, result.Status));
        Assert.Equal(3, harness.Actors.Accepted.Count); Assert.NotNull(await harness.Bookmarks.FindAsync("fresh-start", "fresh-wait"));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Source_router_preserves_authoritative_empty_after_later_listener_and_publication(bool coalescing)
    {
        await using var harness = new Harness(coalescing); await harness.SeedMaterial(); var source = await harness.Source();
        var first = await harness.Router.RouteSourceAsync(source); Assert.Empty(first.Starts); Assert.Empty(first.Resumes);
        await harness.Bind("late-binding"); await harness.Wait("late", "late-wait"); harness.Lookup.Fail = true;
        var retry = await harness.Router.RouteSourceAsync(source); Assert.Empty(retry.Starts); Assert.Empty(retry.Resumes);
        Assert.Empty(harness.Actors.Attempts); Assert.Equal(1, harness.Lookup.Reads);
        Assert.NotNull((await harness.Store.FindAsync(source.SourceOutboxItemId))!.RecipientSnapshot);
    }

    [Fact]
    public async Task Source_router_first_delivery_includes_a_wait_created_after_publisher_commit()
    {
        await using var harness = new Harness(); await harness.SeedMaterial(); var source = await harness.Source();
        harness.Clock.Advance(TimeSpan.FromSeconds(5)); await harness.Wait("delayed", "wait");
        var result = await harness.Router.RouteSourceAsync(source);
        Assert.Equal(BookmarkResumeDispatchStatus.Dispatched, Assert.Single(result.Resumes).Status);
        var saved = (await harness.Store.FindAsync(source.SourceOutboxItemId))!.RecipientSnapshot!;
        Assert.Equal(Now.AddSeconds(5), saved.SelectedAt); Assert.Equal(Now.AddSeconds(5), Assert.Single(saved.Resumes).CreatedAt);
    }

    [Fact]
    public async Task Source_router_keeps_same_artifact_bindings_after_first_shed_and_retries_the_original_key()
    {
        await using var harness = new Harness(); await harness.SeedMaterial(); await harness.Bind("binding-a"); await harness.Bind("binding-b");
        var source = await harness.Source(); harness.Actors.ShedStarts = 1;
        var result = await harness.Router.RouteSourceAsync(source);
        Assert.Collection(result.Starts, first => Assert.Equal(StimulusStartStatus.Shed, first.Status), second => Assert.Equal(StimulusStartStatus.Started, second.Status));
        Assert.Equal(1, result.ShedStartCount); Assert.Equal(harness.Actors.Attempts[0].IdempotencyKey, harness.Actors.Attempts[1].IdempotencyKey);
        harness.Lookup.Fail = true; var retried = await harness.Router.RouteSourceAsync(source);
        Assert.Equal(0, retried.ShedStartCount); Assert.All(retried.Starts, start => Assert.Equal(StimulusStartStatus.SkippedDuplicate, start.Status));
        Assert.Single(harness.Actors.Accepted);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Source_router_refuses_missing_adapter_or_material_before_snapshot_and_effects(bool missingAdapter)
    {
        await using var harness = new Harness(adapter: !missingAdapter); await harness.SeedMaterial();
        await harness.Bind("binding", missingAdapter ? Pin.ArtifactId : "missing-artifact"); var source = await harness.Source();
        await Assert.ThrowsAsync<StimulusDeliveryBoundaryException>(() => harness.Router.RouteSourceAsync(source).AsTask());
        Assert.Empty(harness.Actors.Attempts); Assert.Null((await harness.Store.FindAsync(source.SourceOutboxItemId))!.RecipientSnapshot);
        Assert.Equal(0, (await harness.Store.FindAsync(source.SourceOutboxItemId))!.DeliveryAttemptCount);
    }

    [Fact]
    public async Task Source_router_rearmed_identical_wait_is_excluded_after_the_cohort_was_committed()
    {
        await using var harness = new Harness(); await harness.SeedMaterial(); await harness.Bind("binding"); await harness.Wait("existing", "wait");
        var source = await harness.Source(); var old = (await harness.Bookmarks.ObserveAsync("existing", "wait"))!.Generation;
        harness.Actors.BeforeEnqueue = async envelope =>
        {
            if (envelope.Command.Kind != WorkflowExecutionCommandKind.Start) return;
            Assert.NotNull((await harness.Store.FindAsync(source.SourceOutboxItemId))!.RecipientSnapshot);
            await harness.Bookmarks.DeleteAsync("existing", "wait"); await harness.Wait("existing", "wait");
        };
        var result = await harness.Router.RouteSourceAsync(source);
        Assert.Equal(BookmarkResumeDispatchStatus.NotFound, Assert.Single(result.Resumes).Status);
        Assert.NotEqual(old, (await harness.Bookmarks.ObserveAsync("existing", "wait"))!.Generation);
        Assert.Single(harness.Actors.Accepted); Assert.Equal(WorkflowExecutionCommandKind.Start, harness.Actors.Accepted[0].Command.Kind);
    }

    [Fact]
    public async Task Source_router_lost_recipient_ack_retries_recorded_cohort_without_a_second_mailbox_acceptance()
    {
        await using var harness = new Harness(); await harness.SeedMaterial(); await harness.Wait("existing", "wait-a"); await harness.Wait("existing", "wait-b");
        var source = await harness.Source(); harness.Actors.LoseNextAck = true;
        var failure = await Assert.ThrowsAsync<StimulusDeliveryBoundaryException>(() => harness.Router.RouteSourceAsync(source).AsTask());
        Assert.True(failure.IsUnavailable); Assert.IsType<IOException>(failure.InnerException); Assert.Single(harness.Actors.Accepted);
        harness.Lookup.Fail = true;
        var retried = await harness.Router.RouteSourceAsync(source);
        Assert.Equal(2, retried.Resumes.Count); Assert.Equal(2, harness.Actors.Accepted.Count); Assert.Equal(3, harness.Actors.Attempts.Count);
        Assert.All(harness.Actors.Accepted, command => Assert.NotNull(command.Command.Payload!.Value.Deserialize<RuntimeResumeBookmarkCommandPayload>()!.ExpectedBookmarkResume));
    }

    [Theory]
    [InlineData(WorkflowExecutionCommandDispatchStatus.Deferred)] [InlineData(WorkflowExecutionCommandDispatchStatus.Rejected)]
    public async Task Source_router_non_shed_start_refusal_preserves_cohort_and_key_for_retry(WorkflowExecutionCommandDispatchStatus refusal)
    {
        await using var harness = new Harness(); await harness.SeedMaterial(); await harness.Bind("binding"); var source = await harness.Source();
        harness.Actors.RefuseNextStart = refusal;
        var failure = await Assert.ThrowsAsync<StimulusDeliveryBoundaryException>(() => harness.Router.RouteSourceAsync(source).AsTask());
        Assert.True(failure.IsUnavailable); Assert.Empty(harness.Actors.Accepted);
        var originalKey = Assert.Single(harness.Actors.Attempts).IdempotencyKey;
        var winner = (await harness.Store.FindAsync(source.SourceOutboxItemId))!.RecipientSnapshot!;
        Assert.Single(winner.Starts); harness.Lookup.Fail = true;
        var retry = await harness.Router.RouteSourceAsync(source);
        Assert.Equal(1, retry.StartedCount); Assert.Equal(originalKey, Assert.Single(harness.Actors.Accepted).IdempotencyKey);
        Assert.True(winner.IsEquivalentTo((await harness.Store.FindAsync(source.SourceOutboxItemId))!.RecipientSnapshot));
    }

    [Fact]
    public async Task Source_router_local_delivery_targets_only_the_canonical_publisher_and_never_starts()
    {
        await using var harness = new Harness(); await harness.SeedMaterial(); await harness.Bind("binding");
        await harness.Wait("publisher", "own", "A"); await harness.Wait("other", "other", "A");
        var source = await harness.Source(local: true, correlation: "A");
        var result = await harness.Router.RouteSourceAsync(source);
        Assert.Empty(result.Starts); Assert.Equal("publisher", Assert.Single(result.Resumes).WorkflowExecutionId);
        Assert.Equal("publisher", Assert.Single(harness.Actors.Accepted).WorkflowExecutionId);
    }

    [Fact]
    public async Task Source_router_correlation_narrows_waits_only_and_isolated_test_runs_are_excluded()
    {
        await using var harness = new Harness(); await harness.SeedMaterial(); await harness.Bind("binding", correlation: "B");
        await harness.Wait("existing", "match", "A"); await harness.Wait("existing", "other", "B");
        await harness.Wait("test-run", "isolated", "A", testRun: true);
        var result = await harness.Router.RouteSourceAsync(await harness.Source(correlation: "A"));
        Assert.Single(result.Starts); Assert.Equal("existing", Assert.Single(result.Resumes).WorkflowExecutionId);
        var payload = harness.Actors.Accepted.Single(command => command.Command.Kind == WorkflowExecutionCommandKind.ResumeBookmark).Command.Payload!.Value.Deserialize<RuntimeResumeBookmarkCommandPayload>()!;
        Assert.Equal("match", payload.BookmarkId); Assert.NotNull(await harness.Bookmarks.FindAsync("test-run", "isolated"));
    }

    [Theory]
    [InlineData("tenant")] [InlineData(null)] [InlineData("foreign")]
    public async Task Source_router_native_publication_access_uses_authoritative_tenant_before_acceptance(string? tenant)
    {
        await using var harness = new Harness(); await harness.SeedMaterial(); await harness.Bind("binding");
        var reference = Reference(tenant); await harness.References.SaveAsync(reference); var source = await harness.Source();
        if (tenant == "foreign")
        {
            var failure = await Assert.ThrowsAsync<StimulusDeliveryBoundaryException>(() => harness.Router.RouteSourceAsync(source).AsTask());
            Assert.False(failure.IsUnavailable); Assert.Empty(harness.Actors.Accepted);
            Assert.Null((await harness.Store.FindAsync(source.SourceOutboxItemId))!.RecipientSnapshot);
        }
        else
        {
            await harness.Router.RouteSourceAsync(source);
            var payload = Assert.Single(harness.Actors.Accepted).Command.Payload!.Value.Deserialize<WorkflowExecutionStartCommandPayload>()!;
            Assert.Equal(WorkflowExecutableSourceProvenance.From(reference), payload.PinnedSource);
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Source_router_native_publication_tenant_change_after_validation_is_fenced_at_acceptance(bool foreign)
    {
        await using var harness = new Harness(); await harness.SeedMaterial(); await harness.Bind("binding");
        await harness.References.SaveAsync(Reference("tenant")); var source = await harness.Source();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hold = harness.Ownership.ExecuteOwnershipAtomicAsync(async _ => { entered.SetResult(); await release.Task; return true; }, default).AsTask();
        await entered.Task; var route = harness.Router.RouteSourceAsync(source).AsTask();
        try
        {
            Assert.False(route.IsCompleted); Assert.Equal(0, harness.Store.State.WriteGate.CurrentCount);
            await harness.References.SaveAsync(Reference(foreign ? "foreign" : "tenant"));
        }
        finally { release.TrySetResult(); }
        await hold;
        if (foreign)
        {
            var failure = await Assert.ThrowsAsync<StimulusDeliveryBoundaryException>(() => route);
            Assert.False(failure.IsUnavailable); Assert.Empty(harness.Actors.Accepted);
            Assert.Null((await harness.Store.FindAsync(source.SourceOutboxItemId))!.RecipientSnapshot);
        }
        else { await route; Assert.Single(harness.Actors.Accepted); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Source_router_contextual_provider_outage_keeps_selection_uninitialized_then_repairs(bool cancellation)
    {
        await using var harness = new Harness(); await harness.SeedMaterial(); await harness.Wait("existing", "wait");
        var source = await harness.Source(); Exception inner = cancellation ? new OperationCanceledException("Constructed unrequested provider cancellation.") : new IOException("Constructed provider outage.");
        var cause = new InvalidOperationException("Constructed contextual provider boundary.", inner); harness.Lookup.NextFailure = cause;
        var failure = await Assert.ThrowsAsync<StimulusDeliveryBoundaryException>(() => harness.Router.RouteSourceAsync(source).AsTask());
        Assert.True(failure.IsUnavailable); Assert.Same(cause, failure.InnerException); Assert.Empty(harness.Actors.Accepted);
        Assert.Null((await harness.Store.FindAsync(source.SourceOutboxItemId))!.RecipientSnapshot);
        await harness.Router.RouteSourceAsync(source); Assert.Single(harness.Actors.Accepted);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Source_router_legacy_dispatcher_composition_refuses_before_any_selection_or_effect(bool legacyStart)
    {
        await using var harness = new Harness(legacyStart: legacyStart, legacyResume: !legacyStart); await harness.SeedMaterial();
        await harness.Bind("binding"); await harness.Wait("existing", "wait"); var source = await harness.Source();
        var failure = await Assert.ThrowsAsync<StimulusDeliveryBoundaryException>(() => harness.Router.RouteSourceAsync(source).AsTask());
        Assert.False(failure.IsUnavailable); Assert.IsType<NotSupportedException>(failure.InnerException);
        Assert.Equal(0, harness.Lookup.Reads); Assert.Empty(harness.Actors.Attempts); Assert.Null((await harness.Store.FindAsync(source.SourceOutboxItemId))!.RecipientSnapshot);
    }

    [Fact]
    public async Task Source_router_duplicate_intent_owners_refuse_during_composition()
    {
        await using var harness = new Harness(duplicateAdapter: true); await harness.SeedMaterial(); var source = await harness.Source();
        Assert.Throws<InvalidOperationException>(() => harness.Router);
        Assert.Empty(harness.Actors.Attempts); Assert.Null((await harness.Store.FindAsync(source.SourceOutboxItemId))!.RecipientSnapshot);
    }

    [Fact]
    public async Task Saved_resume_rechecks_source_takeover_after_actual_actor_lookup_before_enqueue()
    {
        await using var harness = new Harness(); await harness.SeedMaterial(); await harness.Wait("existing", "wait"); var source = await harness.Source();
        RuntimePostCommitOutboxClaim? peer = null;
        harness.Actors.BeforeAgent = async _ =>
        {
            await harness.Store.RecordDeliveryResultAsync(source.Claim, new(source.SourceOutboxItemId, RuntimePostCommitOutboxStatus.FailedRetryable, Now, "Constructed peer retry."));
            harness.Clock.Advance(TimeSpan.FromSeconds(2));
            peer = Assert.Single(await harness.Store.ClaimAsync(new("peer", harness.Clock.GetUtcNow(), TimeSpan.FromMinutes(1), 1)));
        };
        await Assert.ThrowsAsync<RuntimePostCommitOutboxStaleClaimException>(() => harness.Router.RouteSourceAsync(source).AsTask());
        Assert.Empty(harness.Actors.Attempts); Assert.NotNull((await harness.Store.FindAsync(source.SourceOutboxItemId))!.RecipientSnapshot);
        harness.Lookup.Fail = true;
        await harness.Router.RouteSourceAsync(new(peer!, Scope)); Assert.Single(harness.Actors.Accepted);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Source_owned_dispatch_uses_physical_intent_input_even_for_an_equivalent_caller_context(bool resume)
    {
        await using var harness = new Harness(); await harness.SeedMaterial();
        if (resume) await harness.Wait("existing", "wait"); else await harness.Bind("binding");
        var source = await harness.Source();
        var altered = new RuntimePostCommitIntent(source.Intent.IntentId, source.Intent.WorkflowExecutionId, source.Intent.Kind,
            source.Intent.RecordedAt, source.Intent.ActivityExecutionId, source.Intent.IdempotencyKey,
            JsonSerializer.Deserialize<JsonElement>("{\"correlation\":null,\"local\":false,\"value\":\"immutable-value\"}"));
        Assert.Equal(source.IntentFingerprint, RuntimeCheckpointCommitFingerprint.ComputeIntent(altered));
        var item = source.Claim.Item;
        var supplied = new PostCommitDeliveryContext(new(new RuntimePostCommitOutboxItem(item.OutboxItemId, altered, item.Status, item.RecordedAt,
            item.AvailableAt, item.RetryPolicy, item.DeliveryAttemptCount, item.DeliveringOwnerId, item.DeliveryStartedAt, item.DeliveredAt,
            item.LastFailureMessage, item.Metadata, item.DeliveryFencingToken, item.DeliveryVisibleAfter, item.StimulusDeliveryProtocolVersion,
            item.StimulusDeliverySourceScope, item.RecipientSnapshot), source.Claim.OwnerId, source.Claim.FencingToken, source.Claim.ClaimedAt, source.Claim.VisibleAfter), Scope);
        if (resume)
        {
            var observed = (await harness.Bookmarks.ObserveAsync("existing", "wait"))!;
            var captured = StimulusResumeRecipient.From(observed, (await harness.Executions.FindAsync("existing"))!, Scope);
            await harness.Store.InstallAsync(source, new(1, source.SourceOutboxItemId, source.IntentFingerprint,
                StimulusDeliveryScope.From((await harness.Executions.FindAsync("publisher"))!, Scope), Now, "event", "named", [], [captured]));
            await harness.Resume.DispatchAsync(supplied, "existing", "wait");
        }
        else await harness.Router.RouteSourceAsync(supplied);
        var command = Assert.Single(harness.Actors.Accepted).Command;
        var input = resume ? command.Payload!.Value.Deserialize<RuntimeResumeBookmarkCommandPayload>()!.Input :
            command.Payload!.Value.Deserialize<WorkflowExecutionStartCommandPayload>()!.StimulusInput;
        Assert.Equal(source.Intent.Payload!.Value.GetRawText(), input!.Value.GetRawText());
        Assert.NotEqual(altered.Payload!.Value.GetRawText(), input.Value.GetRawText());
    }

    [Theory]
    [InlineData("valid", false)] [InlineData("valid", true)]
    [InlineData("physical", false)] [InlineData("physical", true)]
    [InlineData("tenant", false)] [InlineData("tenant", true)]
    [InlineData("test-run", false)] [InlineData("test-run", true)]
    [InlineData("test-scope", false)] [InlineData("test-scope", true)]
    [InlineData("missing-correlation", false)] [InlineData("missing-correlation", true)]
    [InlineData("wrong-correlation", false)] [InlineData("wrong-correlation", true)]
    public async Task Already_installed_cohort_is_validated_in_full_before_any_dispatch(string defect, bool directResume)
    {
        await using var harness = new Harness(); await harness.SeedMaterial(); await harness.Bind("binding");
        await harness.Wait("existing", "wait", "A"); var source = await harness.Source(correlation: "A");
        var observed = (await harness.Bookmarks.ObserveAsync("existing", "wait"))!;
        var saved = StimulusResumeRecipient.From(observed, (await harness.Executions.FindAsync("existing"))!, Scope);
        var capture = saved.RegistrationCapture!; var scope = capture.Scope;
        var changedScope = new StimulusDeliveryScope(defect == "physical" ? new("foreign") : scope.PersistenceScope,
            defect == "tenant" ? "foreign" : scope.TenantId, scope.Partition,
            defect == "test-run" ? WorkflowRunKind.TestRun : scope.RunKind,
            defect == "test-scope" ? new WorkflowTestScope("isolated", Now.AddHours(1), scope.TenantId, scope.Partition!) : null);
        IReadOnlyDictionary<string, string> metadata = defect == "missing-correlation" ? new Dictionary<string, string>() :
            defect == "wrong-correlation" ? new Dictionary<string, string> { [RuntimeMetadataKeys.CorrelationId] = "B" } : capture.Metadata;
        var recipient = new StimulusResumeRecipient(saved.WorkflowExecutionId, saved.BookmarkId, saved.ActivityExecutionId,
            saved.ExecutableNodeId, saved.ResumeTargetId, saved.StimulusType, saved.StimulusHash, saved.CreatedAt, saved.ExpiresAt,
            new(capture.Generation, capture.Executable, changedScope, capture.PinnedSource, metadata, capture.PayloadFingerprint));
        var binding = Assert.Single(await harness.Bindings.ListAllByStimulusAsync("event", "named"));
        var winner = await harness.Store.InstallAsync(source, new(1, source.SourceOutboxItemId, source.IntentFingerprint,
            StimulusDeliveryScope.From((await harness.Executions.FindAsync("publisher"))!, Scope), Now, "event", "named",
            [new StimulusStartRecipient(binding, Pin, null)], [recipient]));
        Assert.Equal(1, winner.RetentionAcceptanceVersion); Assert.Single(winner.Starts); Assert.Single(winner.Resumes);
        harness.Lookup.Fail = true;
        async Task Dispatch()
        {
            if (directResume) await harness.Resume.DispatchAsync(source, "existing", "wait");
            else await harness.Router.RouteSourceAsync(source);
        }
        if (defect == "valid")
        {
            await Dispatch(); Assert.Equal(directResume ? 1 : 2, harness.Actors.Accepted.Count);
        }
        else
        {
            var failure = await Assert.ThrowsAsync<StimulusDeliveryBoundaryException>(Dispatch);
            Assert.False(failure.IsUnavailable); Assert.IsType<InvalidDataException>(failure.InnerException);
            Assert.Empty(harness.Actors.Attempts);
        }
        Assert.Equal(0, harness.Lookup.Reads);
    }

    private static WorkflowExecutableSourceReference Reference(string? tenant) => new("publication", Pin.ArtifactId, "definition", "source",
        "2", Pin.DefinitionId, "published-version", "published-label", Now, Now, WorkflowExecutableReferenceScope.Published, TenantId: tenant);

    private sealed class Access : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(Scope); }
    private sealed class Adapter : IStimulusDeliveryIntentAdapter
    {
        public string IntentKind => "test.saved-event";
        public StimulusDispatchRequest CreateRequest(RuntimePostCommitIntent intent)
        {
            var payload = intent.Payload!.Value; var local = payload.GetProperty("local").GetBoolean();
            return new("event", "named", intent.Payload, payload.GetProperty("correlation").GetString(),
                local ? StimulusRoutingMode.ResumeOnly : StimulusRoutingMode.StartAndResume, intent.IdempotencyKey, "test.saved-event",
                targetWorkflowExecutionId: local ? intent.WorkflowExecutionId : null) { ResumeAllMatchingBookmarks = true };
        }
    }
    private sealed class CountingLookup(IGlobalBookmarkStimulusLookup inner) : IGlobalBookmarkStimulusLookup
    {
        public int Reads; public bool Fail; public Exception? NextFailure;
        public ValueTask<GlobalBookmarkStimulusLookupResult> FindWaitingAsync(GlobalBookmarkStimulusLookupRequest request, CancellationToken cancellationToken = default)
        { Reads++; if (NextFailure is { } failure) { NextFailure = null; throw failure; } if (Fail) throw new IOException("A retry must not perform live recipient selection."); return inner.FindWaitingAsync(request, cancellationToken); }
        public ValueTask<GlobalBookmarkStimulusLookupResult> FindWaitingByTypeAsync(GlobalBookmarkStimulusTypeLookupRequest request, CancellationToken cancellationToken = default) => inner.FindWaitingByTypeAsync(request, cancellationToken);
        public ValueTask<IReadOnlyCollection<string>> FindWaitingStimulusHashesByTypeAsync(GlobalBookmarkStimulusTypeLookupRequest request, CancellationToken cancellationToken = default) => inner.FindWaitingStimulusHashesByTypeAsync(request, cancellationToken);
    }
    private sealed class LegacyStart : IWorkflowStartDispatcher
    {
        public ValueTask<WorkflowExecutionStartDispatchResult> DispatchAsync(WorkflowExecutionStartDispatchRequest request,
            WorkflowExecutableReferenceScope requiredScope = WorkflowExecutableReferenceScope.Published, WorkflowExecutionCommandDispatchOptions? dispatchOptions = null,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("A legacy start dispatcher must never receive a saved source.");
    }
    private sealed class LegacyResume : IBookmarkResumeDispatcher
    {
        public ValueTask<BookmarkResumeDispatchResult> DispatchAsync(BookmarkResumeDispatchRequest request,
            WorkflowExecutionCommandDispatchOptions? dispatchOptions = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("A legacy resume dispatcher must never receive a saved source.");
    }
    private sealed class RecordingActors : IWorkflowExecutionActorProvider
    {
        private readonly HashSet<string> seen = new(StringComparer.Ordinal);
        public List<WorkflowExecutionCommandEnvelope> Attempts { get; } = [];
        public List<WorkflowExecutionCommandEnvelope> Accepted { get; } = [];
        public Func<WorkflowExecutionCommandEnvelope, Task>? BeforeEnqueue; public int ShedStarts; public bool LoseNextAck;
        public Func<WorkflowExecutionActorActivationRequest, Task>? BeforeAgent;
        public WorkflowExecutionCommandDispatchStatus? RefuseNextStart;
        public WorkflowExecutionActorCapabilities Capabilities => WorkflowExecutionActorCapabilities.InProcessMailbox;
        public async ValueTask<IWorkflowExecutionActor> GetAgentAsync(WorkflowExecutionActorActivationRequest request, CancellationToken cancellationToken = default)
        { if (BeforeAgent is { } before) { BeforeAgent = null; await before(request); } return new Actor(this, request.WorkflowExecutionId); }
        public ValueTask PassivateAsync(WorkflowExecutionActorPassivationRequest request, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        private sealed class Actor(RecordingActors owner, string executionId) : IWorkflowExecutionActor
        {
            public WorkflowExecutionActorDescriptor Descriptor { get; } = new(executionId, "recording:" + executionId, "recording-component-boundary", WorkflowExecutionActorStatus.Active, WorkflowExecutionActorCapabilities.InProcessMailbox, Now);
            public async ValueTask<WorkflowExecutionCommandDispatchResult> EnqueueAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default)
            {
                owner.Attempts.Add(envelope); if (owner.BeforeEnqueue is { } before) await before(envelope);
                if (envelope.Command.Kind == WorkflowExecutionCommandKind.Start && owner.RefuseNextStart is { } refusal)
                { owner.RefuseNextStart = null; return new(envelope.EnvelopeId, executionId, refusal, Now, "Constructed non-shed admission refusal."); }
                if (envelope.Command.Kind == WorkflowExecutionCommandKind.Start && owner.ShedStarts > 0)
                { owner.ShedStarts--; return new(envelope.EnvelopeId, executionId, WorkflowExecutionCommandDispatchStatus.Deferred, Now, "Constructed capacity refusal.", new Dictionary<string, string> { [RuntimeMetadataKeys.DispatchShed] = "true" }); }
                if (!owner.seen.Add(envelope.IdempotencyKey)) return new(envelope.EnvelopeId, executionId, WorkflowExecutionCommandDispatchStatus.Duplicate, Now);
                owner.Accepted.Add(envelope);
                if (owner.LoseNextAck) { owner.LoseNextAck = false; throw new IOException("Constructed acknowledgment loss after recorded mailbox acceptance."); }
                return new(envelope.EnvelopeId, executionId, WorkflowExecutionCommandDispatchStatus.Accepted, Now);
            }
        }
    }
    private sealed class Harness : IAsyncDisposable
    {
        private readonly ServiceProvider provider; private readonly IServiceScope scope;
        private IServiceProvider Services => scope.ServiceProvider;
        public FakeTimeProvider Clock { get; } = new(Now);
        public RecordingActors Actors { get; } = new();
        public IStimulusRouter Router => Services.GetRequiredService<IStimulusRouter>();
        public IBookmarkResumeDispatcher Resume => Services.GetRequiredService<IBookmarkResumeDispatcher>();
        public IWorkflowExecutionStateStore Executions => Services.GetRequiredService<IWorkflowExecutionStateStore>();
        public InMemoryRuntimeCheckpointCommitStore Store => Services.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>();
        public IBookmarkStateStore Bookmarks => Services.GetRequiredService<IBookmarkStateStore>();
        public IWorkflowTriggerBindingStore Bindings => Services.GetRequiredService<IWorkflowTriggerBindingStore>();
        public IWorkflowExecutableSourceReferenceStore References => Services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>();
        public InMemoryExecutionLivenessStateStore Ownership => (InMemoryExecutionLivenessStateStore)Services.GetRequiredService<IExecutionLivenessStateStore>();
        public CountingLookup Lookup => Services.GetRequiredService<CountingLookup>();
        public Harness(bool coalescing = false, bool adapter = true, bool legacyStart = false, bool legacyResume = false, bool duplicateAdapter = false)
        {
            var services = new ServiceCollection(); services.AddSingleton<TimeProvider>(Clock); services.AddSingleton<IPersistenceAccessContextAccessor, Access>();
            services.AddSingleton<IWorkflowExecutionActorProvider>(Actors); if (adapter) services.AddSingleton<IStimulusDeliveryIntentAdapter, Adapter>();
            if (duplicateAdapter) services.AddSingleton<IStimulusDeliveryIntentAdapter>(new Adapter());
            if (legacyStart) services.AddSingleton<IWorkflowStartDispatcher, LegacyStart>();
            if (legacyResume) services.AddSingleton<IBookmarkResumeDispatcher, LegacyResume>();
            services.AddSingleton(new PersistenceModeOptions()); services.AddWorkflowRuntime(); if (coalescing) services.AddPersistenceModes();
            services.AddSingleton<IWorkflowTriggerBindingStore, InMemoryWorkflowTriggerBindingStore>();
            services.AddScoped<CountingLookup>(sp => new(new GlobalBookmarkStimulusLookup((IBookmarkStimulusIndex)sp.GetRequiredService<IBookmarkStateStore>(), sp.GetRequiredService<IWorkflowExecutionStateStore>())));
            services.AddScoped<IGlobalBookmarkStimulusLookup>(sp => sp.GetRequiredService<CountingLookup>()); services.AddWorkflowStimulusRouter();
            provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }); scope = provider.CreateScope();
        }
        public async Task SeedMaterial()
        {
            _ = Store;
            await Services.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(new(Pin,
                new ExecutableNode("node", "authored", "test/activity", "1.0.0", new RuntimeActivityDescriptor("test", RuntimeActivityDescriptor.InitialSchemaVersion, JsonSerializer.SerializeToElement(new { })), new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>()),
                new Dictionary<string, WorkflowExecutableResumeTarget> { ["resume"] = new("resume", "node", "test", new Dictionary<string, string>()) }, Now, new Dictionary<string, string>(), IncidentStrategyBuiltIns.FaultReference));
            await Services.GetRequiredService<IWorkflowExecutionStateStore>().SaveAsync(Execution("publisher"));
        }
        public async Task<PostCommitDeliveryContext> Source(bool local = false, string? correlation = null)
        {
            var intent = new RuntimePostCommitIntent("send", "publisher", "test.saved-event", Now, "publisher-activity", "original-key", JsonSerializer.SerializeToElement(new { value = "immutable-value", local, correlation }));
            await Store.AddPendingForTestingAsync(new("source-commit:send", intent, RuntimePostCommitOutboxStatus.Pending, Now, Now, new(4, TimeSpan.FromSeconds(1)), 0, null, null, null, null, null, 0, null, 1, Scope, null));
            return new(Assert.Single(await Store.ClaimAsync(new("owner", Clock.GetUtcNow(), TimeSpan.FromMinutes(1), 1))), Scope);
        }
        public async Task Bind(string id, string artifact = "artifact", string? correlation = null) => await Bindings.SaveAsync(new(id, artifact, Pin.DefinitionId,
            Pin.ArtifactVersion, Pin.ArtifactHash, "node", "event", "named", correlation, new Dictionary<string, string> { ["binding"] = id }, Now));
        public async Task Wait(string executionId, string id, string? correlation = null, bool testRun = false)
        {
            await Services.GetRequiredService<IWorkflowExecutionStateStore>().SaveAsync(Execution(executionId) with { RunKind = testRun ? WorkflowRunKind.TestRun : WorkflowRunKind.PublishedRun });
            await Services.GetRequiredService<IActivityExecutionStateStore>().SaveAsync(new(new(id + ":activity", executionId, "node", "authored", "test/activity", "1.0.0"),
                ActivityExecutionStatus.Suspended, null, 1, Now, Now, null, null, null, null, null,
                ActivitySchedulingProvenance.From(executionId, null, null, null, null, null, null, "fixture"), null, [id], [], 0, 0, new Dictionary<string, string>()));
            await Bookmarks.SaveAsync(new(id, executionId, id + ":activity", "node", "resume", "event", "named", null,
                correlation is null ? new Dictionary<string, string>() : new Dictionary<string, string> { [RuntimeMetadataKeys.CorrelationId] = correlation }, Clock.GetUtcNow(), null));
        }
        private static WorkflowExecutionState Execution(string id) => new(id, Pin, WorkflowExecutionStatus.Suspended, null, Now, Now, Now,
            null, null, null, "tenant", new Dictionary<string, string>()) { RunKind = WorkflowRunKind.PublishedRun, Partition = new("partition") };
        public async ValueTask DisposeAsync() { scope.Dispose(); await provider.DisposeAsync(); }
    }
}
