using System.Text.Json;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;
using Xunit;

namespace Elsa.Persistence.Groundwork.Querying.Tests;

/// <summary>
/// The design lane's post-commit outbox and the sweep that drains it. A cross-target design mutation becomes
/// done at its own commit; whatever it derives elsewhere is written afterwards, and until an intent existed
/// the only driver of that second write was the caller coming back. Issue #1171.
/// </summary>
public sealed class GroundworkDesignPostCommitRedriveTests
{
    private const string Kind = "test.deferred-write";
    private const string TargetKind = "test.target-document";
    private const string IntentDocumentKind =
        GroundworkDesignAtomicWriteStorageManifest.DesignPostCommitIntentDocumentKind;

    private static readonly DateTimeOffset Now = new(2026, 8, 10, 9, 0, 0, TimeSpan.Zero);

    private readonly InMemoryDocumentStore _documents = new(GroundworkDesignAtomicWriteStorageManifest.Create());
    private readonly GroundworkDesignPostCommitOutbox _outbox;

    public GroundworkDesignPostCommitRedriveTests() => _outbox = new(_documents, _documents);

    /// <summary>
    /// The declaration in <see cref="GroundworkDesignAtomicWriteStorageManifest"/> names <c>claimableAt</c>,
    /// <c>recordedAt</c> and <c>intentId</c> as bare top-level paths, while design entities are stored nested
    /// under <c>content.entity.*</c>. Writing the intent through the entity envelope would put all three one
    /// level too deep and the claim index would match nothing — silently, because every write would still
    /// succeed and every by-id read would still work. This is the assertion that fails if that happens.
    /// </summary>
    [Fact]
    public void The_claim_index_fields_are_written_at_the_top_level()
    {
        var intent = Intent("only");

        var request = GroundworkDesignPostCommitOutbox.CreateSaveRequest(intent, Now);

        using var document = JsonDocument.Parse(request.ContentJson);
        Assert.Equal(
            GroundworkDesignPostCommitOutbox.ProjectIntentId(intent.IntentId),
            document.RootElement.GetProperty(
                GroundworkDesignAtomicWriteStorageManifest.DesignPostCommitIntentIdField).GetString());
        Assert.Equal(
            Now,
            document.RootElement.GetProperty(
                GroundworkDesignAtomicWriteStorageManifest.DesignPostCommitIntentRecordedAtField).GetDateTimeOffset());
        Assert.Equal(
            Now,
            document.RootElement.GetProperty(
                GroundworkDesignAtomicWriteStorageManifest.DesignPostCommitIntentClaimableAtField).GetDateTimeOffset());
    }

    /// <summary>
    /// The intent is what a crash after the design commit leaves behind: committed, undelivered, and with no
    /// caller coming back for it. A sweep is the other driver.
    /// </summary>
    [Fact]
    public async Task A_sweep_delivers_an_abandoned_intent_and_deletes_it()
    {
        await CommitAsync(Intent("one"));
        var deliverer = new RecordingDeliverer();

        var result = await Redrive(deliverer).SweepAsync();

        Assert.Equal(1, result.DeliveredCount);
        Assert.Equal(
            new SaveDocumentRequest(TargetKind, "target-one", "1.0.0", Payload("target-one"), ExpectedVersion: 0),
            Assert.Single(deliverer.Delivered));
        Assert.Null(await _outbox.FindAsync(Intent("one").IntentId));
    }

    /// <summary>A claim leases the intent forward, so a concurrent sweep sees no work rather than the same work.</summary>
    [Fact]
    public async Task A_claimed_intent_is_invisible_until_its_lease_expires()
    {
        await CommitAsync(Intent("leased"));
        var claim = Assert.Single(await _outbox.ClaimAsync(new("owner-a", Now, TimeSpan.FromMinutes(1), 8)));

        Assert.Empty(await _outbox.ClaimAsync(new("owner-b", Now.AddSeconds(30), TimeSpan.FromMinutes(1), 8)));

        var afterExpiry = Assert.Single(
            await _outbox.ClaimAsync(new("owner-b", Now.AddMinutes(2), TimeSpan.FromMinutes(1), 8)));
        Assert.Equal("owner-b", afterExpiry.OwnerId);
        Assert.Equal(claim.FencingToken + 1, afterExpiry.FencingToken);
    }

