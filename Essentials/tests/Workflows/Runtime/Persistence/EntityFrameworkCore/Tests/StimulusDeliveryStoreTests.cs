using System.Data.Common;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Services.Executables;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Incidents;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>Actual provider CAS and rewrite controls; these do not qualify recipient dispatch/host recovery.</summary>
public sealed partial class StimulusDeliveryStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 1, 0, 0, TimeSpan.Zero);
    private static readonly PersistenceScope Scope = new("tenant");
    private static readonly WorkflowExecutableIdentity Pin = new("artifact", "definition", "version", "1.0.0", "hash");
    private static WorkflowExecutionState Publisher => new("publisher", Pin, WorkflowExecutionStatus.Running, null,
        Now, Now, null, null, null, null, "tenant", new Dictionary<string, string>())
        { RunKind = WorkflowRunKind.PublishedRun, Partition = new("partition") };

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public async Task First_complete_or_empty_set_wins_and_is_read_after_provider_restart(bool ef, bool emptyFirst)
    {
        await using var harness = await Harness.Create(ef);
        var claim = await harness.Seed();
        var source = new PostCommitDeliveryContext(claim, Scope);
        var first = Snapshot(source, emptyFirst);
        var saved = await harness.Delivery.InstallAsync(source, first);
        var reopened = harness.Reopen();
        var winner = await reopened.InstallAsync(source, Snapshot(source, !emptyFirst));
        Assert.True(first.IsEquivalentTo(saved));
        Assert.True(first.IsEquivalentTo(winner));
        Assert.True(first.IsEquivalentTo((await harness.Lookup.FindAsync(claim.OutboxItemId))!.RecipientSnapshot));
        Assert.Equal(emptyFirst ? 0 : 2, winner.Resumes.Count);
        if (ef)
        {
            var row = await harness.Context!.RuntimePostCommitOutbox.AsNoTracking().SingleAsync();
            Assert.Equal("2.0.0", row.SchemaVersion);
            Assert.Equal(3, row.Revision); // create, claim, one install; a losing proposal never rewrites.
        }
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public async Task Source_capture_survives_saved_winner_restart_renew_retry_and_completion(bool ef, bool referenceLess)
    {
        await using var harness = await Harness.Create(ef, referenceLess: referenceLess);
        var claim = await harness.Seed(); var source = new PostCommitDeliveryContext(claim, Scope);
        var candidate = CapturedSnapshot(source, referenceLess);
        var winner = await harness.Delivery.InstallAsync(source, candidate);
        var reopened = harness.Reopen();
        Assert.True(winner.IsEquivalentTo(await reopened.InstallAsync(source, Snapshot(source))));
        harness.Clock.Advance(TimeSpan.FromSeconds(1));
        var renewed = await harness.Claims.RenewClaimAsync(claim, Now.AddSeconds(1), TimeSpan.FromMinutes(2));
        Assert.True(candidate.IsEquivalentTo(renewed!.Item.RecipientSnapshot));
        harness.Clock.Advance(TimeSpan.FromSeconds(1));
        await harness.Claims.RecordDeliveryResultAsync(claim, new(claim.OutboxItemId,
            RuntimePostCommitOutboxStatus.FailedRetryable, Now.AddSeconds(2), "retry"));
        harness.Clock.Advance(TimeSpan.FromSeconds(2));
        var retried = Assert.Single(await harness.Claims.ClaimAsync(new("next", Now.AddSeconds(4), TimeSpan.FromMinutes(1), 1)));
        harness.Clock.Advance(TimeSpan.FromSeconds(1));
        await harness.Completion.CompleteClaimAsync(new(retried,
            new(retried.OutboxItemId, RuntimePostCommitOutboxStatus.Delivered, Now.AddSeconds(5))));
        var saved = (await harness.Lookup.FindDurableAsync(claim.OutboxItemId))!.RecipientSnapshot!;
        Assert.True(candidate.IsEquivalentTo(saved));
        Assert.All(saved.Starts, entry => Assert.True(entry.SourceProvenanceCaptured));
        if (referenceLess) Assert.All(saved.Starts, entry => Assert.Null(entry.SourceProvenance));
        else Assert.All(saved.Starts, entry =>
        {
            Assert.Equal("selected-publication-version", entry.SourceProvenance!.DefinitionVersionId);
            Assert.Equal("version", entry.Executable.DefinitionVersionId);
        });
    }

    [Theory]
    [InlineData("capture-version")]
    [InlineData("capture-provenance")]
    [InlineData("source-reference")]
    public async Task Corrupt_present_source_capture_refuses_through_actual_EF_reader(string defect)
    {
        await using var harness = await Harness.Create(true);
        var source = new PostCommitDeliveryContext(await harness.Seed(), Scope);
        await harness.Delivery.InstallAsync(source, CapturedSnapshot(source, false));
        var row = await harness.Context!.RuntimePostCommitOutbox.SingleAsync();
        var json = JsonNode.Parse(row.ContentJson)!;
        var capture = json["recipientSnapshot"]!["starts"]![0]!["sourceCapture"]!.AsObject();
        if (defect == "source-reference") Assert.True(capture["provenance"]!.AsObject().Remove("sourceReferenceId"));
        else Assert.True(capture.Remove(defect == "capture-version" ? "version" : "provenance"));
        row.ContentJson = json.ToJsonString(); await harness.Context.SaveChangesAsync(); harness.Context.ChangeTracker.Clear();
        await Assert.ThrowsAsync<InvalidDataException>(() => harness.Lookup.FindDurableAsync(source.SourceOutboxItemId).AsTask());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Saved_start_source_read_returns_only_current_physical_cohort_claim(bool ef)
    {
        await using var harness = await Harness.Create(ef);
        var source = new PostCommitDeliveryContext(await harness.Seed(), Scope);
        var candidate = CapturedSnapshot(source, false);
        await harness.Delivery.InstallAsync(source, candidate);
        var authority = WorkflowExecutableStartAuthority.FromStimulusDelivery(source, "binding-a").StimulusDelivery!;
        var opened = await harness.Reopen().ReadSavedStartSourceAsync(authority);
        Assert.Equal(source.SourceOutboxItemId, opened.SourceOutboxItemId);
        Assert.Equal(source.IntentFingerprint, opened.IntentFingerprint);
        Assert.Equal(source.Claim.OwnerId, opened.Claim.OwnerId);
        Assert.Equal(source.Claim.FencingToken, opened.Claim.FencingToken);
        Assert.True(candidate.IsEquivalentTo(opened.Claim.Item.RecipientSnapshot));
    }

    [Theory]
    [InlineData(false, "source")] [InlineData(true, "source")]
    [InlineData(false, "intent")] [InlineData(true, "intent")]
    [InlineData(false, "scope")] [InlineData(true, "scope")]
    [InlineData(false, "binding")] [InlineData(true, "binding")]
    [InlineData(false, "unselected")] [InlineData(true, "unselected")]
    [InlineData(false, "uncaptured")] [InlineData(true, "uncaptured")]
    [InlineData(false, "publisher")] [InlineData(true, "publisher")]
    public async Task Saved_start_source_read_refuses_missing_or_contradictory_authority(bool ef, string defect)
    {
        await using var harness = await Harness.Create(ef);
        var source = new PostCommitDeliveryContext(await harness.Seed(), Scope);
        if (defect == "uncaptured")
        {
            var candidate = Snapshot(source);
            var uncaptured = new StimulusDeliverySnapshot(candidate.Version, candidate.SourceOutboxItemId, candidate.IntentFingerprint,
                candidate.Scope, candidate.SelectedAt, candidate.StimulusType, candidate.StimulusHash,
                candidate.Starts.Select(start => new StimulusStartRecipient(start.Binding, start.Executable)).ToArray(), candidate.Resumes);
            await Assert.ThrowsAsync<StimulusDeliveryBoundaryException>(() => harness.Delivery.InstallAsync(source, uncaptured).AsTask());
        }
        else if (defect != "unselected") await harness.Delivery.InstallAsync(source, CapturedSnapshot(source, false));
        if (defect == "publisher") await harness.SavePublisher(Publisher with { RunKind = WorkflowRunKind.Unknown });
        var original = WorkflowExecutableStartAuthority.FromStimulusDelivery(source, "binding-a").StimulusDelivery!;
        var presented = new WorkflowExecutableStimulusDeliveryAuthority(defect == "source" ? "missing:send" : original.SourceOutboxItemId,
            defect == "intent" ? new string('0', 64) : original.IntentFingerprint,
            defect == "scope" ? new("foreign") : original.Scope, original.OwnerId, original.FencingToken,
            defect == "binding" ? "not-selected" : original.TriggerBindingId);
        var exception = await Assert.ThrowsAsync<StimulusDeliveryBoundaryException>(() =>
            harness.Delivery.ReadSavedStartSourceAsync(presented).AsTask());
        Assert.False(exception.IsUnavailable); Assert.NotNull(exception.InnerException);
        Assert.Equal(0, (await harness.Lookup.FindAsync(source.SourceOutboxItemId))!.DeliveryAttemptCount);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public async Task Saved_start_source_read_refuses_wrong_current_owner_or_fence(bool ef, bool fence)
    {
        await using var harness = await Harness.Create(ef);
        var source = new PostCommitDeliveryContext(await harness.Seed(), Scope);
        await harness.Delivery.InstallAsync(source, CapturedSnapshot(source, false));
        var authority = new WorkflowExecutableStimulusDeliveryAuthority(source.SourceOutboxItemId, source.IntentFingerprint,
            Scope, fence ? source.Claim.OwnerId : "other", fence ? source.Claim.FencingToken + 1 : source.Claim.FencingToken, "binding-a");
        await Assert.ThrowsAsync<RuntimePostCommitOutboxStaleClaimException>(() => harness.Delivery.ReadSavedStartSourceAsync(authority).AsTask());
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public async Task Saved_start_source_read_uses_trusted_clock_and_current_renewed_deadline(bool ef, bool renew)
    {
        await using var harness = await Harness.Create(ef);
        var source = new PostCommitDeliveryContext(await harness.Seed(), Scope);
        await harness.Delivery.InstallAsync(source, CapturedSnapshot(source, false));
        var authority = WorkflowExecutableStartAuthority.FromStimulusDelivery(source, "binding-a").StimulusDelivery!;
        harness.Clock.Advance(TimeSpan.FromSeconds(30));
        if (renew) Assert.NotNull(await harness.Claims.RenewClaimAsync(source.Claim, Now.AddSeconds(30), TimeSpan.FromMinutes(3)));
        harness.Clock.Advance(TimeSpan.FromSeconds(31));
        if (renew)
        {
            var current = await harness.Delivery.ReadSavedStartSourceAsync(authority);
            Assert.Equal(Now.AddSeconds(30).AddMinutes(3), current.Claim.VisibleAfter);
        }
        else await Assert.ThrowsAsync<RuntimePostCommitOutboxStaleClaimException>(() => harness.Delivery.ReadSavedStartSourceAsync(authority).AsTask());
    }

    [Fact]
    public async Task Saved_start_source_read_refuses_caller_transaction_then_accepts_committed_read()
    {
        await using var harness = await Harness.Create(true);
        var source = new PostCommitDeliveryContext(await harness.Seed(), Scope);
        await harness.Delivery.InstallAsync(source, CapturedSnapshot(source, false));
        var authority = WorkflowExecutableStartAuthority.FromStimulusDelivery(source, "binding-a").StimulusDelivery!;
        await using (var transaction = await harness.Context!.Database.BeginTransactionAsync())
        {
            var exception = await Assert.ThrowsAsync<StimulusDeliveryBoundaryException>(() => harness.Delivery.ReadSavedStartSourceAsync(authority).AsTask());
            Assert.False(exception.IsUnavailable); Assert.IsType<InvalidOperationException>(exception.InnerException);
            await transaction.RollbackAsync();
        }
        Assert.Equal(source.SourceOutboxItemId, (await harness.Delivery.ReadSavedStartSourceAsync(authority)).SourceOutboxItemId);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public async Task Saved_start_source_read_preserves_unavailable_cause_and_requested_cancellation(bool ef, bool providerCancellation)
    {
        await using var harness = await Harness.Create(ef);
        var source = new PostCommitDeliveryContext(await harness.Seed(), Scope);
        var authority = WorkflowExecutableStartAuthority.FromStimulusDelivery(source, "binding-a").StimulusDelivery!;
        Exception cause = providerCancellation ? new OperationCanceledException("provider unavailable") : new IOException("provider unavailable");
        IStimulusDeliveryStore failed = ef ? new EfRuntimePostCommitOutboxStore(harness.Context!, new FailedAccess(cause)) :
            new InMemoryRuntimeCheckpointCommitStore(persistenceAccessContextAccessor: new FailedAccess(cause));
        var exception = await Assert.ThrowsAsync<StimulusDeliveryBoundaryException>(() => failed.ReadSavedStartSourceAsync(authority).AsTask());
        Assert.True(exception.IsUnavailable); Assert.Same(cause, exception.InnerException);
        var token = new CancellationToken(canceled: true);
        var cancelled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => failed.ReadSavedStartSourceAsync(authority, token).AsTask());
        Assert.Equal(token, cancelled.CancellationToken);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Saved_start_source_read_and_cohort_replay_recheck_peer_takeover_during_publisher_lookup(bool replay)
    {
        var hook = new BeforePublisherRead(); await using var harness = await Harness.Create(true, hook);
        var source = new PostCommitDeliveryContext(await harness.Seed(), Scope);
        await harness.Delivery.InstallAsync(source, CapturedSnapshot(source, false));
        var authority = WorkflowExecutableStartAuthority.FromStimulusDelivery(source, "binding-a").StimulusDelivery!;
        hook.Action = async () =>
        {
            var peer = (IRuntimePostCommitOutboxClaimStore)harness.Reopen();
            await peer.RecordDeliveryResultAsync(source.Claim, new(source.SourceOutboxItemId,
                RuntimePostCommitOutboxStatus.FailedRetryable, Now, "retry"));
            harness.Clock.Advance(TimeSpan.FromSeconds(2));
            Assert.Single(await peer.ClaimAsync(new("peer", Now.AddSeconds(2), TimeSpan.FromMinutes(1), 1)));
        };
        if (replay)
            await Assert.ThrowsAsync<RuntimePostCommitOutboxStaleClaimException>(() => harness.Delivery.InstallAsync(source, Snapshot(source)).AsTask());
        else
            await Assert.ThrowsAsync<RuntimePostCommitOutboxStaleClaimException>(() => harness.Delivery.ReadSavedStartSourceAsync(authority).AsTask());
        Assert.Equal(1, hook.Interleavings);
        Assert.True(harness.Clock.GetUtcNow() < source.Claim.VisibleAfter);
        Assert.Equal("peer", (await harness.Lookup.FindAsync(source.SourceOutboxItemId))!.DeliveringOwnerId);
    }

    private sealed class BeforePublisherRead : DbCommandInterceptor
    {
        public Func<Task>? Action { get; set; }
        public int Interleavings { get; private set; }
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Action is { } action && command.CommandText.Contains(RuntimeWorkflowExecutionEfModule.TableName, StringComparison.Ordinal))
            { Action = null; Interleavings++; await action(); }
            return result;
        }
    }

    // Constructed storage witnesses only; no authoritative reference selection/retention or host start is implied.
    private static StimulusDeliverySnapshot CapturedSnapshot(PostCommitDeliveryContext source, bool referenceLess)
    {
        var entries = new[] { Start("binding-a"), Start("binding-b") }.Select(entry => new StimulusStartRecipient(
            referenceLess ? entry.Binding with { ActivationId = null, SlotId = null } : entry.Binding, entry.Executable,
            referenceLess ? null : SelectedSource())).ToArray();
        return new(1, source.SourceOutboxItemId, source.IntentFingerprint, StimulusDeliveryScope.From(Publisher, source.Scope),
            Now, "event", "named", entries, [], StimulusDeliverySnapshot.CurrentRetentionAcceptanceVersion);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Old_claim_content_cannot_erase_winner_on_renew_retry_reclaim_or_atomic_completion(bool ef)
    {
        await using var harness = await Harness.Create(ef);
        var claim = await harness.Seed();
        var source = new PostCommitDeliveryContext(claim, Scope);
        var winner = await harness.Delivery.InstallAsync(source, Snapshot(source));
        Assert.Null(claim.Item.RecipientSnapshot); // the real pre-install object remains stale.
        var renewed = await harness.Claims.RenewClaimAsync(claim, Now.AddSeconds(1), TimeSpan.FromMinutes(2));
        Assert.True(winner.IsEquivalentTo(renewed!.Item.RecipientSnapshot));
        await harness.Claims.RecordDeliveryResultAsync(claim, new(claim.OutboxItemId,
            RuntimePostCommitOutboxStatus.FailedRetryable, Now.AddSeconds(2), "retry"));
        var retried = await harness.Lookup.FindAsync(claim.OutboxItemId);
        Assert.True(winner.IsEquivalentTo(retried!.RecipientSnapshot));
        var next = Assert.Single(await harness.Claims.ClaimAsync(new("next", Now.AddSeconds(4), TimeSpan.FromMinutes(1), 1)));
        Assert.True(winner.IsEquivalentTo(next.Item.RecipientSnapshot));
        await Assert.ThrowsAsync<RuntimePostCommitOutboxStaleClaimException>(() =>
            harness.Delivery.InstallAsync(source, Snapshot(source, true)).AsTask());
        var result = await harness.Completion.CompleteClaimAsync(new(next,
            new(next.OutboxItemId, RuntimePostCommitOutboxStatus.Delivered, Now.AddSeconds(5))));
        Assert.Equal(RuntimePostCommitOutboxClaimCompletionOutcome.Persisted, result);
        var completed = (await harness.Lookup.FindAsync(next.OutboxItemId))!;
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, completed.Status);
        Assert.True(winner.IsEquivalentTo(completed.RecipientSnapshot));
        Assert.Equal(1, completed.StimulusDeliveryProtocolVersion);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Direct_old_claim_acknowledgement_merges_persisted_cohort(bool ef)
    {
        await using var harness = await Harness.Create(ef);
        var claim = await harness.Seed();
        var source = new PostCommitDeliveryContext(claim, Scope);
        var winner = await harness.Delivery.InstallAsync(source, Snapshot(source));
        await harness.Completion.CompleteClaimAsync(new(claim,
            new(claim.OutboxItemId, RuntimePostCommitOutboxStatus.Delivered, Now.AddSeconds(1))));
        Assert.True(winner.IsEquivalentTo((await harness.Lookup.FindAsync(claim.OutboxItemId))!.RecipientSnapshot));
    }

    [Theory]
    [InlineData(false, "legacy")] [InlineData(true, "legacy")]
    [InlineData(false, "intent")] [InlineData(true, "intent")]
    [InlineData(false, "partition")] [InlineData(true, "partition")]
    [InlineData(false, "run-kind")] [InlineData(true, "run-kind")]
    [InlineData(false, "test-scope")] [InlineData(true, "test-scope")]
    [InlineData(false, "scope")] [InlineData(true, "scope")]
    public async Task Legacy_or_contradictory_source_facts_refuse_without_install(bool ef, string defect)
    {
        await using var harness = await Harness.Create(ef);
        var claim = await harness.Seed(protocol: defect != "legacy");
        var source = new PostCommitDeliveryContext(claim, defect == "scope" ? new("foreign") : Scope);
        var publisher = Publisher;
        if (defect == "partition") publisher = publisher with { Partition = null };
        if (defect == "run-kind") publisher = publisher with { RunKind = WorkflowRunKind.Unknown };
        if (defect == "test-scope") publisher = publisher with
        { RunKind = WorkflowRunKind.TestRun, TestScope = new("test", Now.AddHours(1), "tenant", new("partition")) };
        if (defect == "intent")
        {
            var fake = Pending("commit", true, "different");
            var fakeClaim = RuntimePostCommitOutboxClaimTransitions.Claim(fake, new("owner", Now, TimeSpan.FromMinutes(1), 1));
            source = new(fakeClaim, Scope);
        }
        var candidate = Snapshot(source, publisher: publisher);
        var exception = await Assert.ThrowsAsync<StimulusDeliveryBoundaryException>(() =>
            harness.Delivery.InstallAsync(source, candidate).AsTask());
        Assert.False(exception.IsUnavailable);
        Assert.NotNull(exception.InnerException);
        Assert.Null((await harness.Lookup.FindAsync(claim.OutboxItemId))!.RecipientSnapshot);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Expired_claim_without_takeover_cannot_choose_first_cohort(bool ef)
    {
        await using var harness = await Harness.Create(ef);
        var claim = await harness.Seed(); var source = new PostCommitDeliveryContext(claim, Scope);
        harness.Clock.SetUtcNow(claim.VisibleAfter);
        await Assert.ThrowsAsync<RuntimePostCommitOutboxStaleClaimException>(() =>
            harness.Delivery.InstallAsync(source, Snapshot(source)).AsTask());
        Assert.Null((await harness.Lookup.FindAsync(claim.OutboxItemId))!.RecipientSnapshot);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Current_renewed_deadline_admits_stale_claim_object_after_its_original_deadline(bool ef)
    {
        await using var harness = await Harness.Create(ef);
        var claim = await harness.Seed(); var source = new PostCommitDeliveryContext(claim, Scope);
        Assert.NotNull(await harness.Claims.RenewClaimAsync(claim, Now.AddSeconds(30), TimeSpan.FromMinutes(2)));
        harness.Clock.SetUtcNow(claim.VisibleAfter.AddSeconds(1));
        Assert.NotNull(await harness.Delivery.InstallAsync(source, Snapshot(source)));
        harness.Clock.SetUtcNow(Now.AddSeconds(30).AddMinutes(2));
        await Assert.ThrowsAsync<RuntimePostCommitOutboxStaleClaimException>(() =>
            harness.Delivery.InstallAsync(source, Snapshot(source, true)).AsTask());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Same_owner_reclaim_changes_fence_and_refuses_previous_claim(bool ef)
    {
        await using var harness = await Harness.Create(ef);
        var claim = await harness.Seed(); var source = new PostCommitDeliveryContext(claim, Scope);
        harness.Clock.SetUtcNow(claim.VisibleAfter.AddSeconds(1));
        var replacement = Assert.Single(await harness.Claims.ClaimAsync(new(claim.OwnerId, harness.Clock.GetUtcNow(), TimeSpan.FromMinutes(1), 1)));
        Assert.NotEqual(claim.FencingToken, replacement.FencingToken);
        await Assert.ThrowsAsync<RuntimePostCommitOutboxStaleClaimException>(() =>
            harness.Delivery.InstallAsync(source, Snapshot(source)).AsTask());
        Assert.Null((await harness.Lookup.FindAsync(claim.OutboxItemId))!.RecipientSnapshot);
        var replacementSource = new PostCommitDeliveryContext(replacement, Scope);
        Assert.NotNull(await harness.Delivery.InstallAsync(replacementSource, Snapshot(replacementSource)));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Reused_intent_id_in_another_commit_never_shares_a_cohort(bool ef)
    {
        await using var harness = await Harness.Create(ef);
        var first = await harness.Seed();
        var other = Pending("second", true);
        await harness.SavePending(other);
        var second = Assert.Single(await harness.Claims.ClaimAsync(new("owner", Now, TimeSpan.FromMinutes(1), 1)));
        Assert.NotEqual(first.OutboxItemId, second.OutboxItemId);
        var source = new PostCommitDeliveryContext(first, Scope);
        await harness.Delivery.InstallAsync(source, Snapshot(source));
        Assert.Null((await harness.Lookup.FindAsync(second.OutboxItemId))!.RecipientSnapshot);
        var secondSource = new PostCommitDeliveryContext(second, Scope);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            harness.Delivery.InstallAsync(secondSource, Snapshot(source)).AsTask());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Requested_cancellation_leaves_source_untouched(bool ef)
    {
        await using var harness = await Harness.Create(ef);
        var source = new PostCommitDeliveryContext(await harness.Seed(), Scope);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            harness.Delivery.InstallAsync(source, Snapshot(source), cancellation.Token).AsTask());
        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Null((await harness.Lookup.FindAsync(source.SourceOutboxItemId))!.RecipientSnapshot);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Competing_install_reloads_actual_winner_including_empty(bool emptyWinner)
    {
        var race = new InterleavingSave();
        await using var harness = await Harness.Create(true, race);
        var source = new PostCommitDeliveryContext(await harness.Seed(), Scope);
        var expected = Snapshot(source, emptyWinner);
        race.Action = async () => { await harness.Reopen().InstallAsync(source, expected); };
        var result = await harness.Delivery.InstallAsync(source, Snapshot(source, !emptyWinner));
        Assert.True(expected.IsEquivalentTo(result));
        Assert.Equal(1, race.Interleavings);
        Assert.Equal(3, (await harness.Context!.RuntimePostCommitOutbox.AsNoTracking().SingleAsync()).Revision);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Source_race_with_renewal_or_takeover_revalidates_fence(bool takeover)
    {
        var race = new InterleavingSave();
        await using var harness = await Harness.Create(true, race);
        var claim = await harness.Seed();
        var source = new PostCommitDeliveryContext(claim, Scope);
        race.Action = async () =>
        {
            var other = (IRuntimePostCommitOutboxClaimStore)harness.Reopen();
            if (takeover)
                Assert.Single(await other.ClaimAsync(new("other", Now.AddMinutes(2), TimeSpan.FromMinutes(1), 1)));
            else
                Assert.NotNull(await other.RenewClaimAsync(claim, Now.AddSeconds(1), TimeSpan.FromMinutes(2)));
        };
        if (takeover)
        {
            await Assert.ThrowsAsync<RuntimePostCommitOutboxStaleClaimException>(() =>
                harness.Delivery.InstallAsync(source, Snapshot(source)).AsTask());
            Assert.Null((await harness.Lookup.FindAsync(claim.OutboxItemId))!.RecipientSnapshot);
        }
        else
        {
            var result = await harness.Delivery.InstallAsync(source, Snapshot(source));
            Assert.Equal(2, result.Resumes.Count);
            Assert.Equal(Now.AddSeconds(1).AddMinutes(2), (await harness.Lookup.FindAsync(claim.OutboxItemId))!.DeliveryVisibleAfter);
        }
    }

    [Theory]
    [InlineData("recipientSnapshot", "null", "2.0.0")]
    [InlineData("recipientSnapshot", "null", "1.0.0")]
    [InlineData("RecipientSnapshot", "null", "1.0.0")]
    [InlineData("stimulusDeliveryProtocolVersion", "1", "1.0.0")]
    [InlineData("StimulusDeliveryProtocolVersion", "1", "1.0.0")]
    [InlineData("stimulusDeliveryProtocolVersion", "2", "2.0.0")]
    public async Task Corrupt_or_forged_legacy_wire_cannot_become_absence(string field, string value, string version)
    {
        await using var harness = await Harness.Create(true);
        await harness.Seed(protocol: false);
        var row = await harness.Context!.RuntimePostCommitOutbox.SingleAsync();
        var json = JsonNode.Parse(row.ContentJson)!.AsObject(); json[field] = JsonNode.Parse(value);
        row.ContentJson = json.ToJsonString(); row.SchemaVersion = version;
        await harness.Context.SaveChangesAsync(); harness.Context.ChangeTracker.Clear();
        await Assert.ThrowsAsync<InvalidDataException>(() => harness.Lookup.FindAsync("commit:send").AsTask());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Legacy_stamp_cannot_hide_complete_new_protocol(bool selected)
    {
        await using var harness = await Harness.Create(true);
        var source = new PostCommitDeliveryContext(await harness.Seed(), Scope);
        if (selected) await harness.Delivery.InstallAsync(source, Snapshot(source));
        var row = await harness.Context!.RuntimePostCommitOutbox.SingleAsync();
        row.SchemaVersion = "1.0.0"; await harness.Context.SaveChangesAsync(); harness.Context.ChangeTracker.Clear();
        await Assert.ThrowsAsync<InvalidDataException>(() => harness.Lookup.FindAsync(source.SourceOutboxItemId).AsTask());
    }

    [Fact]
    public async Task Declared_v1_upcast_preserves_exact_absence_and_never_invents_recipients()
    {
        await using var harness = await Harness.Create(true);
        await harness.Seed(protocol: false);
        var row = await harness.Context!.RuntimePostCommitOutbox.SingleAsync();
        var original = row.ContentJson; row.SchemaVersion = "1.0.0";
        await harness.Context.SaveChangesAsync(); harness.Context.ChangeTracker.Clear();
        var read = (await harness.Lookup.FindAsync("commit:send"))!;
        Assert.Null(read.RecipientSnapshot); Assert.Null(read.StimulusDeliveryProtocolVersion);
        var cast = RuntimePostCommitOutboxEfModule.Chain.Upcast<RuntimePostCommitOutboxEntity>("1.0.0", ("ContentJson", original));
        Assert.Equal(original, cast["ContentJson"]);
        Assert.True(RuntimePostCommitOutboxEfModule.Chain.IsReadable("1.0.0"));
        Assert.True(RuntimePostCommitOutboxEfModule.Chain.IsReadable("2.0.0"));
        Assert.False(RuntimePostCommitOutboxEfModule.Chain.IsReadable("future"));
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task Unavailable_dependency_or_unrequested_cancellation_keeps_original_cause(bool ef, bool cancelled)
    {
        await using var harness = await Harness.Create(ef);
        var source = new PostCommitDeliveryContext(await harness.Seed(), Scope);
        Exception cause = cancelled ? new OperationCanceledException("provider token") : new IOException("provider unavailable");
        var access = new FailedAccess(cause);
        IStimulusDeliveryStore unavailable = ef ? new EfRuntimePostCommitOutboxStore(harness.Context!, access) :
            new InMemoryRuntimeCheckpointCommitStore(persistenceAccessContextAccessor: access);
        var exception = await Assert.ThrowsAsync<StimulusDeliveryBoundaryException>(() =>
            unavailable.InstallAsync(source, Snapshot(source)).AsTask());
        Assert.True(exception.IsUnavailable); Assert.Same(cause, exception.InnerException);
        Assert.DoesNotContain(cause.Message, exception.Message);
        Assert.Null((await harness.Lookup.FindAsync(source.SourceOutboxItemId))!.RecipientSnapshot);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Provider_rejects_forged_source_scope_before_pending_acceptance(bool ef)
    {
        await using var harness = await Harness.Create(ef);
        var original = Pending("forged", true);
        var forged = new RuntimePostCommitOutboxItem(original.OutboxItemId, original.Intent, original.Status,
            original.RecordedAt, original.AvailableAt, original.RetryPolicy, 0, null, null, null, null, null, 0, null,
            1, new("foreign"), null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.SavePending(forged).AsTask());
        Assert.Null(await harness.Lookup.FindAsync(forged.OutboxItemId));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Atomic_checkpoint_refuses_forged_source_scope_before_publisher_or_marker_effects(bool ef)
    {
        await using var harness = await Harness.Create(ef);
        await harness.EnsureMaterial();
        var item = Pending("forged", true);
        var forged = new RuntimePostCommitOutboxItem(item.OutboxItemId, item.Intent, item.Status, item.RecordedAt,
            item.AvailableAt, item.RetryPolicy, 0, null, null, null, null, null, 0, null, 1, new("foreign"), null);
        RuntimeCheckpointCommit Envelope(RuntimePostCommitOutboxItem candidate) => new("forged-commit", new("checkpoint", "EmptyCheckpoint", "publisher", Now, [],
            new Dictionary<string, string>()), new(new("publisher", RuntimeStateChangeOperation.Upsert, Publisher,
            new Dictionary<string, string>()), null, [], [], [], [], [],
            postCommitOutbox: [new(candidate.OutboxItemId, RuntimeStateChangeOperation.Upsert, candidate, new Dictionary<string, string>())]),
            [], new Dictionary<string, string>());
        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Checkpoints.CommitAsync(Envelope(forged),
            new(RuntimeCheckpointPersistenceMode.Immediate)).AsTask());
        Assert.Null(await harness.Lookup.FindAsync(forged.OutboxItemId));
        Assert.Null(await harness.FindPublisher());
        if (harness.Context is not null) Assert.Empty(await harness.Context.RuntimeCheckpointCommits.AsNoTracking().ToArrayAsync());
        else Assert.Empty(((InMemoryRuntimeCheckpointCommitStore)harness.Outbox).ListCommits());
        // The identical participant envelope succeeds when only its committed scope is corrected. This prevents a
        // missing fixture dependency from falsely qualifying the scope refusal or its no-effects claim.
        await using var positive = await Harness.Create(ef);
        await positive.EnsureMaterial();
        await positive.Checkpoints.CommitAsync(Envelope(item), new(RuntimeCheckpointPersistenceMode.Immediate));
        Assert.NotNull(await positive.FindPublisher());
        Assert.Equal(Scope, (await positive.Lookup.FindAsync(item.OutboxItemId))!.StimulusDeliverySourceScope);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Committed_scope_protects_even_null_tenant_legacy_publisher(bool ef)
    {
        await using var harness = await Harness.Create(ef);
        var publisher = Publisher with { TenantId = null, Partition = null, RunKind = WorkflowRunKind.Unknown };
        var claim = await harness.Seed(publisher: publisher);
        var foreignScope = new PersistenceScope("foreign");
        var forged = new PostCommitDeliveryContext(claim, foreignScope);
        var refused = await Assert.ThrowsAsync<StimulusDeliveryBoundaryException>(() =>
            harness.Reopen(foreignScope).InstallAsync(forged, Snapshot(forged, publisher: publisher)).AsTask());
        Assert.False(refused.IsUnavailable);
        Assert.Null((await harness.Lookup.FindAsync(claim.OutboxItemId))!.RecipientSnapshot);
        var rightful = new PostCommitDeliveryContext(claim, Scope);
        Assert.NotNull(await harness.Delivery.InstallAsync(rightful, Snapshot(rightful, publisher: publisher)));
    }

    [Fact]
    public async Task Export_closed_provider_witnesses_for_original_reader_refusal()
    {
        await using var harness = await Harness.Create(true);
        var source = new PostCommitDeliveryContext(await harness.Seed(), Scope);
        Assert.Equal("2.0.0", (await harness.Context!.RuntimePostCommitOutbox.AsNoTracking().SingleAsync()).SchemaVersion);
        Assert.Null((await harness.Lookup.FindAsync(source.SourceOutboxItemId))!.RecipientSnapshot);
        await harness.Export("before-selection");
        await harness.Delivery.InstallAsync(source, Snapshot(source));
        await harness.Export("after-selection");
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Port_registration_shares_provider_and_rejects_competing_ownership(bool ef)
    {
        var services = new ServiceCollection(); services.AddWorkflowRuntime();
        if (ef)
        {
            services.AddRuntimeOperationalStateEntityFrameworkCore(new() { Provider = "Sqlite", ConnectionString = "Data Source=:memory:" });
            services.AddRuntimeWorkflowDispatchEntityFrameworkCore();
            services.AddRuntimePostCommitOutboxEntityFrameworkCore();
            services.AddRuntimePostCommitOutboxEntityFrameworkCore();
        }
        using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope();
        var port = scope.ServiceProvider.GetRequiredService<IStimulusDeliveryStore>();
        Assert.Same(scope.ServiceProvider.GetRequiredService<IRuntimePostCommitOutboxClaimStore>(), port);
        if (!ef)
        {
            services.AddRuntimeOperationalStateEntityFrameworkCore(new() { Provider = "Sqlite", ConnectionString = "Data Source=:memory:" });
            services.AddRuntimeWorkflowDispatchEntityFrameworkCore();
        }
        services.AddScoped<IStimulusDeliveryStore>(_ => throw new InvalidOperationException("custom"));
        var before = services.ToArray();
        Assert.Throws<InvalidOperationException>(() => services.AddRuntimePostCommitOutboxEntityFrameworkCore());
        Assert.Equal(before, services);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Required_source_processor_passes_real_provider_context_and_preserves_handler_installed_cohort(bool ef)
    {
        await using var harness = await Harness.Create(ef);
        await harness.SeedPending();
        var handler = new ContextHandler(async source => { await harness.Delivery.InstallAsync(source, Snapshot(source)); });
        using var services = new ServiceCollection().AddSingleton(handler).BuildServiceProvider();
        var processor = ContextProcessor(harness, services);
        Assert.Equal(1, (await processor.ProcessAsync(new(10))).DeliveredCount);
        var context = Assert.Single(handler.Contexts);
        Assert.Equal("commit:send", context.SourceOutboxItemId); Assert.Equal(Scope, context.Scope);
        var completed = (await harness.Lookup.FindAsync(context.SourceOutboxItemId))!;
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, completed.Status);
        Assert.True(Snapshot(context).IsEquivalentTo(completed.RecipientSnapshot));
    }

    [Fact]
    public async Task Actual_EF_uncommitted_source_never_invokes_handler_and_committed_retry_succeeds()
    {
        await using var harness = await Harness.Create(true);
        await harness.EnsureMaterial();
        var handler = new ContextHandler(_ => ValueTask.CompletedTask);
        using var services = new ServiceCollection().AddSingleton(handler).BuildServiceProvider();
        await using (var transaction = await harness.Context!.Database.BeginTransactionAsync())
        {
            await harness.SeedPending();
            var refusal = await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(() =>
                ContextProcessor(harness, services).ProcessAsync(new(10)).AsTask());
            Assert.IsType<InvalidOperationException>(refusal.InnerException);
            Assert.Empty(handler.Contexts);
            await transaction.RollbackAsync();
        }
        harness.Context!.ChangeTracker.Clear();
        Assert.Null(await harness.Lookup.FindAsync("commit:send"));
        Assert.Null(await harness.FindPublisher());
        await harness.SeedPending();
        Assert.Equal(1, (await ContextProcessor(harness, services).ProcessAsync(new(10))).DeliveredCount);
        Assert.Single(handler.Contexts);
    }

    [Fact]
    public async Task Actual_EF_ambient_transaction_cannot_authorize_source_or_install_recipients()
    {
        await using var harness = await Harness.Create(true);
        var source = new PostCommitDeliveryContext(await harness.Seed(), Scope);
        using (var ambient = new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Lookup.FindDurableAsync(source.SourceOutboxItemId).AsTask());
            var refusal = await Assert.ThrowsAsync<StimulusDeliveryBoundaryException>(() =>
                harness.Delivery.InstallAsync(source, Snapshot(source)).AsTask());
            Assert.False(refusal.IsUnavailable); Assert.IsType<InvalidOperationException>(refusal.InnerException);
        }
        Assert.Null((await harness.Lookup.FindAsync(source.SourceOutboxItemId))!.RecipientSnapshot);
        Assert.NotNull(await harness.Lookup.FindDurableAsync(source.SourceOutboxItemId));
        Assert.NotNull(await harness.Delivery.InstallAsync(source, Snapshot(source)));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Actual_EF_source_is_claimed_only_after_real_coalescing_flush_with_canonical_identity(bool acknowledgementLost)
    {
        await using var harness = await Harness.Create(true);
        var handler = new ContextHandler(_ => ValueTask.CompletedTask);
        var registrations = new ServiceCollection(); registrations.AddWorkflowRuntime();
        registrations.AddSingleton(handler);
        registrations.AddRuntimePostCommitIntentHandler<ContextHandler>("publish", RuntimePostCommitRetryPolicy.None, true);
        registrations.AddSingleton<IPersistenceAccessContextAccessor>(new Accessor());
        registrations.Replace(ServiceDescriptor.Singleton<TimeProvider>(harness.Clock));
        // Finite coalescing/provider seam: source/checkpoint contracts share the real EF context; this fixture has no
        // scheduler work and does not qualify a fully EF-backed host, queue recovery, retention or recipient effects.
        var lost = new LostFlushAcknowledgement(harness.Checkpoints, acknowledgementLost);
        registrations.Replace(ServiceDescriptor.Scoped<IRuntimeCheckpointCommitStore>(_ => lost));
        registrations.Replace(ServiceDescriptor.Scoped<IRuntimePostCommitOutboxStore>(_ => harness.Outbox));
        registrations.Replace(ServiceDescriptor.Scoped<IPostCommitOutboxLookupStore>(_ => harness.Lookup));
        registrations.Replace(ServiceDescriptor.Scoped<IRuntimePostCommitOutboxClaimStore>(_ => harness.Claims));
        registrations.Replace(ServiceDescriptor.Scoped<IRuntimePostCommitOutboxClaimCompletionStore>(_ => harness.Completion));
        registrations.Replace(ServiceDescriptor.Scoped<IStimulusDeliveryStore>(_ => harness.Delivery));
        registrations.AddPersistenceModes();
        using var provider = registrations.BuildServiceProvider(); using var scope = provider.CreateScope();
        var services = scope.ServiceProvider;
        await using var drain = services.GetRequiredService<IRuntimeCoalescingDrainScopeFactory>()
            .Begin("publisher", new(PersistenceMode.Balanced, RunHasRecord: false));
        var envelope = new RuntimeCheckpointCommit("overlay", new("checkpoint", RuntimeCheckpointNames.ActivityCompleted,
            "publisher", Now, ["activity"], new Dictionary<string, string>()),
            new(null, null, [], [], [], [], []), [], new Dictionary<string, string>());
        var original = RuntimePostCommitOutboxItems.CreatePending(envelope, Pending("ignored", false).Intent,
            [new("publish", typeof(ContextHandler), RuntimePostCommitRetryPolicy.None, true)], Scope);
        envelope = envelope with { StateChanges = envelope.StateChanges.WithPostCommitOutbox(
            [new(original.OutboxItemId, RuntimeStateChangeOperation.Upsert, original, new Dictionary<string, string>())]) };
        await services.GetRequiredService<IRuntimeCheckpointCommitStore>().CommitAsync(envelope, new(RuntimeCheckpointPersistenceMode.Deferred));
        Assert.Null(await harness.Lookup.FindDurableAsync(original.OutboxItemId));
        var processor = services.GetRequiredService<IRuntimePostCommitOutboxProcessor>();
        Assert.Empty((await processor.ProcessAsync(new(10, "publisher", "publish"))).Items);
        Assert.Empty(handler.Contexts);
        if (acknowledgementLost)
        {
            await Assert.ThrowsAsync<IOException>(() => drain.FlushAtQuiescenceAsync().AsTask());
            Assert.Equal(1, (await ContextProcessor(harness, services).ProcessAsync(new(10))).DeliveredCount);
            harness.Clock.Advance(TimeSpan.FromSeconds(2));
        }
        await drain.FlushAtQuiescenceAsync();
        Assert.Equal(acknowledgementLost ? 2 : 1, lost.FlushIds.Count);
        Assert.Single(lost.FlushIds.Distinct(StringComparer.Ordinal));
        var row = await harness.Context!.RuntimePostCommitOutbox.AsNoTracking().SingleAsync();
        Assert.Equal("2.0.0", row.SchemaVersion);
        var canonical = EfRelationalIdentity.Decode(row.OutboxItemId);
        Assert.NotEqual(original.OutboxItemId, canonical);
        Assert.Equal(acknowledgementLost ? 0 : 1, (await processor.ProcessAsync(new(10, "publisher", "publish"))).DeliveredCount);
        Assert.Equal(canonical, Assert.Single(handler.Contexts).SourceOutboxItemId);
        Assert.Null(await harness.Lookup.FindDurableAsync(original.OutboxItemId));
        Assert.Equal(Scope, (await harness.Lookup.FindDurableAsync(canonical))!.StimulusDeliverySourceScope);
        Assert.Empty((await processor.ProcessAsync(new(10, "publisher", "publish"))).Items);
    }

    [Theory]
    [InlineData(false, RuntimePostCommitOutboxStatus.Delivering)] [InlineData(true, RuntimePostCommitOutboxStatus.Delivering)]
    [InlineData(false, RuntimePostCommitOutboxStatus.FailedRetryable)] [InlineData(true, RuntimePostCommitOutboxStatus.FailedRetryable)]
    [InlineData(false, RuntimePostCommitOutboxStatus.FailedFinal)] [InlineData(true, RuntimePostCommitOutboxStatus.FailedFinal)]
    [InlineData(false, RuntimePostCommitOutboxStatus.Cancelled)] [InlineData(true, RuntimePostCommitOutboxStatus.Cancelled)]
    public async Task Owed_cohort_retains_pins_publisher_and_exact_recipient_dedup_in_every_unresolved_status(
        bool ef, RuntimePostCommitOutboxStatus status)
    {
        await using var harness = await Harness.Create(ef);
        var source = new PostCommitDeliveryContext(await harness.Seed(), Scope);
        await harness.Delivery.InstallAsync(source, Snapshot(source));
        if (status != RuntimePostCommitOutboxStatus.Delivering)
            await harness.Claims.RecordDeliveryResultAsync(source.Claim, new(source.SourceOutboxItemId, status, Now, status == RuntimePostCommitOutboxStatus.Cancelled ? null : "constructed failure"));
        var startId = KeyedWorkflowStartIdentity.For("key", Pin.ArtifactId).WorkflowExecutionId;
        await harness.Executions.SaveAsync(Publisher with { WorkflowExecutionId = startId, Status = WorkflowExecutionStatus.Completed });
        await harness.Executions.SaveAsync(Publisher with { WorkflowExecutionId = "waiter", Status = WorkflowExecutionStatus.Completed });
        Assert.Equal(new[] { Pin.ArtifactId }, await harness.Reopen().ListPinnedExecutableArtifactIdsAsync());
        foreach (var id in new[] { "publisher", "waiter", startId })
        {
            Assert.False(await harness.Executions.DeleteAsync(id));
            Assert.NotNull(await harness.Executions.FindAsync(id));
            Assert.False(await harness.Executions.IsDeletionTombstonedAsync(id));
        }
        Assert.True((await harness.Lookup.FindAsync(source.SourceOutboxItemId))!.RecipientSnapshot!.IsEquivalentTo(Snapshot(source)));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Delivered_cohort_outlives_publisher_and_absent_start_until_target_sticky_retirement(bool ef)
    {
        await using var harness = await Harness.Create(ef);
        var source = new PostCommitDeliveryContext(await harness.Seed(), Scope);
        await harness.Delivery.InstallAsync(source, Snapshot(source));
        await harness.Claims.RecordDeliveryResultAsync(source.Claim, new(source.SourceOutboxItemId, RuntimePostCommitOutboxStatus.Delivered, Now));
        Assert.True(await harness.Executions.DeleteAsync("publisher"));
        var target = KeyedWorkflowStartIdentity.For("key", Pin.ArtifactId).WorkflowExecutionId;
        Assert.Null(await harness.Executions.FindAsync(target));
        Assert.False(await harness.Executions.IsDeletionTombstonedAsync(target));
        Assert.Equal(new[] { Pin.ArtifactId }, await harness.Reopen().ListPinnedExecutableArtifactIdsAsync());
        Assert.Empty(await harness.Reopen(new("foreign")).ListPinnedExecutableArtifactIdsAsync());
        await harness.Executions.SaveAsync(Publisher with { WorkflowExecutionId = target, Status = WorkflowExecutionStatus.Completed });
        Assert.Equal(new[] { Pin.ArtifactId }, await harness.Reopen().ListPinnedExecutableArtifactIdsAsync());
        Assert.True(await harness.Executions.DeleteAsync(target));
        Assert.True(await harness.Executions.IsDeletionTombstonedAsync(target));
        Assert.Empty(await harness.Reopen().ListPinnedExecutableArtifactIdsAsync());
        Assert.NotNull((await harness.Lookup.FindAsync(source.SourceOutboxItemId))!.RecipientSnapshot);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Declared_source_retains_publisher_before_first_cohort_but_empty_delivered_source_releases_it(bool ef)
    {
        await using var harness = await Harness.Create(ef);
        await harness.SeedPending();
        Assert.False(await harness.Executions.DeleteAsync("publisher")); // Pending, before any claim.
        var source = new PostCommitDeliveryContext(Assert.Single(await harness.Claims.ClaimAsync(new("owner", Now, TimeSpan.FromMinutes(1), 1))), Scope);
        Assert.False(await harness.Executions.DeleteAsync("publisher"));
        Assert.False(await harness.Executions.IsDeletionTombstonedAsync("publisher"));
        await harness.Delivery.InstallAsync(source, Snapshot(source, true));
        Assert.Empty(await harness.Delivery.ListPinnedExecutableArtifactIdsAsync());
        Assert.False(await harness.Executions.DeleteAsync("publisher"));
        await harness.Claims.RecordDeliveryResultAsync(source.Claim, new(source.SourceOutboxItemId, RuntimePostCommitOutboxStatus.Delivered, Now));
        Assert.True(await harness.Executions.DeleteAsync("publisher"));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Actual_GC_keeps_cohort_pin_dependency_and_retired_reference_through_absent_target_handoff(bool ef)
    {
        await using var harness = await Harness.Create(ef);
        IWorkflowExecutableStore artifacts = harness.Artifacts;
        IWorkflowExecutableSourceReferenceStore references = harness.References;
        var dependency = Material(Pin with { ArtifactId = "dependency", ArtifactHash = "dependency-hash" });
        await artifacts.SaveBatchAsync([dependency, Material(Pin, dependency)]);
        var source = new PostCommitDeliveryContext(await harness.Seed(), Scope);
        await harness.Delivery.InstallAsync(source, Snapshot(source));
        Assert.True(await references.RetireAsync("selected-reference", Now));
        var gc = new WorkflowExecutableReferenceGarbageCollector(artifacts, references, new InMemoryExecutableActivityTemplateStore(),
            harness.Executions, null, Options.Create(new WorkflowExecutableGarbageCollectionOptions { ArtifactCreationGracePeriod = TimeSpan.Zero }),
            harness.Clock, NullLogger<WorkflowExecutableReferenceGarbageCollector>.Instance, harness.Delivery);
        Assert.True(gc.SupportsStimulusDeliveryRetention);
        Assert.False((await gc.SweepAsync()).DidWork);
        await harness.Claims.RecordDeliveryResultAsync(source.Claim, new(source.SourceOutboxItemId, RuntimePostCommitOutboxStatus.Delivered, Now));
        Assert.True(await harness.Executions.DeleteAsync("publisher"));
        Assert.False((await gc.SweepAsync()).DidWork); // No target state yet; absence cannot drop the handoff root.
        Assert.NotNull(await artifacts.FindAsync(Pin.ArtifactId)); Assert.NotNull(await artifacts.FindAsync("dependency"));
        Assert.NotNull(await references.FindAsync("selected-reference"));
        var target = KeyedWorkflowStartIdentity.For("key", Pin.ArtifactId).WorkflowExecutionId;
        await harness.Executions.SaveAsync(Publisher with { WorkflowExecutionId = target, Status = WorkflowExecutionStatus.Completed });
        Assert.True(await harness.Executions.DeleteAsync(target));
        var released = await gc.SweepAsync();
        Assert.Equal(1, released.DeletedReferenceCount); Assert.Equal(2, released.DeletedArtifactCount);
        Assert.Null(await references.FindAsync("selected-reference")); Assert.Null(await artifacts.FindAsync("dependency"));
    }

    [Fact]
    public async Task EF_retention_reads_all_source_pages_including_a_later_selected_cohort()
    {
        await using var harness = await Harness.Create(true);
        var source = new PostCommitDeliveryContext(await harness.Seed(), Scope);
        await harness.Delivery.InstallAsync(source, Snapshot(source));
        var context = harness.Context!;
        for (var index = 0; index <= RuntimeStorePageRequest.MaximumLimit; index++)
        {
            var item = Pending($"a-padding-{index:D5}", false);
            await harness.SavePending(item);
        }
        await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        Assert.Equal(new[] { Pin.ArtifactId }, await harness.Reopen().ListPinnedExecutableArtifactIdsAsync());
        Assert.False(await harness.Executions.DeleteAsync("publisher"));
    }

    private static WorkflowExecutable Material(WorkflowExecutableIdentity identity, params WorkflowExecutable[] dependencies) =>
        new(identity, new ExecutableNode("node", "authored", "test/activity", "1.0.0",
            new RuntimeActivityDescriptor("test", RuntimeActivityDescriptor.InitialSchemaVersion, JsonSerializer.SerializeToElement(new { })),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>()),
            new Dictionary<string, WorkflowExecutableResumeTarget>(), Now, new Dictionary<string, string>(), inputContract: null,
            dependencies: dependencies.Select(dependency => new WorkflowExecutableDependency(dependency.Identity.ArtifactId, dependency.Identity.ArtifactHash, ["node"])).ToArray(),
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference);

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public async Task Retention_read_preserves_provider_failure_and_actual_scope_without_fabricating_a_source(bool ef, bool unrequestedCancellation)
    {
        Exception cause = unrequestedCancellation ? new OperationCanceledException("provider cancellation") : new IOException("provider read failed");
        var fault = new RetentionReadFailure(cause);
        await using var harness = await Harness.Create(ef, fault);
        var source = new PostCommitDeliveryContext(await harness.Seed(), Scope);
        await harness.Delivery.InstallAsync(source, Snapshot(source));
        IStimulusDeliveryStore failed;
        if (ef) { fault.Armed = true; failed = harness.Delivery; }
        else failed = new InMemoryRuntimeCheckpointCommitStore(persistenceAccessContextAccessor: new FailedAccess(cause));
        var failure = await Assert.ThrowsAsync<StimulusDeliveryRetentionException>(() => failed.ListPinnedExecutableArtifactIdsAsync().AsTask());
        Assert.Same(cause, failure.InnerException); Assert.Equal(ef ? Scope : null, failure.Scope);
        fault.Armed = false;
        Assert.Equal(new[] { Pin.ArtifactId }, await harness.Delivery.ListPinnedExecutableArtifactIdsAsync());
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Delivery.ListPinnedExecutableArtifactIdsAsync(cancellation.Token).AsTask());
    }

    private sealed class RetentionReadFailure(Exception cause) : DbCommandInterceptor
    {
        public bool Armed { get; set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Armed) throw cause;
            return ValueTask.FromResult(result);
        }
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public async Task GC_reference_final_recheck_and_native_leases_order_competing_cohort_root_creation(bool ef, bool acceptanceFirst)
    {
        await using var harness = await Harness.Create(ef);
        IWorkflowExecutableStore artifacts = harness.Artifacts;
        IWorkflowExecutableSourceReferenceStore references = harness.References;
        var publisher = Publisher with { PinnedExecutable = Pin with { ArtifactId = "publisher-artifact", ArtifactHash = "publisher-hash" } };
        var dependency = Material(Pin with { ArtifactId = "dependency", ArtifactHash = "dependency-hash" });
        await artifacts.SaveBatchAsync([Material(publisher.PinnedExecutable), dependency, Material(Pin, dependency)]);
        var reference = new WorkflowExecutableSourceReference("selected", Pin.ArtifactId, "WorkflowDefinitionVersion", "version", "1",
            Pin.DefinitionId, Pin.DefinitionVersionId, Pin.ArtifactVersion, Now, Now, WorkflowExecutableReferenceScope.Published);
        await references.SaveAsync(reference); await references.RetireAsync(reference.SourceReferenceId, Now);
        var source = new PostCommitDeliveryContext(await harness.Seed(publisher: publisher), Scope);
        var options = Options.Create(new WorkflowExecutableGarbageCollectionOptions { ArtifactCreationGracePeriod = TimeSpan.Zero });
        var installed = false;
        async ValueTask Accept()
        {
            await harness.Delivery.InstallAsync(source, Snapshot(source, publisher: publisher)); installed = true;
        }
        var interleaving = new AtReferenceGuard(artifacts, acceptanceFirst, async () =>
        {
            if (acceptanceFirst) await Accept();
            else
            {
                var refused = await Assert.ThrowsAsync<StimulusDeliveryBoundaryException>(() => Accept().AsTask());
                Assert.True(refused.IsUnavailable); Assert.IsType<WorkflowExecutableRootWriteLeaseUnavailableException>(refused.InnerException);
            }
            Assert.True(await references.RetireAsync("selected-reference", Now));
        });
        var gc = new WorkflowExecutableReferenceGarbageCollector(interleaving, references, new InMemoryExecutableActivityTemplateStore(),
            harness.Executions, null, options, harness.Clock, NullLogger<WorkflowExecutableReferenceGarbageCollector>.Instance, harness.Delivery);
        await gc.SweepAsync(); await gc.SweepAsync();
        Assert.Equal(1, interleaving.Interleavings); Assert.Equal(acceptanceFirst, installed);
        Assert.Equal(acceptanceFirst, await references.FindAsync("selected") is not null);
        Assert.Equal(acceptanceFirst, await artifacts.FindAsync(Pin.ArtifactId) is not null);
        Assert.Equal(acceptanceFirst, await artifacts.FindAsync("dependency") is not null);
        Assert.Equal(acceptanceFirst, (await harness.Lookup.FindAsync(source.SourceOutboxItemId))!.RecipientSnapshot is not null);
    }

    private sealed class AtReferenceGuard(IWorkflowExecutableStore inner, bool before, Func<ValueTask> interleave) : WorkflowExecutableStoreDecorator(inner)
    {
        public int Interleavings { get; private set; }
        public override async ValueTask<WorkflowExecutableDeletionGuard?> TryBeginDeletionAsync(string artifactId, string operationId,
            DateTimeOffset expiresAt, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            if (artifactId != Pin.ArtifactId || Interleavings != 0) return await base.TryBeginDeletionAsync(artifactId, operationId, expiresAt, now, cancellationToken);
            Interleavings++;
            if (before) await interleave();
            var guard = await base.TryBeginDeletionAsync(artifactId, operationId, expiresAt, now, cancellationToken);
            if (!before) { Assert.NotNull(guard); await interleave(); }
            return guard;
        }
    }

    private sealed class LostFlushAcknowledgement(IRuntimeCheckpointCommitStore inner, bool armed) : IRuntimeCheckpointCommitStore
    {
        public List<string> FlushIds { get; } = [];
        public async ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(RuntimeCheckpointCommit commit,
            RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken = default)
        {
            var flush = commit.Checkpoint.Metadata.ContainsKey(RuntimeCoalescingMetadataKeys.CoalescedFlush);
            if (flush) FlushIds.Add(commit.CommitId);
            var result = await inner.CommitAsync(commit, decision, cancellationToken);
            if (flush && armed) { armed = false; throw new IOException("Constructed acknowledgement loss after actual EF commit."); }
            return result;
        }
    }

    private static RuntimePostCommitOutboxProcessor ContextProcessor(Harness harness, IServiceProvider services) =>
        new(harness.Outbox, new RuntimePostCommitIntentDispatcher([new("publish", typeof(ContextHandler), RuntimePostCommitRetryPolicy.None, true)], services),
            harness.Clock, DefaultRuntimeFaultCapturePolicy.CreateDefault(), null, [], null, null, null, new Accessor(),
            [new("publish", typeof(ContextHandler), RuntimePostCommitRetryPolicy.None, true)]);
    private sealed class ContextHandler(Func<PostCommitDeliveryContext, ValueTask> deliver) : IRuntimePostCommitDurableSourceIntentHandler
    {
        public List<PostCommitDeliveryContext> Contexts { get; } = [];
        public async ValueTask HandleAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken = default)
        { Contexts.Add(source); await deliver(source); }
    }

    private static StimulusDeliverySnapshot Snapshot(PostCommitDeliveryContext source, bool empty = false, WorkflowExecutionState? publisher = null) =>
        new(1, source.SourceOutboxItemId, source.IntentFingerprint, StimulusDeliveryScope.From(publisher ?? Publisher, source.Scope),
            Now, "event", "named", empty ? [] : [Start("binding-a"), Start("binding-b")], empty ? [] : [new("waiter", "bookmark-1", "activity", "node", "resume", "event", "named", Now, null),
                new("waiter", "bookmark-2", "activity", "node", "resume", "event", "named", Now, null)], StimulusDeliverySnapshot.CurrentRetentionAcceptanceVersion);

    private static WorkflowExecutableSourceProvenance SelectedSource() => new("selected-reference", "definition", "selected-definition", "2",
        "selected-definition", "selected-publication-version", "selected-label", "activation", "slot");

    private static StimulusStartRecipient Start(string id) => new(new(id, "artifact", "definition", "1.0.0", "hash",
        "start", "event", "named", null, new Dictionary<string, string> { ["key\ud800"] = "value\udc00" }, Now,
        "activation", "slot"), Pin, SelectedSource());

    private static RuntimePostCommitOutboxItem Pending(string commit, bool protocol, string payload = "original") => new(
        commit + ":send", new("send", "publisher", "publish", Now, "activity", "key", JsonSerializer.SerializeToElement(payload)),
        RuntimePostCommitOutboxStatus.Pending, Now, Now, new(4, TimeSpan.FromSeconds(1)), 0,
        null, null, null, null, null, 0, null, protocol ? 1 : null, protocol ? Scope : null, null);

    private sealed class Accessor(PersistenceScope? scope = null) : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(scope ?? Scope); }
    // Only makes unrelated executable-lease infrastructure available to finite atomic participant controls;
    // these fixtures do not qualify real executable retention/GC or host admission.
    private sealed class ParticipantLease : IWorkflowExecutableRootWriteLeaseManager
    {
        public ValueTask ExecuteAsync(string artifactId, string leaseId, Func<CancellationToken, ValueTask> write,
            CancellationToken cancellationToken = default) => write(cancellationToken);
    }
    private sealed class FailedAccess(Exception cause) : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current => throw cause; }
    private sealed class Codec : IRuntimeRecoveryContinuationCodec
    {
        public string Encode(string purpose, ReadOnlySpan<byte> payload) => Convert.ToBase64String(payload);
        public byte[] Decode(string purpose, string token) => Convert.FromBase64String(token);
    }
    // SQLite serializes writes at transaction entry. Interleave after actual capture/leases but before that owned
    // transaction, rather than accidentally firing on a lease-row SaveChanges or attempting a nested peer transaction.
    private sealed class InterleavingSave : DbTransactionInterceptor
    {
        public Func<Task>? Action { get; set; }
        public int Interleavings { get; private set; }
        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection,
            TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        {
            if (Action is { } action) { Action = null; Interleavings++; await action(); }
            return result;
        }
    }
    private sealed class ObservedRootManager(IWorkflowExecutableRootWriteLeaseManager inner, Harness owner) : IWorkflowExecutableRootWriteLeaseManager
    {
        public ValueTask ExecuteAsync(string artifactId, string leaseId, Func<CancellationToken, ValueTask> write, CancellationToken cancellationToken = default) =>
            inner.ExecuteAsync(artifactId, leaseId, write, cancellationToken);
        public ValueTask ExecuteAsync(WorkflowExecutableIdentity root, string leaseId, Func<CancellationToken, ValueTask> write, CancellationToken cancellationToken = default) =>
            inner.ExecuteAsync(root, leaseId, write, cancellationToken);
        public ValueTask ExecuteAsync(IReadOnlyCollection<WorkflowExecutableIdentity> roots, string leaseId, Func<CancellationToken, ValueTask> write, CancellationToken cancellationToken = default) =>
            inner.ExecuteAsync(roots, leaseId, write, cancellationToken);
        public async ValueTask ExecuteFencedAsync(IReadOnlyCollection<WorkflowExecutableIdentity> roots, string leaseId,
            Func<IReadOnlyList<WorkflowExecutableRootWriteLease>, CancellationToken, ValueTask> write, CancellationToken cancellationToken = default)
        {
            await inner.ExecuteFencedAsync(roots, leaseId, async (grants, token) =>
            {
                if (owner.BeforeAcceptance is { } action) { owner.BeforeAcceptance = null; await action(); }
                await write(grants, token);
            }, cancellationToken);
            if (owner.LoseInstallAcknowledgement) { owner.LoseInstallAcknowledgement = false; throw owner.LostInstallAcknowledgement; }
        }
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly SqliteConnection? connection;
        private readonly InMemoryRuntimeCheckpointStoreState memory = new();
        private readonly InMemoryWorkflowExecutionStateStore publishers;
        private readonly InMemoryExecutionLivenessStateStore liveness = new();
        private readonly bool referenceLess;
        private readonly InMemoryWorkflowExecutableStore memoryArtifacts = new();
        private readonly InMemoryWorkflowExecutableSourceReferenceStore memoryReferences;
        private readonly List<RuntimeSqliteDbContext> contexts = [];
        public FakeTimeProvider Clock { get; } = new(Now);
        public Func<Task>? BeforeAcceptance { get; set; }
        public bool LoseInstallAcknowledgement { get; set; }
        public IOException LostInstallAcknowledgement { get; } = new("Constructed acknowledgement loss after actual native cohort commit.");
        public RuntimeSqliteDbContext? Context { get; }
        public IRuntimePostCommitOutboxStore Outbox { get; }
        public IPostCommitOutboxLookupStore Lookup => (IPostCommitOutboxLookupStore)Outbox;
        public IRuntimePostCommitOutboxClaimStore Claims => (IRuntimePostCommitOutboxClaimStore)Outbox;
        public IRuntimePostCommitOutboxClaimCompletionStore Completion => (IRuntimePostCommitOutboxClaimCompletionStore)Outbox;
        public IRuntimeCheckpointCommitStore Checkpoints => Context is null ? (IRuntimeCheckpointCommitStore)Outbox :
            new EfRuntimeCheckpointCommitStore(Context, new Accessor(), Clock, new ParticipantLease());
        public ValueTask<WorkflowExecutionState?> FindPublisher() => Context is null ? publishers.FindAsync("publisher") :
            new EfWorkflowExecutionStateStore(Context, new Accessor(), new Codec()).FindAsync("publisher");
        public IStimulusDeliveryStore Delivery => (IStimulusDeliveryStore)Outbox;
        public IWorkflowExecutionStateStore Executions => Context is null ? publishers : new EfWorkflowExecutionStateStore(Context, new Accessor(), new Codec());
        private Harness(SqliteConnection? connection, IInterceptor? interceptor, bool referenceLess)
        {
            this.connection = connection; this.referenceLess = referenceLess;
            memoryReferences = new(memoryArtifacts);
            publishers = new(memory, liveness);
            if (connection is null)
                Outbox = MemoryStore();
            else
            {
                Context = Open(interceptor);
                Outbox = EfStore(Context);
            }
        }
        public IWorkflowExecutableStore Artifacts => Context is null ? memoryArtifacts : new EfWorkflowExecutableStore(Context, new Accessor());
        public IWorkflowExecutableSourceReferenceStore References => Context is null ? memoryReferences : new EfWorkflowExecutableSourceReferenceStore(Context, new Accessor(), new Codec());
        private InMemoryRuntimeCheckpointCommitStore MemoryStore(PersistenceScope? scope = null) =>
            new(memoryArtifacts, memoryReferences, publishers, operationalStateStore: liveness, state: memory,
                persistenceAccessContextAccessor: new Accessor(scope), timeProvider: Clock,
                rootWriteLeaseManager: Observe(new WorkflowExecutableRootWriteLeaseManager(memoryArtifacts,
                    Options.Create(new WorkflowExecutableGarbageCollectionOptions()), Clock)));
        private EfRuntimePostCommitOutboxStore EfStore(RuntimeSqliteDbContext context, PersistenceScope? scope = null) =>
            new(context, new Accessor(scope), Clock, new EfWorkflowExecutableSourceReferenceStore(context, new Accessor(scope), new Codec()),
                Observe(new WorkflowExecutableRootWriteLeaseManager(new EfWorkflowExecutableStore(context, new Accessor(scope)),
                    Options.Create(new WorkflowExecutableGarbageCollectionOptions()), Clock)));
        private IWorkflowExecutableRootWriteLeaseManager Observe(IWorkflowExecutableRootWriteLeaseManager inner) => new ObservedRootManager(inner, this);
        public async Task EnsureMaterial(WorkflowExecutionState? publisher = null)
        {
            if (await Artifacts.FindAsync(Pin.ArtifactId) is null) await Artifacts.SaveAsync(Material(Pin));
            if (publisher is not null && await Artifacts.FindAsync(publisher.PinnedExecutable.ArtifactId) is null)
                await Artifacts.SaveAsync(Material(publisher.PinnedExecutable));
            if (!referenceLess && await References.FindAsync("selected-reference") is null)
                await References.SaveAsync(new("selected-reference", Pin.ArtifactId, "definition", "selected-definition", "2",
                    "selected-definition", "selected-publication-version", "selected-label", Now, Now,
                    WorkflowExecutableReferenceScope.Published, ActivationId: "activation", SlotId: "slot"));
        }
        private RuntimeSqliteDbContext Open(IInterceptor? interceptor = null)
        {
            var options = new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection!);
            if (interceptor is not null) options.AddInterceptors(interceptor);
            var context = new RuntimeSqliteDbContext(options.Options); contexts.Add(context); return context;
        }
        public IStimulusDeliveryStore Reopen(PersistenceScope? scope = null) => connection is null ? MemoryStore(scope) : EfStore(Open(), scope);
        public IStimulusDeliveryStore Unsupported(bool legacyManager) => Context is null
            ? legacyManager
                ? new InMemoryRuntimeCheckpointCommitStore(memoryArtifacts, memoryReferences, publishers, operationalStateStore: liveness,
                    state: memory, persistenceAccessContextAccessor: new Accessor(), timeProvider: Clock, rootWriteLeaseManager: new ParticipantLease())
                : new InMemoryRuntimeCheckpointCommitStore(publishers, operationalStateStore: liveness, state: memory,
                    persistenceAccessContextAccessor: new Accessor(), timeProvider: Clock)
            : legacyManager
                ? new EfRuntimePostCommitOutboxStore(Context, new Accessor(), Clock, References, new ParticipantLease())
                : new EfRuntimePostCommitOutboxStore(Context, new Accessor(), Clock);
        public static async Task<Harness> Create(bool ef, IInterceptor? interceptor = null, bool referenceLess = false)
        {
            SqliteConnection? connection = null;
            if (ef) { connection = new("Data Source=:memory:"); await connection.OpenAsync(); }
            var harness = new Harness(connection, interceptor, referenceLess);
            if (ef) await harness.Context!.Database.EnsureCreatedAsync();
            return harness;
        }
        public ValueTask SavePending(RuntimePostCommitOutboxItem item) => Outbox is EfRuntimePostCommitOutboxStore ef
            ? ef.SavePendingAsync(item) : ((InMemoryRuntimeCheckpointCommitStore)Outbox).AddPendingForTestingAsync(item);
        public ValueTask<WorkflowExecutionState> SavePublisher(WorkflowExecutionState publisher) => Context is null ?
            publishers.SaveAsync(publisher) : new EfWorkflowExecutionStateStore(Context, new Accessor(), new Codec()).SaveAsync(publisher);
        public async Task SeedPending(bool protocol = true, WorkflowExecutionState? publisher = null)
        {
            await EnsureMaterial(publisher ?? Publisher);
            if (Context is null) await publishers.SaveAsync(publisher ?? Publisher);
            else await new EfWorkflowExecutionStateStore(Context, new Accessor(), new Codec()).SaveAsync(publisher ?? Publisher);
            await SavePending(Pending("commit", protocol));
        }
        public async Task<RuntimePostCommitOutboxClaim> Seed(bool protocol = true, WorkflowExecutionState? publisher = null)
        {
            await SeedPending(protocol, publisher);
            return Assert.Single(await Claims.ClaimAsync(new("owner", Now, TimeSpan.FromMinutes(1), 1)));
        }
        public async Task Export(string name)
        {
            if (Environment.GetEnvironmentVariable("ELSA_T047_PROVIDER_WITNESS_DIR") is not { Length: > 0 } directory) return;
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, name + ".sqlite");
            if (File.Exists(path)) File.Delete(path);
            await using var copy = new SqliteConnection("Data Source=" + path); await copy.OpenAsync();
            connection!.BackupDatabase(copy);
        }
        public async ValueTask DisposeAsync()
        {
            foreach (var context in contexts) await context.DisposeAsync();
            if (connection is not null) await connection.DisposeAsync();
        }
    }
}
