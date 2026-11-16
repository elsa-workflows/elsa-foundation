using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Elsa.Workflows.Runtime.Services.WorkHandlers;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>Actual provider cohort admission through the ordinary dispatcher; recording actors are not host recovery proof.</summary>
public sealed partial class StimulusDeliveryStoreTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Saved_start_uses_verified_retired_publication_and_exact_original_key_metadata_and_options(bool ef)
    {
        await using var harness = await Harness.Create(ef); var source = new PostCommitDeliveryContext(await harness.Seed(), Scope);
        await harness.Delivery.InstallAsync(source, AdmissionSnapshot(source));
        Assert.True(await harness.References.RetireAsync("selected-reference", Now));
        var actors = new StartActors(); var policy = new StartPolicy(); var options = new WorkflowExecutionCommandDispatchOptions();
        var result = await SavedDispatcher(harness, actors, policy).DispatchAsync(SavedRequest(source), dispatchOptions: options);
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, result.CommandDispatch.Status);
        Assert.Equal(SelectedSource(), result.PinnedSource); Assert.Equal(Pin, result.PinnedExecutable);
        var command = Assert.Single(actors.Envelopes); var payload = command.Command.Payload!.Value.Deserialize<WorkflowExecutionStartCommandPayload>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var key = KeyedWorkflowStartIdentity.For("key", Pin.ArtifactId);
        Assert.Equal(key.CommandId, command.Command.CommandId); Assert.Equal(key.EnvelopeId, command.EnvelopeId);
        Assert.Equal(key.StartKey, command.IdempotencyKey); Assert.Equal(key.WorkflowExecutionId, command.WorkflowExecutionId);
        Assert.Equal("start", payload.TriggerNodeId); Assert.Equal(AdmissionMetadata, payload.TriggerMetadata);
        Assert.Equal(SelectedSource(), payload.PinnedSource); Assert.Equal("selected-label", command.Metadata["runtime.artifactVersion"]);
        Assert.True(JsonElement.DeepEquals(source.Intent.Payload!.Value, payload.StimulusInput!.Value));
        Assert.Equal(WorkflowExecutableStartAuthorityKind.StimulusDelivery, payload.StartAuthority!.Kind);
        Assert.Equal("tenant", payload.TenantId); Assert.Equal(Publisher.Partition, payload.Partition);
        Assert.Same(options, Assert.Single(actors.Options));
        var evaluated = Assert.Single(policy.Contexts); Assert.Equal(WorkflowExecutableStartAuthorityKind.StimulusDelivery, evaluated.AuthorityKind);
        Assert.Equal("tenant", evaluated.TenantId); Assert.Equal(Publisher.Partition, evaluated.Partition);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Saved_duplicate_preserves_actual_first_binding_provenance_without_live_material_or_policy_reselection(bool ef)
    {
        await using var harness = await Harness.Create(ef); var source = new PostCommitDeliveryContext(await harness.Seed(), Scope);
        await harness.Delivery.InstallAsync(source, AdmissionSnapshot(source));
        var actualSource = SelectedSource() with { SourceReferenceId = "actual-earlier-binding", DefinitionVersionId = "actual-version" };
        var key = KeyedWorkflowStartIdentity.For("key", Pin.ArtifactId);
        await harness.Executions.SaveAsync(Publisher with { WorkflowExecutionId = key.WorkflowExecutionId, PinnedSource = actualSource,
            Authority = WorkflowExecutionAuthoritySnapshot.CreateRoot("runtime.publish-test") });
        Assert.True(await harness.References.RetireAsync("selected-reference", Now));
        Assert.True(await harness.Artifacts.DeleteAsync(Pin.ArtifactId)); // A duplicate never reselects material.
        var actors = new StartActors(); var policy = new StartPolicy { Decision = WorkflowExecutableStartDecision.Deny("hold", "No new starts.") };
        var result = await SavedDispatcher(harness, actors, policy).DispatchAsync(SavedRequest(source, binding: "binding-b"));
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Duplicate, result.CommandDispatch.Status);
        Assert.Equal(actualSource, result.PinnedSource); Assert.Empty(actors.Envelopes); Assert.Empty(actors.Activations); Assert.Empty(policy.Contexts);
    }

    [Theory]
    [InlineData(false, false, false)] [InlineData(false, false, true)]
    [InlineData(false, true, false)] [InlineData(false, true, true)]
    [InlineData(true, false, false)] [InlineData(true, false, true)]
    [InlineData(true, true, false)] [InlineData(true, true, true)]
    public async Task Saved_start_rejects_caller_stimulus_input_before_duplicate_or_effects(bool ef, bool existing, bool missing)
    {
        await using var harness = await Harness.Create(ef);
        var source = new PostCommitDeliveryContext(await harness.Seed(), Scope);
        await harness.Delivery.InstallAsync(source, AdmissionSnapshot(source));
        if (existing)
            await harness.Executions.SaveAsync(Publisher with
            {
                WorkflowExecutionId = KeyedWorkflowStartIdentity.For("key", Pin.ArtifactId).WorkflowExecutionId,
                Authority = WorkflowExecutionAuthoritySnapshot.CreateRoot("runtime.publish-test")
            });
        var actors = new StartActors();
        var policy = new StartPolicy();

        var refusal = await Assert.ThrowsAsync<WorkflowExecutableStartRejectedException>(() =>
            SavedDispatcher(harness, actors, policy).DispatchAsync(SavedRequest(source,
                missing ? "stimulus-input-missing" : "stimulus-input-changed")).AsTask());

        Assert.Equal(WorkflowExecutableStartRejectionCategory.Authority, refusal.Category);
        Assert.Equal("stimulus-delivery-input-mismatch", refusal.ReasonCode);
        Assert.Empty(actors.Activations);
        Assert.Empty(actors.Envelopes);
        Assert.Empty(policy.Contexts);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Saved_start_rejects_caller_identity_replacing_the_source_identity(bool ef)
    {
        await using var harness = await Harness.Create(ef);
        var source = new PostCommitDeliveryContext(await harness.Seed(), Scope);
        await harness.Delivery.InstallAsync(source, AdmissionSnapshot(source));
        var actors = new StartActors();
        var policy = new StartPolicy();

        var refusal = await Assert.ThrowsAsync<WorkflowExecutableStartRejectedException>(() =>
            SavedDispatcher(harness, actors, policy).DispatchAsync(SavedRequest(source, "requested-by")).AsTask());

        Assert.Equal("stimulus-delivery-input-mismatch", refusal.ReasonCode);
        Assert.Empty(actors.Activations);
        Assert.Empty(actors.Envelopes);
        Assert.Empty(policy.Contexts);
    }

    public static TheoryData<bool, string> SavedRequestDefects()
    {
        var data = new TheoryData<bool, string>();
        foreach (var ef in new[] { false, true })
        foreach (var defect in new[] { "key", "execution", "artifact", "node", "metadata", "tenant", "partition", "run", "parent", "depth", "correlation", "authority", "variables", "inputs" })
            data.Add(ef, defect);
        return data;
    }

    [Theory]
    [MemberData(nameof(SavedRequestDefects))]
    public async Task Saved_start_cannot_override_exact_entry_key_scope_or_ordinary_root_context(bool ef, string defect)
    {
        await using var harness = await Harness.Create(ef); var source = new PostCommitDeliveryContext(await harness.Seed(), Scope);
        await harness.Delivery.InstallAsync(source, AdmissionSnapshot(source));
        // Existing state must not turn forged source/entry/context into a successful early duplicate.
        await harness.Executions.SaveAsync(Publisher with { WorkflowExecutionId = KeyedWorkflowStartIdentity.For("key", Pin.ArtifactId).WorkflowExecutionId,
            Authority = WorkflowExecutionAuthoritySnapshot.CreateRoot("runtime.publish-test") });
        var actors = new StartActors(); var policy = new StartPolicy();
        var refusal = await Assert.ThrowsAsync<WorkflowExecutableStartRejectedException>(() => SavedDispatcher(harness, actors, policy).DispatchAsync(SavedRequest(source, defect)).AsTask());
        Assert.Equal(WorkflowExecutableStartRejectionCategory.Authority, refusal.Category);
        Assert.Empty(actors.Activations); Assert.Empty(actors.Envelopes); Assert.Empty(policy.Contexts);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Saved_start_runs_ordinary_policy_and_denial_precedes_actor_lookup(bool ef)
    {
        await using var harness = await Harness.Create(ef); var source = new PostCommitDeliveryContext(await harness.Seed(), Scope);
        await harness.Delivery.InstallAsync(source, AdmissionSnapshot(source));
        var actors = new StartActors(); var policy = new StartPolicy { Decision = WorkflowExecutableStartDecision.Deny("domain-denied", "Not allowed.") };
        var refused = await Assert.ThrowsAsync<WorkflowExecutableStartRejectedException>(() => SavedDispatcher(harness, actors, policy).DispatchAsync(SavedRequest(source)).AsTask());
        Assert.Equal(WorkflowExecutableStartRejectionCategory.Policy, refused.Category); Assert.Equal("domain-denied", refused.ReasonCode);
        Assert.Single(policy.Contexts); Assert.Empty(actors.Activations); Assert.Empty(actors.Envelopes);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task Saved_start_rechecks_source_after_policy_or_actor_lookup_before_recipient_enqueue(bool ef, bool duringActorLookup)
    {
        await using var harness = await Harness.Create(ef); var source = new PostCommitDeliveryContext(await harness.Seed(), Scope);
        await harness.Delivery.InstallAsync(source, AdmissionSnapshot(source));
        async Task TakeOver()
        {
            var peer = (IRuntimePostCommitOutboxClaimStore)harness.Reopen();
            await peer.RecordDeliveryResultAsync(source.Claim, new(source.SourceOutboxItemId, RuntimePostCommitOutboxStatus.FailedRetryable, Now, "retry"));
            harness.Clock.Advance(TimeSpan.FromSeconds(2));
            Assert.Single(await peer.ClaimAsync(new("peer", Now.AddSeconds(2), TimeSpan.FromMinutes(1), 1)));
        }
        var actors = new StartActors { BeforeLookup = duringActorLookup ? TakeOver : null };
        var policy = new StartPolicy { BeforeDecision = duringActorLookup ? null : TakeOver };
        await Assert.ThrowsAsync<RuntimePostCommitOutboxStaleClaimException>(() => SavedDispatcher(harness, actors, policy).DispatchAsync(SavedRequest(source)).AsTask());
        Assert.Empty(actors.Envelopes); Assert.True(harness.Clock.GetUtcNow() < source.Claim.VisibleAfter);
        Assert.Equal(duringActorLookup ? 1 : 0, actors.Activations.Count);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Same_artifact_later_binding_keeps_the_original_key_after_a_shed_start(bool ef)
    {
        await using var harness = await Harness.Create(ef); var source = new PostCommitDeliveryContext(await harness.Seed(), Scope);
        await harness.Delivery.InstallAsync(source, AdmissionSnapshot(source));
        var actors = new StartActors { ShedFirst = true }; var dispatcher = SavedDispatcher(harness, actors, new StartPolicy());
        Assert.True((await dispatcher.DispatchAsync(SavedRequest(source))).CommandDispatch.IsShed);
        Assert.Null(await harness.Executions.FindAsync(KeyedWorkflowStartIdentity.For("key", Pin.ArtifactId).WorkflowExecutionId));
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, (await dispatcher.DispatchAsync(SavedRequest(source, binding: "binding-b"))).CommandDispatch.Status);
        Assert.Equal(2, actors.Envelopes.Count); Assert.Equal(actors.Envelopes[0].EnvelopeId, actors.Envelopes[1].EnvelopeId);
        Assert.Equal(actors.Envelopes[0].IdempotencyKey, actors.Envelopes[1].IdempotencyKey);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Saved_start_preserves_explicit_legacy_publisher_scope_without_fabricating_tenant_or_test_authority(bool ef)
    {
        var legacy = Publisher with { TenantId = null, Partition = null, RunKind = WorkflowRunKind.Unknown };
        await using var harness = await Harness.Create(ef); var source = new PostCommitDeliveryContext(await harness.Seed(publisher: legacy), Scope);
        await harness.Delivery.InstallAsync(source, AdmissionSnapshot(source, legacy));
        var actors = new StartActors();
        var result = await SavedDispatcher(harness, actors, new StartPolicy()).DispatchAsync(SavedRequest(source, publisher: legacy));
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, result.CommandDispatch.Status);
        var payload = Assert.Single(actors.Envelopes).Command.Payload!.Value.Deserialize<WorkflowExecutionStartCommandPayload>()!;
        Assert.Null(payload.TenantId); Assert.Null(payload.TestScope); Assert.Equal(WorkflowRunKind.PublishedRun, payload.RunKind);
        Assert.Equal(new WorkflowExecutionPartition(WorkflowExecutionPartition.DefaultValue), payload.Partition);
    }

    public static TheoryData<bool, string> ExistingSavedRootDefects()
    {
        var data = new TheoryData<bool, string>();
        foreach (var ef in new[] { false, true })
        foreach (var defect in new[] { "tenant", "partition", "run", "parent", "depth", "correlation", "authority", "missing-authority" })
            if (!ef || defect != "tenant") data.Add(ef, defect); // EF already prevents this foreign-tenant row at its writer.
        return data;
    }

    [Theory]
    [MemberData(nameof(ExistingSavedRootDefects))]
    public async Task Saved_duplicate_refuses_a_keyed_execution_with_foreign_root_or_scope_context(bool ef, string defect)
    {
        await using var harness = await Harness.Create(ef); var source = new PostCommitDeliveryContext(await harness.Seed(), Scope);
        await harness.Delivery.InstallAsync(source, AdmissionSnapshot(source));
        var actual = Publisher with
        {
            WorkflowExecutionId = KeyedWorkflowStartIdentity.For("key", Pin.ArtifactId).WorkflowExecutionId,
            TenantId = defect == "tenant" ? "foreign" : "tenant", Partition = defect == "partition" ? new("foreign") : Publisher.Partition,
            RunKind = defect == "run" ? WorkflowRunKind.Unknown : WorkflowRunKind.PublishedRun,
            ParentWorkflowExecutionId = defect == "parent" ? "foreign-parent" : null,
            DispatchNestingDepth = defect == "depth" ? 1 : 0, CorrelationId = defect == "correlation" ? "foreign-correlation" : null,
            Authority = defect == "missing-authority" ? null : new("runtime.publish-test", defect == "authority" ? "foreign-root" : "runtime.publish-test")
        };
        await harness.Executions.SaveAsync(actual);
        var actors = new StartActors();
        var refusal = await Assert.ThrowsAsync<WorkflowExecutableStartRejectedException>(() => SavedDispatcher(harness, actors, new StartPolicy()).DispatchAsync(SavedRequest(source)).AsTask());
        Assert.Equal("stimulus-delivery-duplicate-scope-mismatch", refusal.ReasonCode);
        Assert.Empty(actors.Activations); Assert.Empty(actors.Envelopes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task An_unmarked_data_only_cohort_cannot_admit_a_saved_start_even_for_an_existing_key(bool ef)
    {
        await using var harness = await Harness.Create(ef); await harness.EnsureMaterial(); await harness.SavePublisher(Publisher);
        var pending = Pending("unverified-start", true);
        var proposed = new PostCommitDeliveryContext(RuntimePostCommitOutboxClaimTransitions.Claim(pending, new("owner", Now, TimeSpan.FromMinutes(1), 1)), Scope);
        var older = AdmissionSnapshot(proposed);
        await harness.SavePending(new(pending.OutboxItemId, pending.Intent, pending.Status, pending.RecordedAt, pending.AvailableAt,
            pending.RetryPolicy, 0, null, null, null, null, null, 0, null, 1, Scope, older));
        var source = new PostCommitDeliveryContext(Assert.Single(await harness.Claims.ClaimAsync(new("owner", Now, TimeSpan.FromMinutes(1), 1))), Scope);
        await harness.Executions.SaveAsync(Publisher with { WorkflowExecutionId = KeyedWorkflowStartIdentity.For("key", Pin.ArtifactId).WorkflowExecutionId,
            Authority = WorkflowExecutionAuthoritySnapshot.CreateRoot("runtime.publish-test") });
        var actors = new StartActors();
        var refusal = await Assert.ThrowsAsync<WorkflowExecutableStartRejectedException>(() => SavedDispatcher(harness, actors, new StartPolicy()).DispatchAsync(SavedRequest(source)).AsTask());
        Assert.Equal("stimulus-delivery-cohort-unverified", refusal.ReasonCode); Assert.Empty(actors.Activations); Assert.Empty(actors.Envelopes);
    }

    [Theory]
    [InlineData(false, "definition")] [InlineData(false, "version")] [InlineData(false, "label")] [InlineData(false, "hash")]
    [InlineData(true, "definition")] [InlineData(true, "version")] [InlineData(true, "label")] [InlineData(true, "hash")]
    public async Task Saved_start_checks_actual_complete_material_pin_before_new_admission(bool ef, string defect)
    {
        await using var harness = await Harness.Create(ef); var source = new PostCommitDeliveryContext(await harness.Seed(), Scope);
        await harness.Delivery.InstallAsync(source, AdmissionSnapshot(source));
        Assert.True(await harness.Artifacts.DeleteAsync(Pin.ArtifactId));
        var changed = defect switch { "definition" => Pin with { DefinitionId = "other" }, "version" => Pin with { DefinitionVersionId = "other" },
            "label" => Pin with { ArtifactVersion = "other" }, _ => Pin with { ArtifactHash = "other" } };
        await harness.Artifacts.SaveAsync(Material(changed));
        var actors = new StartActors();
        var refusal = await Assert.ThrowsAsync<WorkflowExecutableStartRejectedException>(() => SavedDispatcher(harness, actors, new StartPolicy()).DispatchAsync(SavedRequest(source)).AsTask());
        Assert.Equal("stimulus-delivery-pin-mismatch", refusal.ReasonCode); Assert.Empty(actors.Activations); Assert.Empty(actors.Envelopes);
    }

    [Theory]
    [InlineData(false, "definition")] [InlineData(false, "version")] [InlineData(false, "label")] [InlineData(false, "hash")]
    [InlineData(true, "definition")] [InlineData(true, "version")] [InlineData(true, "label")] [InlineData(true, "hash")]
    public async Task Saved_duplicate_checks_its_existing_complete_pin_without_replacing_actual_source_provenance(bool ef, string defect)
    {
        await using var harness = await Harness.Create(ef); var source = new PostCommitDeliveryContext(await harness.Seed(), Scope);
        await harness.Delivery.InstallAsync(source, AdmissionSnapshot(source));
        var changed = defect switch { "definition" => Pin with { DefinitionId = "other" }, "version" => Pin with { DefinitionVersionId = "other" },
            "label" => Pin with { ArtifactVersion = "other" }, _ => Pin with { ArtifactHash = "other" } };
        await harness.Executions.SaveAsync(Publisher with { WorkflowExecutionId = KeyedWorkflowStartIdentity.For("key", Pin.ArtifactId).WorkflowExecutionId,
            PinnedExecutable = changed, PinnedSource = SelectedSource(), Authority = WorkflowExecutionAuthoritySnapshot.CreateRoot("runtime.publish-test") });
        var actors = new StartActors(); var policy = new StartPolicy();
        var refused = await Assert.ThrowsAsync<WorkflowExecutableStartRejectedException>(() => SavedDispatcher(harness, actors, policy).DispatchAsync(SavedRequest(source)).AsTask());
        Assert.Equal(WorkflowExecutableStartRejectionCategory.Authority, refused.Category);
        Assert.Empty(actors.Activations); Assert.Empty(actors.Envelopes); Assert.Empty(policy.Contexts);
    }

    public static TheoryData<bool, string> SavedSourceDefects()
    {
        var data = new TheoryData<bool, string>();
        foreach (var ef in new[] { false, true })
        foreach (var defect in new[] { "source", "digest", "scope", "owner", "fence", "binding", "expired", "delivered" }) data.Add(ef, defect);
        return data;
    }

    [Theory]
    [MemberData(nameof(SavedSourceDefects))]
    public async Task Saved_source_authority_is_checked_physically_before_even_an_existing_duplicate(bool ef, string defect)
    {
        await using var harness = await Harness.Create(ef); var source = new PostCommitDeliveryContext(await harness.Seed(), Scope);
        await harness.Delivery.InstallAsync(source, AdmissionSnapshot(source));
        await harness.Executions.SaveAsync(Publisher with { WorkflowExecutionId = KeyedWorkflowStartIdentity.For("key", Pin.ArtifactId).WorkflowExecutionId,
            Authority = WorkflowExecutionAuthoritySnapshot.CreateRoot("runtime.publish-test") });
        var authority = new WorkflowExecutableStartAuthority(WorkflowExecutableStartAuthorityKind.StimulusDelivery, null, new(
            defect == "source" ? "other:send" : source.SourceOutboxItemId,
            defect == "digest" ? new string('0', 64) : source.IntentFingerprint, defect == "scope" ? new("foreign") : Scope,
            defect == "owner" ? "foreign" : source.Claim.OwnerId, defect == "fence" ? source.Claim.FencingToken + 1 : source.Claim.FencingToken,
            defect == "binding" ? "foreign-binding" : "binding-a"));
        if (defect == "expired") harness.Clock.Advance(TimeSpan.FromMinutes(2));
        if (defect == "delivered") await harness.Claims.RecordDeliveryResultAsync(source.Claim, new(source.SourceOutboxItemId, RuntimePostCommitOutboxStatus.Delivered, Now));
        var actors = new StartActors();
        var failure = await Record.ExceptionAsync(() => SavedDispatcher(harness, actors, new StartPolicy()).DispatchAsync(SavedRequest(source, overrideAuthority: authority)).AsTask());
        if (defect is "owner" or "fence" or "expired" or "delivered") Assert.IsType<RuntimePostCommitOutboxStaleClaimException>(failure);
        else Assert.IsType<StimulusDeliveryBoundaryException>(failure);
        Assert.Empty(actors.Activations); Assert.Empty(actors.Envelopes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Saved_duplicate_rechecks_current_source_after_awaited_execution_lookup(bool ef)
    {
        await using var harness = await Harness.Create(ef); var source = new PostCommitDeliveryContext(await harness.Seed(), Scope);
        await harness.Delivery.InstallAsync(source, AdmissionSnapshot(source));
        var target = KeyedWorkflowStartIdentity.For("key", Pin.ArtifactId).WorkflowExecutionId;
        await harness.Executions.SaveAsync(Publisher with { WorkflowExecutionId = target, Authority = WorkflowExecutionAuthoritySnapshot.CreateRoot("runtime.publish-test") });
        var read = new StartExecutionRead(harness.Executions, target, async () =>
        {
            var peer = (IRuntimePostCommitOutboxClaimStore)harness.Reopen();
            await peer.RecordDeliveryResultAsync(source.Claim, new(source.SourceOutboxItemId, RuntimePostCommitOutboxStatus.FailedRetryable, Now, "retry"));
            harness.Clock.Advance(TimeSpan.FromSeconds(2));
            Assert.Single(await peer.ClaimAsync(new("peer", Now.AddSeconds(2), TimeSpan.FromMinutes(1), 1)));
        });
        var actors = new StartActors();
        await Assert.ThrowsAsync<RuntimePostCommitOutboxStaleClaimException>(() => SavedDispatcher(harness, actors, new StartPolicy(), read).DispatchAsync(SavedRequest(source)).AsTask());
        Assert.Equal(1, read.Interleavings); Assert.Empty(actors.Activations); Assert.Empty(actors.Envelopes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Saved_start_preserves_a_verified_reference_less_legacy_capture(bool ef)
    {
        await using var harness = await Harness.Create(ef, referenceLess: true); var source = new PostCommitDeliveryContext(await harness.Seed(), Scope);
        var captured = CapturedSnapshot(source, true);
        var candidate = new StimulusDeliverySnapshot(captured.Version, captured.SourceOutboxItemId, captured.IntentFingerprint, captured.Scope,
            captured.SelectedAt, captured.StimulusType, captured.StimulusHash, captured.Starts.Select(start => new StimulusStartRecipient(
                start.Binding with { Metadata = AdmissionMetadata }, start.Executable, null)).ToArray(), []);
        await harness.Delivery.InstallAsync(source, candidate);
        var actors = new StartActors(); var result = await SavedDispatcher(harness, actors, new StartPolicy()).DispatchAsync(SavedRequest(source));
        Assert.Null(result.PinnedSource); Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, result.CommandDispatch.Status);
        var payload = Assert.Single(actors.Envelopes).Command.Payload!.Value.Deserialize<WorkflowExecutionStartCommandPayload>()!;
        Assert.Null(payload.PinnedSource); Assert.Equal(WorkflowExecutableStartAuthorityKind.StimulusDelivery, payload.StartAuthority!.Kind);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task An_admitted_start_command_schedules_its_checkpoint_after_source_acknowledgement_and_publisher_deletion(bool ef)
    {
        await using var harness = await Harness.Create(ef); var source = new PostCommitDeliveryContext(await harness.Seed(), Scope);
        await harness.Delivery.InstallAsync(source, AdmissionSnapshot(source)); Assert.True(await harness.References.RetireAsync("selected-reference", Now));
        var actors = new StartActors(); await SavedDispatcher(harness, actors, new StartPolicy()).DispatchAsync(SavedRequest(source));
        var envelope = Assert.Single(actors.Envelopes);
        await harness.Claims.RecordDeliveryResultAsync(source.Claim, new(source.SourceOutboxItemId, RuntimePostCommitOutboxStatus.Delivered, Now));
        Assert.True(await harness.Executions.DeleteAsync("publisher"));
        await Assert.ThrowsAsync<StimulusDeliveryBoundaryException>(() => harness.Delivery.ReadSavedStartSourceAsync(
            WorkflowExecutableStartAuthority.FromStimulusDelivery(source, "binding-a").StimulusDelivery!).AsTask());
        Assert.Contains(Pin.ArtifactId, await harness.Delivery.ListPinnedExecutableArtifactIdsAsync());
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var handler = new WorkflowStartSchedulerWorkHandler(harness.Artifacts, queue, new NoStartIds(), harness.Clock, harness.Executions);
        await handler.HandleAsync(new("admitted-start", envelope.WorkflowExecutionId, envelope.Command.CommandId, envelope.Command.Kind,
            envelope.EnvelopeId, envelope.IdempotencyKey, envelope.EnqueuedAt, Now, 1, envelope.Command.Payload, envelope.Command.Metadata, envelope.Metadata));
        var checkpoint = Assert.Single(await queue.ListAllAsync(new RuntimeSchedulerWorkQuery(envelope.WorkflowExecutionId)));
        var payload = checkpoint.Payload!.Value.Deserialize<RuntimeCheckpointCommandPayload>()!;
        Assert.Equal(RuntimeCheckpointNames.WorkflowStarted, payload.CheckpointName); Assert.Equal(Pin, payload.PinnedExecutable);
        Assert.Equal(SelectedSource(), payload.PinnedSource); Assert.Equal(AdmissionMetadata, payload.SeedTriggerMetadata);
        Assert.Null(payload.ParentWorkflowExecutionId); Assert.Equal("tenant", payload.TenantId);
    }

    private sealed class StartExecutionRead(IWorkflowExecutionStateStore inner, string target, Func<Task> interleave) : IWorkflowExecutionStateStore
    {
        public int Interleavings { get; private set; }
        public async ValueTask<WorkflowExecutionState?> FindAsync(string id, CancellationToken cancellationToken = default)
        { var result = await inner.FindAsync(id, cancellationToken); if (id == target && Interleavings == 0) { Interleavings++; await interleave(); } return result; }
        public ValueTask<WorkflowExecutionState> SaveAsync(WorkflowExecutionState state, CancellationToken cancellationToken = default) => inner.SaveAsync(state, cancellationToken);
        public ValueTask<bool> IsDeletionTombstonedAsync(string id, CancellationToken cancellationToken = default) => inner.IsDeletionTombstonedAsync(id, cancellationToken);
        public ValueTask<IReadOnlyCollection<WorkflowExecutionState>> ListAsync(CancellationToken cancellationToken = default) => inner.ListAsync(cancellationToken);
        public ValueTask<WorkflowExecutionStatePage> QueryPageAsync(WorkflowExecutionStatePageQuery query, CancellationToken cancellationToken = default) => inner.QueryPageAsync(query, cancellationToken);
        public ValueTask<IReadOnlyCollection<string>> ListPinnedExecutableArtifactIdsAsync(CancellationToken cancellationToken = default) => inner.ListPinnedExecutableArtifactIdsAsync(cancellationToken);
        public ValueTask<bool> DeleteAsync(string id, CancellationToken cancellationToken = default) => inner.DeleteAsync(id, cancellationToken);
    }

    private static IReadOnlyDictionary<string, string> AdmissionMetadata => new Dictionary<string, string> { ["startElementId"] = "bpmn-start", ["descriptor"] = "same-artifact" };
    private static StimulusDeliverySnapshot AdmissionSnapshot(PostCommitDeliveryContext source, WorkflowExecutionState? publisher = null, bool originalMetadata = false)
    {
        var candidate = Snapshot(source, publisher: publisher);
        return new(candidate.Version, candidate.SourceOutboxItemId, candidate.IntentFingerprint, candidate.Scope, candidate.SelectedAt,
            candidate.StimulusType, candidate.StimulusHash, candidate.Starts.Select(start => new StimulusStartRecipient(
                start.Binding with { Metadata = originalMetadata ? start.Binding.Metadata : AdmissionMetadata }, start.Executable, start.SourceProvenance)).ToArray(), []);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Saved_metadata_that_ordinary_command_wire_cannot_preserve_refuses_before_actor_lookup(bool ef)
    {
        await using var harness = await Harness.Create(ef); var source = new PostCommitDeliveryContext(await harness.Seed(), Scope);
        await harness.Delivery.InstallAsync(source, AdmissionSnapshot(source, originalMetadata: true)); // Keeps the unpaired UTF-16 storage control.
        var request = SavedRequest(source);
        request = new(request.ArtifactId, request.RequestedBy, request.WorkflowExecutionId, request.IdempotencyKey, null, null, null,
            request.StimulusInput, request.TriggerNodeId, request.RunKind, null, request.ProvenanceRequirement, null, null,
            request.TenantId, request.Partition, request.Authority, request.StartAuthority, triggerMetadata: Start("binding-a").Binding.Metadata);
        var actors = new StartActors();
        var refusal = await Assert.ThrowsAsync<WorkflowExecutableStartRejectedException>(() => SavedDispatcher(harness, actors, new StartPolicy()).DispatchAsync(request).AsTask());
        Assert.Equal("stimulus-delivery-command-unrepresentable", refusal.ReasonCode);
        Assert.Empty(actors.Activations); Assert.Empty(actors.Envelopes);
        Assert.NotNull((await harness.Lookup.FindAsync(source.SourceOutboxItemId))!.RecipientSnapshot);
    }

    private static WorkflowStartDispatcher SavedDispatcher(Harness harness, StartActors actors, StartPolicy policy, IWorkflowExecutionStateStore? executions = null) => new(
        harness.Artifacts, harness.References, actors, new NoStartIds(), harness.Clock, null, policy, null, executions ?? harness.Executions, null, harness.Delivery,
        [new SavedStartIntentAdapter()]);

    private sealed class SavedStartIntentAdapter : IStimulusDeliveryIntentAdapter
    {
        public string IntentKind => "publish";
        public StimulusDispatchRequest CreateRequest(RuntimePostCommitIntent intent) => new("event", "named", intent.Payload,
            idempotencyKey: intent.IdempotencyKey, requestedBy: "runtime.publish-test") { ResumeAllMatchingBookmarks = true };
    }

    private static WorkflowExecutionStartDispatchRequest SavedRequest(PostCommitDeliveryContext source, string? defect = null, string binding = "binding-a", WorkflowExecutionState? publisher = null, WorkflowExecutableStartAuthority? overrideAuthority = null)
    {
        var key = KeyedWorkflowStartIdentity.For("key", Pin.ArtifactId); var entry = Start(binding);
        return new(defect == "artifact" ? "other" : Pin.ArtifactId, defect == "requested-by" ? "caller-identity" : "runtime.publish-test",
            defect == "execution" ? "other-execution" : key.WorkflowExecutionId, defect == "key" ? "other-key" : key.StartKey,
            null, defect == "variables" ? new Dictionary<string, object?> { ["injected"] = 1 } : null,
            defect == "inputs" ? new Dictionary<string, object?> { ["injected"] = 1 } : null,
            defect == "stimulus-input-missing" ? null : defect == "stimulus-input-changed" ? JsonSerializer.SerializeToElement("caller-changed") : source.Intent.Payload,
            defect == "node" ? "other-node" : entry.Binding.ExecutableNodeId,
            defect == "run" ? WorkflowRunKind.Unknown : WorkflowRunKind.PublishedRun, null,
            WorkflowExecutableProvenanceRequirement.AllowReferenceLessLegacy, defect == "parent" ? "other-parent" : null,
            defect == "correlation" ? "other-correlation" : null, defect == "tenant" ? "foreign" : (publisher ?? Publisher).TenantId,
            defect == "partition" ? new("foreign") : (publisher ?? Publisher).Partition,
            defect == "authority" ? new("runtime.publish-test", "foreign-root") : null,
            overrideAuthority ?? WorkflowExecutableStartAuthority.FromStimulusDelivery(source, binding), defect == "depth" ? 1 : 0, null,
            defect == "metadata" ? new Dictionary<string, string> { ["injected"] = "other" } : AdmissionMetadata);
    }

    private sealed class StartPolicy : IWorkflowExecutableStartPolicy
    {
        public List<WorkflowExecutableStartPolicyContext> Contexts { get; } = [];
        public WorkflowExecutableStartDecision Decision { get; set; } = WorkflowExecutableStartDecision.Allow();
        public Func<Task>? BeforeDecision { get; set; }
        public async ValueTask<WorkflowExecutableStartDecision> EvaluateAsync(WorkflowExecutableStartPolicyContext context, CancellationToken cancellationToken = default)
        { Contexts.Add(context); if (BeforeDecision is { } callback) { BeforeDecision = null; await callback(); } return Decision; }
    }

    private sealed class NoStartIds : IRuntimeExecutionIdGenerator
    {
        public string NewWorkflowExecutionId() => throw new InvalidOperationException("A saved start must keep its original key.");
        public string NewWorkflowExecutionCommandId() => throw new InvalidOperationException("A saved start must keep its original key.");
        public string NewWorkflowExecutionCommandEnvelopeId() => throw new InvalidOperationException("A saved start must keep its original key.");
        public string NewActivityExecutionId() => "activity-saved-root";
    }

    private sealed class StartActors : IWorkflowExecutionActorProvider, IWorkflowExecutionActor
    {
        public List<WorkflowExecutionActorActivationRequest> Activations { get; } = [];
        public List<WorkflowExecutionCommandEnvelope> Envelopes { get; } = [];
        public List<WorkflowExecutionCommandDispatchOptions> Options { get; } = [];
        public Func<Task>? BeforeLookup { get; set; }
        public bool ShedFirst { get; set; }
        public WorkflowExecutionActorCapabilities Capabilities => WorkflowExecutionActorCapabilities.None;
        public WorkflowExecutionActorDescriptor Descriptor => new(Activations[^1].WorkflowExecutionId, "recording", "recording",
            WorkflowExecutionActorStatus.Active, Capabilities, Now);
        public async ValueTask<IWorkflowExecutionActor> GetAgentAsync(WorkflowExecutionActorActivationRequest request, CancellationToken cancellationToken = default)
        { Activations.Add(request); if (BeforeLookup is { } callback) { BeforeLookup = null; await callback(); } return this; }
        public ValueTask PassivateAsync(WorkflowExecutionActorPassivationRequest request, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<WorkflowExecutionCommandDispatchResult> EnqueueAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default) =>
            EnqueueAsync(envelope, WorkflowExecutionCommandDispatchOptions.Default, cancellationToken);
        public ValueTask<WorkflowExecutionCommandDispatchResult> EnqueueAsync(WorkflowExecutionCommandEnvelope envelope, WorkflowExecutionCommandDispatchOptions options, CancellationToken cancellationToken = default)
        {
            Envelopes.Add(envelope); Options.Add(options); var shed = ShedFirst && Envelopes.Count == 1;
            return ValueTask.FromResult(new WorkflowExecutionCommandDispatchResult(envelope.EnvelopeId, envelope.WorkflowExecutionId,
                shed ? WorkflowExecutionCommandDispatchStatus.Deferred : WorkflowExecutionCommandDispatchStatus.Accepted, Now,
                shed ? "capacity" : null, shed ? new Dictionary<string, string> { [RuntimeMetadataKeys.DispatchShed] = "true" } : null));
        }
    }
}