    /// <summary>
    /// The fence. An owner whose lease expired must not delete an intent the next owner is working on, or one
    /// slow delivery would cancel the obligation the redrive exists to keep.
    /// </summary>
    [Fact]
    public async Task An_expired_owner_cannot_complete_an_intent_another_owner_reclaimed()
    {
        await CommitAsync(Intent("contested"));
        var stale = Assert.Single(await _outbox.ClaimAsync(new("owner-a", Now, TimeSpan.FromMinutes(1), 8)));
        var current = Assert.Single(
            await _outbox.ClaimAsync(new("owner-b", Now.AddMinutes(2), TimeSpan.FromMinutes(1), 8)));

        Assert.False(await _outbox.TryCompleteAsync(stale));
        Assert.NotNull(await _outbox.FindAsync(Intent("contested").IntentId));
        Assert.True(await _outbox.TryCompleteAsync(current));
        Assert.Null(await _outbox.FindAsync(Intent("contested").IntentId));
    }

    /// <summary>Likewise for releasing: a stale owner must not reset a lease it no longer holds.</summary>
    [Fact]
    public async Task An_expired_owner_cannot_release_an_intent_another_owner_reclaimed()
    {
        await CommitAsync(Intent("contested"));
        var stale = Assert.Single(await _outbox.ClaimAsync(new("owner-a", Now, TimeSpan.FromMinutes(1), 8)));
        await _outbox.ClaimAsync(new("owner-b", Now.AddMinutes(2), TimeSpan.FromMinutes(1), 8));

        Assert.False(await _outbox.TryReleaseAsync(stale, Now, "stale owner"));
        // owner-b's lease is intact, so nothing is claimable yet.
        Assert.Empty(await _outbox.ClaimAsync(new("owner-c", Now.AddMinutes(2), TimeSpan.FromMinutes(1), 8)));
    }

    /// <summary>A failed delivery keeps the intent and defers it; the obligation outlives the attempt.</summary>
    [Fact]
    public async Task A_failed_delivery_defers_the_intent_and_a_later_sweep_delivers_it()
    {
        await CommitAsync(Intent("flaky"));
        var deliverer = new RecordingDeliverer { FailNextAttempts = 1 };
        var clock = new AdvanceableTimeProvider(Now);
        var redrive = new GroundworkDesignPostCommitRedrive(_outbox, [deliverer], clock);

        var first = await redrive.SweepAsync();

        Assert.Equal(1, first.DeferredCount);
        Assert.NotNull(await _outbox.FindAsync(Intent("flaky").IntentId));
        // Still inside the backoff: the intent is real, but not yet due.
        Assert.Empty((await redrive.SweepAsync()).Intents);

        clock.Now = Now.AddMinutes(1);
        Assert.Equal(1, (await redrive.SweepAsync()).DeliveredCount);
        Assert.Null(await _outbox.FindAsync(Intent("flaky").IntentId));
    }

    /// <summary>
    /// A host that has not composed the owning feature must not lose the obligation. The intent waits for the
    /// feature to arrive rather than being dropped as unroutable.
    /// </summary>
    [Fact]
    public async Task An_intent_with_no_registered_deliverer_is_kept()
    {
        await CommitAsync(Intent("orphan"));

        var result = await new GroundworkDesignPostCommitRedrive(_outbox, [], new AdvanceableTimeProvider(Now))
            .SweepAsync();

        Assert.Equal(1, result.DeferredCount);
        Assert.Contains("No deliverer", Assert.Single(result.Intents).FailureMessage);
        Assert.NotNull(await _outbox.FindAsync(Intent("orphan").IntentId));
    }

    /// <summary>
    /// The projected id column is bounded at 256 because SQL Server refuses an unbounded index key column, so
    /// a longer logical id rides through a digest. The durable logical value must survive that intact.
    /// </summary>
    [Fact]
    public async Task A_logical_id_longer_than_the_projection_bound_survives_the_digest()
    {
        var longId = new string(
            'x',
            GroundworkDesignAtomicWriteStorageManifest.DesignPostCommitIntentIdProjectionLength + 1);
        var intent = new DesignPostCommitIntent(longId, Kind, Write("target-long"));
        await CommitAsync(intent);

        var projected = GroundworkDesignPostCommitOutbox.ProjectIntentId(longId);
        Assert.True(projected.Length <=
                    GroundworkDesignAtomicWriteStorageManifest.DesignPostCommitIntentIdProjectionLength);
        Assert.NotEqual(longId, projected);
        Assert.Equal(longId, (await _outbox.FindAsync(longId))?.IntentId);

        var claim = Assert.Single(await _outbox.ClaimAsync(new("owner", Now, TimeSpan.FromMinutes(1), 8)));
        Assert.Equal(longId, claim.Intent.IntentId);
    }

