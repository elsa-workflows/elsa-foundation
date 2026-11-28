using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class StimulusDeliveryStoreTests
{
    [Fact]
    public async Task Private_prepared_start_attempt_is_fenced_across_disarm_birth_and_durable_acknowledgement()
    {
        var directory = Directory.CreateTempSubdirectory("elsa-recipient-attempt");
        var path = Path.Combine(directory.FullName, "runtime.db");
        var proof = new PreparedProof();
        var abandoned = Preparation('A', "first-issuance");
        var confirmed = Preparation('B', "second-issuance");
        RuntimeCommittedRecipientAuthority authority;
        RuntimeDeliverySourceContext first;
        RuntimeDeliverySourceContext second;
        long firstAttempt;
        long secondAttempt;
        try
        {
            await using (var harness = await Harness.OpenPreparedAttemptAsync(path, create: true))
            {
                var fixture = await PrepareIngressAsync(harness);
                var accepted = await fixture.Port.AdmitAsync(fixture.Request);
                var recipient = Assert.Single(accepted.Item.SubscriptionDelivery!.Recipients);
                var seal = EfRuntimeIngressDeliveryStore.CreatePreparedStartQualificationSeal(accepted.Item, recipient);
                authority = new(seal.SourceOutboxItemId, seal.SourceFingerprint, seal.SelectionFingerprint,
                    Scope, recipient.RecipientId, seal.Fingerprint);
                first = await ClaimAsync(harness, "first-owner");
                var port = PreparedPort(harness, proof);
                await Assert.ThrowsAsync<NotSupportedException>(() => fixture.Port.CommitDispatchAsync(first, seal).AsTask());
                var forgedSeal = new RuntimeDispatchSeal(seal.Version, seal.SourceOutboxItemId, seal.SourceFingerprint,
                    seal.SelectionFingerprint, seal.RecipientId, seal.StartIdentity, seal.CommittedAt, seal.CommandProtocol,
                    seal.CommandProtocolVersion, JsonSerializer.SerializeToElement(new { recipientFingerprint = "different" }));
                await AssertInvalidAuthorityAsync(() => port.CommitDispatchAsync(first, forgedSeal).AsTask());
                await port.CommitDispatchAsync(first, seal);

                var otherScope = new RuntimeCommittedRecipientAuthority(authority.SourceOutboxItemId, authority.SourceFingerprint,
                    authority.SelectionFingerprint, new("foreign"), authority.RecipientId, authority.SealFingerprint);
                await AssertInvalidAuthorityAsync(() => BindAsync(port, first, otherScope, 0, abandoned).AsTask());
                var otherSeal = new RuntimeCommittedRecipientAuthority(authority.SourceOutboxItemId, authority.SourceFingerprint,
                    authority.SelectionFingerprint, Scope, authority.RecipientId, forgedSeal.Fingerprint);
                await AssertInvalidAuthorityAsync(() => BindAsync(port, first, otherSeal, 0, abandoned).AsTask());
                await AssertInvalidAuthorityAsync(() => port.TryBindPreparedStartAsync(first, authority, 0, "foreign",
                    abandoned.ActorReference, abandoned.Issuance).AsTask());
                firstAttempt = Assert.IsType<long>(await BindAsync(port, first, authority, 0, abandoned));
                Assert.Equal(1L, firstAttempt);
                Assert.Equal(firstAttempt, await BindAsync(port, first, authority, 0, abandoned));
                Assert.Null(await BindAsync(port, first, authority, 0, confirmed));
                await port.RecordRecipientFailureAsync(first, authority.RecipientId, RuntimeRecipientRecoveryState.RetryWaiting,
                    "test-observer-unavailable", Now);
            }

            // The writer closes after recording the preparation and before a birth or acknowledgement.
            await using (var harness = await Harness.OpenPreparedAttemptAsync(path))
            {
                harness.Clock.Advance(TimeSpan.FromMinutes(2));
                var port = PreparedPort(harness, proof);
                Assert.Equal((firstAttempt, false), await port.ReadPreparedStartAttemptAsync(authority));
                second = await ClaimAsync(harness, "second-owner");
                await Assert.ThrowsAsync<RuntimePostCommitOutboxStaleClaimException>(() =>
                    BindAsync(port, first, authority, 0, confirmed).AsTask());

                var unknown = Progress(await port.ReconcilePreparedStartAsync(second, authority, firstAttempt));
                Assert.Equal((firstAttempt, false), await port.ReadPreparedStartAttemptAsync(authority));
                Assert.Null(unknown.Receipt);
                Assert.Null(await BindAsync(port, second, authority, firstAttempt, confirmed));
                Assert.Contains("ingress-material", await harness.Delivery.ListPinnedExecutableArtifactIdsAsync());

                proof.Set(abandoned, RuntimePreparedStartProof.Disarmed);
                await port.ReconcilePreparedStartAsync(second, authority, firstAttempt);
                Assert.Equal((firstAttempt, true), await port.ReadPreparedStartAttemptAsync(authority));
                Assert.Null(await BindAsync(port, second, authority, firstAttempt, abandoned));
                secondAttempt = Assert.IsType<long>(await BindAsync(port, second, authority, firstAttempt, confirmed));
                Assert.Equal(2L, secondAttempt);
                Assert.Null(await BindAsync(port, second, authority, firstAttempt, abandoned));
                await port.ReconcilePreparedStartAsync(second, authority, firstAttempt);
                Assert.Equal((secondAttempt, false), await port.ReadPreparedStartAttemptAsync(authority));
                proof.Set(confirmed, RuntimePreparedStartProof.Accepted);
            }

            // The next writer finds a committed external birth with no Runtime acknowledgement.
            await using (var harness = await Harness.OpenPreparedAttemptAsync(path))
            {
                harness.Clock.Advance(TimeSpan.FromMinutes(2));
                var port = PreparedPort(harness, proof);
                Assert.Equal((secondAttempt, false), await port.ReadPreparedStartAttemptAsync(authority));
                Assert.Null(Progress((await port.ReadCommittedRecipientAsync(authority)).Item).Receipt);
                proof.BeforeObservation = () => harness.Clock.Advance(TimeSpan.FromMinutes(2));
                await Assert.ThrowsAsync<RuntimePostCommitOutboxStaleClaimException>(() =>
                    port.ReconcilePreparedStartAsync(second, authority, secondAttempt).AsTask());
                proof.BeforeObservation = null;
                var owner = await ClaimAsync(harness, "final-owner");
                var settled = await port.ReconcilePreparedStartAsync(owner, authority, secondAttempt);
                var progress = Progress(settled);
                Assert.Equal(RuntimeRecipientPhase.TargetAccepted, progress.Phase);
                Assert.Equal((secondAttempt, false), await port.ReadPreparedStartAttemptAsync(authority));
                Assert.Equal(RuntimeTargetAcceptanceKind.PreparedActorBirth, progress.Receipt?.Kind);
                Assert.Equal(confirmed.ActorReference, progress.Receipt?.EvidenceId);
                Assert.Equal(authority.SealFingerprint, progress.Receipt?.SealFingerprint);
                proof.Set(confirmed, RuntimePreparedStartProof.Unresolved);
                var replay = Progress(await port.ReconcilePreparedStartAsync(owner, authority, secondAttempt));
                Assert.True(progress.Receipt!.IsEquivalentTo(replay.Receipt));
                Assert.Null(await BindAsync(port, owner, authority, secondAttempt, Preparation('C', "late")));
                var completion = (IRuntimePostCommitOutboxClaimCompletionStore)harness.Reopen();
                await completion.CompleteClaimAsync(new(owner.Claim,
                    new(settled.OutboxItemId, RuntimePostCommitOutboxStatus.Delivered, harness.Clock.GetUtcNow())));
                // The existing outbox retains accepted material until target retirement is proved.
                Assert.Contains("ingress-material", await harness.Reopen().ListPinnedExecutableArtifactIdsAsync());
            }

            await using (var harness = await Harness.OpenPreparedAttemptAsync(path))
            {
                var port = PreparedPort(harness, proof);
                var item = (await port.ReadCommittedRecipientAsync(authority)).Item;
                Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, item.Status);
                Assert.Equal(confirmed.ActorReference, Progress(item).Receipt?.EvidenceId);
                Assert.Equal((secondAttempt, false), await port.ReadPreparedStartAttemptAsync(authority));
            }
        }
        finally { directory.Delete(recursive: true); }
    }

    private sealed record PreparedOffer(string ActorReference, string Issuance);

    private static PreparedOffer Preparation(char reference, string issuance) => new(new string(reference, 64), issuance);

    private static ValueTask<long?> BindAsync(EfRuntimeIngressDeliveryStore store, RuntimeDeliverySourceContext source,
        RuntimeCommittedRecipientAuthority authority, long previous, PreparedOffer offer) =>
        store.TryBindPreparedStartAsync(source, authority, previous, Scope.Value, offer.ActorReference, offer.Issuance);

    private static RuntimeRecipientProgress Progress(RuntimePostCommitOutboxItem item) => Assert.Single(item.SubscriptionDelivery!.Progress);

    private static EfRuntimeIngressDeliveryStore PreparedPort(Harness harness, PreparedProof proof) =>
        Assert.IsType<EfRuntimeIngressDeliveryStore>(harness.IngressInSeparateScope(proof).Port);

    private static async Task<RuntimeDeliverySourceContext> ClaimAsync(Harness harness, string owner) =>
        new(Assert.Single(await harness.Claims.ClaimAsync(new(owner, harness.Clock.GetUtcNow(), TimeSpan.FromMinutes(1), 1))), Scope);

    private sealed class PreparedProof : IRuntimePreparedStartAttemptProof
    {
        private readonly Dictionary<PreparedOffer, RuntimePreparedStartProof> proofs = [];
        internal Action? BeforeObservation { get; set; }

        internal void Set(PreparedOffer prepared, RuntimePreparedStartProof proof) => proofs[prepared] = proof;

        public ValueTask<RuntimePreparedStartProof> ObserveAsync(TriggerScope scope, string actorReference, string issuance, CancellationToken cancellationToken)
        {
            Assert.Equal(new TriggerScope(Scope, Scope.Value), scope);
            BeforeObservation?.Invoke();
            return ValueTask.FromResult(proofs.GetValueOrDefault(new(actorReference, issuance)));
        }
    }

    private sealed partial class Harness
    {
        internal static async Task<Harness> OpenPreparedAttemptAsync(string path, bool create = false)
        {
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
            await connection.OpenAsync();
            var harness = new Harness(connection, null, referenceLess: false);
            if (create) await harness.Context!.Database.EnsureCreatedAsync();
            return harness;
        }
    }
}
