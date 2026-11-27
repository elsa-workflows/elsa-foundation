using System.Data.Common;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Extensions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class StimulusDeliveryStoreTests
{
    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task Old_provider_constructors_and_legacy_lease_managers_refuse_verified_installation(bool ef, bool legacyManager)
    {
        await using var harness = await Harness.Create(ef); var source = new PostCommitDeliveryContext(await harness.Seed(), Scope);
        var refusal = await Assert.ThrowsAsync<StimulusDeliveryBoundaryException>(() => harness.Unsupported(legacyManager).InstallAsync(source, Snapshot(source)).AsTask());
        Assert.False(refusal.IsUnavailable); Assert.IsType<NotSupportedException>(refusal.InnerException);
        Assert.Null((await harness.Lookup.FindAsync(source.SourceOutboxItemId))!.RecipientSnapshot);
        Assert.Equal(1, (await harness.Delivery.InstallAsync(source, Snapshot(source))).RetentionAcceptanceVersion);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task EF_canonical_selection_is_bound_to_acceptance_scope_even_with_a_foreign_reader(bool onlyForeign)
    {
        await using var harness = await Harness.Create(true); var source = new PostCommitDeliveryContext(await harness.Seed(), Scope);
        var foreign = new EfWorkflowExecutableSourceReferenceStore(harness.Context!, new Accessor(new("foreign")), new Codec());
        if (onlyForeign)
        {
            Assert.True(await harness.References.RetireAsync("selected-reference", Now));
            await foreign.SaveAsync(new("selected-reference", Pin.ArtifactId, "definition", "selected-definition", "2",
                "selected-definition", "selected-publication-version", "selected-label", Now, Now,
                WorkflowExecutableReferenceScope.Published, ActivationId: "activation", SlotId: "slot"));
        }
        var store = new EfRuntimePostCommitOutboxStore(harness.Context!, new Accessor(), harness.Clock, foreign,
            new WorkflowExecutableRootWriteLeaseManager(harness.Artifacts, Options.Create(new WorkflowExecutableGarbageCollectionOptions()), harness.Clock));
        if (onlyForeign)
        {
            var refusal = await Assert.ThrowsAsync<StimulusDeliveryBoundaryException>(() => store.InstallAsync(source, Snapshot(source)).AsTask());
            Assert.False(refusal.IsUnavailable);
            Assert.IsType<WorkflowExecutableReferenceRejectedException>(refusal.InnerException);
            Assert.Null((await harness.Lookup.FindAsync(source.SourceOutboxItemId))!.RecipientSnapshot);
        }
        else Assert.Equal(1, (await store.InstallAsync(source, Snapshot(source))).RetentionAcceptanceVersion);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task Publisher_full_pin_is_revalidated_after_discovery_even_when_artifact_id_and_hash_match(bool ef, bool changed)
    {
        await using var harness = await Harness.Create(ef); var source = new PostCommitDeliveryContext(await harness.Seed(), Scope);
        var before = harness.Context is null ? null : await harness.Context.WorkflowExecutableCoordinations.AsNoTracking().Select(row => row.Revision).ToArrayAsync();
        harness.BeforeAcceptance = async () => await harness.SavePublisher(changed ? Publisher with
            { PinnedExecutable = Pin with { DefinitionVersionId = "replacement-version" } } : Publisher);
        if (changed)
        {
            var refusal = await Assert.ThrowsAsync<StimulusDeliveryBoundaryException>(() => harness.Delivery.InstallAsync(source, Snapshot(source)).AsTask());
            Assert.False(refusal.IsUnavailable); Assert.IsType<InvalidDataException>(refusal.InnerException);
            Assert.Null((await harness.Lookup.FindAsync(source.SourceOutboxItemId))!.RecipientSnapshot);
            if (harness.Context is not null)
                Assert.Equal(before!, await harness.Context.WorkflowExecutableCoordinations.AsNoTracking().Select(row => row.Revision).ToArrayAsync());
        }
        else Assert.Equal(1, (await harness.Delivery.InstallAsync(source, Snapshot(source))).RetentionAcceptanceVersion);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task Native_runtime_DI_uses_its_actual_physical_store_and_refuses_a_late_foreign_replacement(bool coalesced, bool replace)
    {
        var services = new ServiceCollection(); services.AddLogging();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
        services.AddWorkflowRuntime();
        services.Replace(ServiceDescriptor.Singleton<IPersistenceAccessContextAccessor>(new Accessor()));
        if (coalesced) services.AddPersistenceModes();
        if (replace) services.AddSingleton<IWorkflowExecutionStateStore>(new InMemoryWorkflowExecutionStateStore());
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope(); var runtime = scope.ServiceProvider;
        await runtime.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(Material(Pin));
        await runtime.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().SaveAsync(new("selected-reference", Pin.ArtifactId,
            "definition", "selected-definition", "2", "selected-definition", "selected-publication-version", "selected-label",
            Now, Now, WorkflowExecutableReferenceScope.Published, ActivationId: "activation", SlotId: "slot"));
        await runtime.GetRequiredService<IWorkflowExecutionStateStore>().SaveAsync(Publisher);
        var store = runtime.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>();
        await store.AddPendingForTestingAsync(Pending("di-composition", true));
        var source = new PostCommitDeliveryContext(Assert.Single(await store.ClaimAsync(new("owner", Now, TimeSpan.FromMinutes(1), 1))), Scope);
        var delivery = runtime.GetRequiredService<IStimulusDeliveryStore>(); Assert.Same(store, delivery);
        RuntimeCoalescingSession? session = null;
        if (coalesced && !replace)
        {
            session = new("publisher", runtime.GetRequiredService<IWorkflowSchedulerWorkQueue>(), new PersistenceModeOptions(), store);
            session.RecordCapFlushState(new(new("publisher", RuntimeStateChangeOperation.Upsert,
                Publisher with { TenantId = "uncommitted-overlay" }, new Dictionary<string, string>()), null, [], [], [], [], []));
        }
        using var ambient = coalesced ? runtime.GetRequiredService<IRuntimeCoalescingSessionAccessor>().Push(session) : null;
        if (session is not null)
            Assert.Equal("uncommitted-overlay", (await runtime.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("publisher"))!.TenantId);
        if (replace)
        {
            var refusal = await Assert.ThrowsAsync<StimulusDeliveryBoundaryException>(() => delivery.InstallAsync(source, Snapshot(source)).AsTask());
            Assert.IsType<NotSupportedException>(refusal.InnerException);
            Assert.Null((await store.FindAsync(source.SourceOutboxItemId))!.RecipientSnapshot);
        }
        else Assert.Equal(1, (await delivery.InstallAsync(source, Snapshot(source))).RetentionAcceptanceVersion);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Actual_provider_grant_loss_between_capture_discovery_and_acceptance_refuses_the_cohort(bool ef)
    {
        await using var harness = await Harness.Create(ef); var source = new PostCommitDeliveryContext(await harness.Seed(), Scope);
        harness.BeforeAcceptance = async () =>
        {
            if (harness.Context is not null)
                Assert.True(await harness.Context.WorkflowExecutableRootWriteLeases.ExecuteDeleteAsync() > 0);
            else
            {
                Assert.True(await harness.Artifacts.DeleteAsync(Pin.ArtifactId));
                await harness.Artifacts.SaveAsync(Material(Pin));
            }
        };
        var refusal = await Assert.ThrowsAsync<StimulusDeliveryBoundaryException>(() => harness.Delivery.InstallAsync(source, Snapshot(source)).AsTask());
        Assert.True(refusal.IsUnavailable); Assert.IsType<WorkflowExecutableRootWriteLeaseLostException>(refusal.InnerException);
        Assert.Null((await harness.Lookup.FindAsync(source.SourceOutboxItemId))!.RecipientSnapshot);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task Verified_cohort_witness_cannot_be_minted_by_new_pending_or_checkpoint_writers(bool ef, bool checkpoint)
    {
        await using var harness = await Harness.Create(ef); await harness.EnsureMaterial();
        var item = Pending("forged-witness", true);
        var snapshot = new StimulusDeliverySnapshot(1, item.OutboxItemId, RuntimeCheckpointCommitFingerprint.ComputeIntent(item.Intent),
            StimulusDeliveryScope.From(Publisher, Scope), Now, "event", "named", [], [], StimulusDeliverySnapshot.CurrentRetentionAcceptanceVersion);
        var forged = new RuntimePostCommitOutboxItem(item.OutboxItemId, item.Intent, item.Status, item.RecordedAt, item.AvailableAt,
            item.RetryPolicy, 0, null, null, null, null, null, 0, null, 1, Scope, snapshot);
        RuntimeCheckpointCommit Envelope(RuntimePostCommitOutboxItem proposed) => new("witness-commit", new("checkpoint", "EmptyCheckpoint", "publisher", Now, [],
            new Dictionary<string, string>()), new(new("publisher", RuntimeStateChangeOperation.Upsert, Publisher, new Dictionary<string, string>()),
            null, [], [], [], [], [], postCommitOutbox: [new(proposed.OutboxItemId, RuntimeStateChangeOperation.Upsert, proposed, new Dictionary<string, string>())]),
            [], new Dictionary<string, string>());
        if (checkpoint) await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Checkpoints.CommitAsync(Envelope(forged), new(RuntimeCheckpointPersistenceMode.Immediate)).AsTask());
        else await Assert.ThrowsAsync<InvalidOperationException>(() => harness.SavePending(forged).AsTask());
        Assert.Null(await harness.Lookup.FindAsync(item.OutboxItemId)); Assert.Null(await harness.FindPublisher());
        if (harness.Context is not null) Assert.Empty(await harness.Context.RuntimeCheckpointCommits.AsNoTracking().ToArrayAsync());
        else Assert.Empty(((InMemoryRuntimeCheckpointCommitStore)harness.Outbox).ListCommits());
        if (checkpoint) await harness.Checkpoints.CommitAsync(Envelope(item), new(RuntimeCheckpointPersistenceMode.Immediate));
        else await harness.SavePending(item);
        Assert.Null((await harness.Lookup.FindAsync(item.OutboxItemId))!.RecipientSnapshot);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task An_existing_data_only_cohort_is_held_and_never_restamped_from_a_new_proposal(bool ef)
    {
        await using var harness = await Harness.Create(ef); await harness.EnsureMaterial(); await harness.SavePublisher(Publisher);
        var item = Pending("older-data", true);
        var older = new StimulusDeliverySnapshot(1, item.OutboxItemId, RuntimeCheckpointCommitFingerprint.ComputeIntent(item.Intent),
            StimulusDeliveryScope.From(Publisher, Scope), Now, "event", "named", [Start("binding-a")], []);
        var pending = new RuntimePostCommitOutboxItem(item.OutboxItemId, item.Intent, item.Status, item.RecordedAt, item.AvailableAt,
            item.RetryPolicy, 0, null, null, null, null, null, 0, null, 1, Scope, older);
        await harness.SavePending(pending);
        var source = new PostCommitDeliveryContext(Assert.Single(await harness.Claims.ClaimAsync(new("owner", Now, TimeSpan.FromMinutes(1), 1))), Scope);
        Assert.True(await harness.References.RetireAsync("selected-reference", Now));
        var refusal = await Assert.ThrowsAsync<StimulusDeliveryBoundaryException>(() => harness.Delivery.InstallAsync(source, Snapshot(source, true)).AsTask());
        Assert.False(refusal.IsUnavailable); Assert.IsType<InvalidDataException>(refusal.InnerException);
        var saved = (await harness.Lookup.FindAsync(item.OutboxItemId))!.RecipientSnapshot!;
        Assert.Null(saved.RetentionAcceptanceVersion); Assert.True(older.IsEquivalentTo(saved));
        Assert.Equal(0, (await harness.Lookup.FindAsync(item.OutboxItemId))!.DeliveryAttemptCount);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task Verified_acceptance_refuses_retired_start_or_resume_dependencies_without_partial_fences(bool ef, bool resume)
    {
        await using var harness = await Harness.Create(ef); var source = new PostCommitDeliveryContext(await harness.Seed(), Scope);
        var target = resume ? "waiter" : KeyedWorkflowStartIdentity.For("key", Pin.ArtifactId).WorkflowExecutionId;
        await harness.Executions.SaveAsync(Publisher with { WorkflowExecutionId = target, Status = WorkflowExecutionStatus.Completed });
        Assert.True(await harness.Executions.DeleteAsync(target)); Assert.True(await harness.Executions.IsDeletionTombstonedAsync(target));
        var before = harness.Context is null ? null : await harness.Context.WorkflowExecutableCoordinations.AsNoTracking().OrderBy(row => row.Id).Select(row => row.Revision).ToArrayAsync();
        var ownerCount = harness.Context is null ? 0 : await harness.Context.ExecutionLivenessStates.CountAsync();
        var refusal = await Assert.ThrowsAsync<StimulusDeliveryBoundaryException>(() => harness.Delivery.InstallAsync(source, Snapshot(source)).AsTask());
        Assert.False(refusal.IsUnavailable); Assert.IsType<InvalidDataException>(refusal.InnerException);
        Assert.Null((await harness.Lookup.FindAsync(source.SourceOutboxItemId))!.RecipientSnapshot);
        if (harness.Context is not null)
        {
            Assert.Equal(before!, await harness.Context.WorkflowExecutableCoordinations.AsNoTracking().OrderBy(row => row.Id).Select(row => row.Revision).ToArrayAsync());
            Assert.Equal(ownerCount, await harness.Context.ExecutionLivenessStates.CountAsync());
        }
        Assert.True(await harness.Executions.IsDeletionTombstonedAsync(target));
    }

    [Theory]
    [InlineData(false, "reference-less")] [InlineData(true, "reference-less")]
    [InlineData(false, "other-publication")] [InlineData(true, "other-publication")]
    [InlineData(false, "ambiguous")] [InlineData(true, "ambiguous")]
    public async Task Verified_acceptance_uses_actual_ordinary_capture_instead_of_trusting_candidate_provenance(bool ef, string defect)
    {
        await using var harness = await Harness.Create(ef); var source = new PostCommitDeliveryContext(await harness.Seed(), Scope);
        var candidate = defect == "reference-less" ? CapturedSnapshot(source, true) : Snapshot(source);
        if (defect == "other-publication")
            candidate = new(candidate.Version, candidate.SourceOutboxItemId, candidate.IntentFingerprint, candidate.Scope, candidate.SelectedAt,
                candidate.StimulusType, candidate.StimulusHash, candidate.Starts.Select(start => new StimulusStartRecipient(start.Binding,
                    start.Executable, start.SourceProvenance! with { DefinitionVersionId = "forged-version" })).ToArray(), candidate.Resumes,
                StimulusDeliverySnapshot.CurrentRetentionAcceptanceVersion);
        if (defect == "ambiguous")
            await harness.References.SaveAsync(new("other-reference", Pin.ArtifactId, "definition", "selected-definition", "2",
                "selected-definition", "selected-publication-version", "selected-label", Now, Now, WorkflowExecutableReferenceScope.Published,
                ActivationId: "activation", SlotId: "slot"));
        var refusal = await Assert.ThrowsAsync<StimulusDeliveryBoundaryException>(() => harness.Delivery.InstallAsync(source, candidate).AsTask());
        Assert.False(refusal.IsUnavailable);
        Assert.Null((await harness.Lookup.FindAsync(source.SourceOutboxItemId))!.RecipientSnapshot);
        if (defect == "ambiguous") Assert.True(await harness.References.RetireAsync("other-reference", Now));
        var valid = await harness.Delivery.InstallAsync(source, Snapshot(source));
        Assert.Equal(StimulusDeliverySnapshot.CurrentRetentionAcceptanceVersion, valid.RetentionAcceptanceVersion);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Recipient_deletion_winning_after_source_discovery_refuses_automatic_cohort_acceptance(bool ef)
    {
        await using var harness = await Harness.Create(ef); var source = new PostCommitDeliveryContext(await harness.Seed(), Scope);
        var target = KeyedWorkflowStartIdentity.For("key", Pin.ArtifactId).WorkflowExecutionId;
        await harness.Executions.SaveAsync(Publisher with { WorkflowExecutionId = target, Status = WorkflowExecutionStatus.Completed });
        var deleted = false;
        harness.BeforeAcceptance = async () => deleted = await harness.Executions.DeleteAsync(target);
        await Assert.ThrowsAsync<StimulusDeliveryBoundaryException>(() => harness.Delivery.InstallAsync(source, Snapshot(source)).AsTask());
        Assert.True(deleted); Assert.True(await harness.Executions.IsDeletionTombstonedAsync(target));
        Assert.Null((await harness.Lookup.FindAsync(source.SourceOutboxItemId))!.RecipientSnapshot);
    }

    [Fact]
    public async Task Native_unknown_post_commit_outcome_reuses_verified_winner_after_reference_retirement()
    {
        await using var harness = await Harness.Create(false); var source = new PostCommitDeliveryContext(await harness.Seed(), Scope);
        harness.LoseInstallAcknowledgement = true;
        var failure = await Assert.ThrowsAsync<StimulusDeliveryBoundaryException>(() => harness.Delivery.InstallAsync(source, Snapshot(source)).AsTask());
        Assert.True(failure.IsUnavailable); Assert.Same(harness.LostInstallAcknowledgement, failure.InnerException);
        var saved = (await harness.Lookup.FindAsync(source.SourceOutboxItemId))!.RecipientSnapshot!;
        Assert.Equal(StimulusDeliverySnapshot.CurrentRetentionAcceptanceVersion, saved.RetentionAcceptanceVersion);
        Assert.True(await harness.References.RetireAsync("selected-reference", Now));
        Assert.True(saved.IsEquivalentTo(await harness.Reopen().InstallAsync(source, Snapshot(source, true))));
        Assert.Equal(2, saved.Starts.Count);
    }

    [Fact]
    public async Task Actual_EF_commit_acknowledgement_loss_reuses_exact_verified_winner_without_live_recapture()
    {
        var loss = new AfterCohortCommitLoss();
        await using var harness = await Harness.Create(true, loss); var source = new PostCommitDeliveryContext(await harness.Seed(), Scope);
        loss.Armed = true;
        var failure = await Assert.ThrowsAsync<StimulusDeliveryBoundaryException>(() => harness.Delivery.InstallAsync(source, Snapshot(source)).AsTask());
        Assert.True(failure.IsUnavailable); Assert.Same(loss.Cause, failure.InnerException); Assert.Equal(1, loss.LostAcknowledgements);
        var saved = (await harness.Lookup.FindAsync(source.SourceOutboxItemId))!.RecipientSnapshot!;
        Assert.Equal(StimulusDeliverySnapshot.CurrentRetentionAcceptanceVersion, saved.RetentionAcceptanceVersion);
        Assert.True(await harness.References.RetireAsync("selected-reference", Now));
        var reopened = await harness.Reopen().InstallAsync(source, Snapshot(source, true));
        Assert.True(saved.IsEquivalentTo(reopened)); Assert.Equal(2, reopened.Starts.Count);
        Assert.Equal(3, (await harness.Context!.RuntimePostCommitOutbox.AsNoTracking().SingleAsync()).Revision);
    }

    private sealed class AfterCohortCommitLoss : DbTransactionInterceptor
    {
        public bool Armed { get; set; }
        public int LostAcknowledgements { get; private set; }
        public IOException Cause { get; } = new("Constructed acknowledgement loss after actual EF cohort transaction commit.");
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (Armed) { Armed = false; LostAcknowledgements++; throw Cause; }
            return Task.CompletedTask;
        }
    }

}