    /// <summary>
    /// Two intents recorded in the same tick still need a total order, or a claim can oscillate between them
    /// and a page boundary can starve one forever. Recorded order, then id, is what supplies it.
    /// </summary>
    [Fact]
    public async Task Intents_recorded_in_one_tick_still_claim_in_a_total_order()
    {
        await CommitAsync(Intent("b"));
        await CommitAsync(Intent("a"));
        await CommitAsync(Intent("c"));

        var claims = await _outbox.ClaimAsync(new("owner", Now, TimeSpan.FromMinutes(1), 8));

        Assert.Equal(
            [Intent("a").IntentId, Intent("b").IntentId, Intent("c").IntentId],
            claims.Select(claim => claim.Intent.IntentId));
    }

    /// <summary>One sweep reads one bounded page, which is what keeps a sweep's blast radius bounded.</summary>
    [Fact]
    public async Task A_sweep_claims_at_most_its_page()
    {
        foreach (var suffix in (string[])["a", "b", "c"])
            await CommitAsync(Intent(suffix));

        Assert.Equal(2, (await _outbox.ClaimAsync(new("owner", Now, TimeSpan.FromMinutes(1), 2))).Count);
    }

    /// <summary>
    /// The committing caller normally delivers its own intent and drops it unconditionally, having no claim.
    /// </summary>
    [Fact]
    public async Task The_committing_caller_can_discard_its_own_intent()
    {
        await CommitAsync(Intent("inline"));

        await _outbox.DiscardAsync(Intent("inline").IntentId);

        Assert.Null(await _outbox.FindAsync(Intent("inline").IntentId));
        // Discarding an intent that is already gone is the crash-retry case, and is not an error.
        await _outbox.DiscardAsync(Intent("inline").IntentId);
    }

    [Fact]
    public void A_second_deliverer_for_one_intent_kind_is_rejected() =>
        Assert.Throws<InvalidOperationException>(() =>
            new GroundworkDesignPostCommitRedrive(_outbox, [new RecordingDeliverer(), new RecordingDeliverer()]));

    [Fact]
    public void The_retry_backoff_grows_and_then_stops_growing()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), GroundworkDesignPostCommitRedrive.RetryDelay(0));
        Assert.Equal(TimeSpan.FromSeconds(10), GroundworkDesignPostCommitRedrive.RetryDelay(1));
        Assert.Equal(TimeSpan.FromMinutes(5), GroundworkDesignPostCommitRedrive.RetryDelay(20));
        // Guards the exponent: a saturated attempt count must not wrap round into a short delay.
        Assert.Equal(TimeSpan.FromMinutes(5), GroundworkDesignPostCommitRedrive.RetryDelay(int.MaxValue));
    }

    private GroundworkDesignPostCommitRedrive Redrive(IDesignPostCommitIntentDeliverer deliverer) =>
        new(_outbox, [deliverer], new AdvanceableTimeProvider(Now));

    /// <summary>Writes the intent the way a design commit does: inside the commit, not after it.</summary>
    private Task CommitAsync(DesignPostCommitIntent intent) =>
        _documents.SaveAllAsync(
            DocumentCommitScope.Of(IntentDocumentKind),
            [GroundworkDesignPostCommitOutbox.CreateSaveRequest(intent, Now)]);

    private static DesignPostCommitIntent Intent(string suffix) =>
        new($"intent-{suffix}", Kind, Write($"target-{suffix}"));

    private static DesignPostCommitDocumentWrite Write(string id) => new(TargetKind, id, "1.0.0", Payload(id));

    private static string Payload(string id) => $$"""{"id":"{{id}}"}""";

    private sealed class RecordingDeliverer : IDesignPostCommitIntentDeliverer
    {
        private readonly List<SaveDocumentRequest> _delivered = [];

        public string IntentKind => Kind;

        public int FailNextAttempts { get; set; }

        public IReadOnlyList<SaveDocumentRequest> Delivered => _delivered;

        public ValueTask DeliverAsync(
            DesignPostCommitIntent intent,
            CancellationToken cancellationToken = default)
        {
            if (FailNextAttempts > 0)
            {
                FailNextAttempts--;
                throw new InvalidOperationException("The delivery target is unavailable.");
            }

            _delivered.Add(intent.Document.ToSaveRequest());
            return ValueTask.CompletedTask;
        }
    }

    private sealed class AdvanceableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
