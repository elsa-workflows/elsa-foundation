using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>Actual provider CAS and rewrite controls; these do not qualify recipient dispatch/host recovery.</summary>
public sealed class StimulusDeliveryStoreTests
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
    public async Task CAS_loser_reloads_actual_winner_including_empty(bool emptyWinner)
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
    public async Task CAS_race_with_renewal_or_takeover_revalidates_fence(bool takeover)
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

    private static StimulusDeliverySnapshot Snapshot(PostCommitDeliveryContext source, bool empty = false, WorkflowExecutionState? publisher = null) =>
        new(1, source.SourceOutboxItemId, source.IntentFingerprint, StimulusDeliveryScope.From(publisher ?? Publisher, source.Scope),
            Now, "event", "named", empty ? [] : [Start("binding-a"), Start("binding-b")], empty ? [] : [new("waiter", "bookmark-1", "activity", "node", "resume", "event", "named", Now, null),
                new("waiter", "bookmark-2", "activity", "node", "resume", "event", "named", Now, null)]);

    private static StimulusStartRecipient Start(string id) => new(new(id, "artifact", "definition", "1.0.0", "hash",
        "start", "event", "named", null, new Dictionary<string, string> { ["key\ud800"] = "value\udc00" }, Now,
        "activation", "slot"), Pin);

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
    private sealed class InterleavingSave : SaveChangesInterceptor
    {
        public Func<Task>? Action { get; set; }
        public int Interleavings { get; private set; }
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Action is { } action) { Action = null; Interleavings++; await action(); }
            return result;
        }
    }
    private sealed class Harness : IAsyncDisposable
    {
        private readonly SqliteConnection? connection;
        private readonly InMemoryRuntimeCheckpointStoreState memory = new();
        private readonly InMemoryWorkflowExecutionStateStore publishers;
        private readonly List<RuntimeSqliteDbContext> contexts = [];
        public FakeTimeProvider Clock { get; } = new(Now);
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
        private Harness(SqliteConnection? connection, IInterceptor? interceptor)
        {
            this.connection = connection;
            publishers = new(memory, null);
            if (connection is null)
                Outbox = MemoryStore();
            else
            {
                Context = Open(interceptor);
                Outbox = new EfRuntimePostCommitOutboxStore(Context, new Accessor(), Clock);
            }
        }
        private InMemoryRuntimeCheckpointCommitStore MemoryStore(PersistenceScope? scope = null) =>
            new(publishers, state: memory, persistenceAccessContextAccessor: new Accessor(scope), timeProvider: Clock, rootWriteLeaseManager: new ParticipantLease());
        private RuntimeSqliteDbContext Open(IInterceptor? interceptor = null)
        {
            var options = new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection!);
            if (interceptor is not null) options.AddInterceptors(interceptor);
            var context = new RuntimeSqliteDbContext(options.Options); contexts.Add(context); return context;
        }
        public IStimulusDeliveryStore Reopen(PersistenceScope? scope = null) => connection is null ? MemoryStore(scope) : new EfRuntimePostCommitOutboxStore(Open(), new Accessor(scope), Clock);
        public static async Task<Harness> Create(bool ef, IInterceptor? interceptor = null)
        {
            SqliteConnection? connection = null;
            if (ef) { connection = new("Data Source=:memory:"); await connection.OpenAsync(); }
            var harness = new Harness(connection, interceptor);
            if (ef) await harness.Context!.Database.EnsureCreatedAsync();
            return harness;
        }
        public ValueTask SavePending(RuntimePostCommitOutboxItem item) => Outbox is EfRuntimePostCommitOutboxStore ef
            ? ef.SavePendingAsync(item) : ((InMemoryRuntimeCheckpointCommitStore)Outbox).AddPendingForTestingAsync(item);
        public async Task<RuntimePostCommitOutboxClaim> Seed(bool protocol = true, WorkflowExecutionState? publisher = null)
        {
            if (Context is null) await publishers.SaveAsync(publisher ?? Publisher);
            else await new EfWorkflowExecutionStateStore(Context, new Accessor(), new Codec()).SaveAsync(publisher ?? Publisher);
            await SavePending(Pending("commit", protocol));
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
