using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Persistence.EntityFrameworkCore;
using Elsa.Rules.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using static Elsa.Rules.Tests.RuleTestData;

namespace Elsa.Rules.Tests;

public sealed class RuleDeliveryStoreTests : IAsyncLifetime
{
    private readonly RuleStoreTestFixture fixture = new();
    private static readonly TimeSpan DeliveryLeaseDuration = TimeSpan.FromMinutes(1);
    public Task InitializeAsync() => fixture.InitializeAsync();
    public Task DisposeAsync() => fixture.DisposeAsync();

    [Fact]
    public async Task Direct_terminal_run_has_no_delivery_obligation()
    {
        var invocation = RuleStoreTestFixture.NewInvocation();
        var store = CreateStore();

        var terminal = await store.CancelAsync(new(invocation));
        var claim = await store.ClaimDeliveryAsync(new("delivery-worker", [1]));

        Assert.Equal(RuleCancellationDisposition.Cancelled, terminal.Disposition);
        Assert.Equal(RuleDeliveryClaimDisposition.NoEligibleDelivery, claim.Disposition);
        Assert.Null(claim.Lease);
        await using var db = fixture.Contexts.CreateDbContext();
        var row = await db.Runs.AsNoTracking().SingleAsync(candidate => candidate.RunKey == Key(invocation.Identity));
        Assert.Null(row.DeliveryId64);
        Assert.Null(row.DeliveryPayloadJson);
        Assert.False(row.DeliveryAcknowledged);
    }

    [Fact]
    public async Task Native_terminal_claim_and_read_return_the_complete_frozen_envelope()
    {
        var invocation = await CreateNativeTerminalAsync();
        var store = CreateStore();

        var claim = await store.ClaimDeliveryAsync(new("delivery-worker", [1]));
        var lease = Assert.IsType<RuleDeliveryLease>(claim.Lease);
        var read = await store.ReadDeliveryAsync(lease);

        Assert.Equal(RuleDeliveryClaimDisposition.Claimed, claim.Disposition);
        Assert.Equal(RuleDeliveryReadDisposition.Ready, read.Disposition);
        var envelope = Assert.IsType<RuleTerminalEnvelope>(read.Envelope);
        Assert.Equal(lease.DeliveryId, envelope.DeliveryId);
        Assert.Equal(invocation.NativeDestination, envelope.Destination);
        Assert.Equal(RuleTerminalKind.Cancelled, envelope.Result.Kind);
        Assert.Equal(RuleRecordCodec.Hash(RuleRecordCodec.DeliveryBytes(envelope.DeliveryId,
            envelope.Destination, envelope.Result)), envelope.CanonicalPayloadSha256);
        await using var db = fixture.Contexts.CreateDbContext();
        var row = await db.Runs.AsNoTracking().SingleAsync();
        Assert.Equal(row.DeliveryExpiresAtUtcTicks, lease.ExpiresAt.UtcTicks);
    }

    [Fact]
    public async Task Acknowledgement_validates_with_the_store_retained_invocation_and_scope()
    {
        var invocation = await CreateNativeTerminalAsync();
        var validator = new FixedProofValidator(RuleDeliveryProofValidationDisposition.Equivalent);
        var store = CreateStore(validator);
        var lease = Assert.IsType<RuleDeliveryLease>((await store.ClaimDeliveryAsync(new("delivery-worker", [1]))).Lease);
        var read = await store.ReadDeliveryAsync(lease);

        var confirmation = await store.AcknowledgeDeliveryAsync(lease, new(ProofFor(read)));

        Assert.Equal(RuleTransitionDisposition.Acknowledged, confirmation.Disposition);
        var retained = Assert.IsType<RuleInvocationEnvelope>(validator.Invocation);
        Assert.Equal(invocation.Identity, retained.Identity);
        Assert.Equal(RuleRecordCodec.InvocationBytes(invocation), RuleRecordCodec.InvocationBytes(retained));
        Assert.NotSame(invocation, retained);
    }

    [Fact]
    public async Task Delivery_read_checks_exact_identity_before_decoding_payload()
    {
        var invocation = await CreateNativeTerminalAsync();
        var store = CreateStore();
        var lease = Assert.IsType<RuleDeliveryLease>((await store.ClaimDeliveryAsync(new("delivery-worker", [1]))).Lease);
        var other = RuleStoreTestFixture.NewInvocation().Identity;
        var wrongScopeLease = new RuleDeliveryLease(other, lease.DeliveryId, lease.Owner, lease.Fence, lease.ExpiresAt);

        var read = await store.ReadDeliveryAsync(wrongScopeLease);
        var acknowledgement = await store.AcknowledgeDeliveryAsync(wrongScopeLease,
            new(ProofFor(await store.ReadDeliveryAsync(lease))), CancellationToken.None);

        Assert.Equal(RuleDeliveryReadDisposition.NotFound, read.Disposition);
        Assert.Null(read.Envelope);
        Assert.Equal(RuleTransitionDisposition.LeaseLost, acknowledgement.Disposition);
        Assert.NotNull(invocation.NativeDestination);
    }

    [Fact]
    public async Task Reclaim_replaces_the_delivery_fence_and_stales_the_old_reader_and_acknowledger()
    {
        var invocation = await CreateNativeTerminalAsync();
        var clock = new RuleStoreTestInterceptors.SqliteClock(DateTimeOffset.UtcNow.UtcTicks);
        var store = CreateStore(interceptors: [clock]);
        var original = Assert.IsType<RuleDeliveryLease>((await store.ClaimDeliveryAsync(new("worker-one", [1]))).Lease);
        clock.Set(original.ExpiresAt.UtcTicks);

        var replacement = Assert.IsType<RuleDeliveryLease>((await store.ClaimDeliveryAsync(new("worker-two", [1]))).Lease);
        var staleRead = await store.ReadDeliveryAsync(original);
        var staleAck = await store.AcknowledgeDeliveryAsync(original,
            new(ProofFor(await store.ReadDeliveryAsync(replacement))), CancellationToken.None);

        Assert.True(replacement.Fence > original.Fence);
        Assert.Equal("worker-two", replacement.Owner);
        Assert.Equal(RuleDeliveryReadDisposition.LeaseLost, staleRead.Disposition);
        Assert.Equal(RuleTransitionDisposition.LeaseLost, staleAck.Disposition);
        Assert.NotNull(invocation.NativeDestination);
    }

    [Fact]
    public async Task Claim_rolls_back_if_database_clock_reaches_expiry_after_save()
    {
        var invocation = await CreateNativeTerminalAsync();
        var start = DateTimeOffset.UtcNow.UtcTicks;
        var clock = new RuleStoreTestInterceptors.SqliteClock(start);
        var expires = start + DeliveryLeaseDuration.Ticks;
        var contexts = new RuleStoreTestFixture.SqliteContextFactory(fixture.DatabasePath,
            [clock, new RuleStoreTestInterceptors.ExpireAfterSave(clock, expires)]);
        var store = CreateStore(contexts, deliveryLeaseDuration: DeliveryLeaseDuration);

        var claim = await store.ClaimDeliveryAsync(new("delivery-worker", [1]));

        Assert.Equal(RuleDeliveryClaimDisposition.Unavailable, claim.Disposition);
        Assert.Null(claim.Lease);
        await using var db = fixture.Contexts.CreateDbContext();
        var row = await db.Runs.AsNoTracking().SingleAsync(candidate => candidate.RunKey == Key(invocation.Identity)
            && candidate.DeliveryId64 != null
            && candidate.DeliveryOwner == null);
        Assert.Equal(0, row.DeliveryFence);
        Assert.Null(row.DeliveryExpiresAtUtcTicks);
    }

    [Fact]
    public async Task Acknowledgement_rolls_back_if_database_clock_reaches_expiry_after_save()
    {
        await CreateNativeTerminalAsync();
        var initialStore = CreateStore();
        var lease = Assert.IsType<RuleDeliveryLease>((await initialStore.ClaimDeliveryAsync(new("delivery-worker", [1]))).Lease);
        var proof = ProofFor(await initialStore.ReadDeliveryAsync(lease));
        var start = lease.ExpiresAt.UtcTicks - DeliveryLeaseDuration.Ticks;
        var clock = new RuleStoreTestInterceptors.SqliteClock(start);
        var contexts = new RuleStoreTestFixture.SqliteContextFactory(fixture.DatabasePath,
            [clock, new RuleStoreTestInterceptors.ExpireAfterSave(clock, lease.ExpiresAt.UtcTicks)]);
        var store = CreateStore(contexts, new FixedProofValidator(RuleDeliveryProofValidationDisposition.Equivalent));

        var result = await store.AcknowledgeDeliveryAsync(lease, new(proof));

        Assert.Equal(RuleTransitionDisposition.LeaseLost, result.Disposition);
        await using var db = fixture.Contexts.CreateDbContext();
        var row = await db.Runs.AsNoTracking().SingleAsync(candidate => candidate.DeliveryId64 == lease.DeliveryId);
        Assert.False(row.DeliveryAcknowledged);
        Assert.Null(row.DeliveryAcknowledgementJson);
        Assert.Equal(lease.Owner, row.DeliveryOwner);
        Assert.Equal(lease.Fence, row.DeliveryFence);
    }

    [Fact]
    public async Task Missing_native_proof_validator_retains_delivery_as_retryable()
    {
        await CreateNativeTerminalAsync();
        var store = CreateStore();
        var lease = Assert.IsType<RuleDeliveryLease>((await store.ClaimDeliveryAsync(new("delivery-worker", [1]))).Lease);
        var proof = ProofFor(await store.ReadDeliveryAsync(lease));

        var result = await store.AcknowledgeDeliveryAsync(lease, new(proof));

        Assert.Equal(RuleTransitionDisposition.ProofUnavailable, result.Disposition);
        await using var db = fixture.Contexts.CreateDbContext();
        var row = await db.Runs.AsNoTracking().SingleAsync(candidate => candidate.DeliveryId64 == lease.DeliveryId);
        Assert.False(row.DeliveryAcknowledged);
        Assert.Null(row.DeliveryAcknowledgementJson);
    }

    [Fact]
    public async Task Proof_must_match_the_complete_frozen_destination_and_payload_before_native_validation()
    {
        await CreateNativeTerminalAsync();
        var validator = new FixedProofValidator(RuleDeliveryProofValidationDisposition.Equivalent);
        var store = CreateStore(proofValidator: validator);
        var lease = Assert.IsType<RuleDeliveryLease>((await store.ClaimDeliveryAsync(new("delivery-worker", [1]))).Lease);
        var valid = ProofFor(await store.ReadDeliveryAsync(lease));
        var mismatched = new RuleDeliveryProof(valid.Kind, DifferentSha256(valid.DestinationIdentity), valid.TerminalId,
            valid.PayloadSha256, valid.ParentCommitOrTerminalId);

        var result = await store.AcknowledgeDeliveryAsync(lease, new(mismatched));

        Assert.Equal(RuleTransitionDisposition.Invalid, result.Disposition);
        Assert.Equal(0, validator.Calls);
        await using var db = fixture.Contexts.CreateDbContext();
        var row = await db.Runs.AsNoTracking().SingleAsync(candidate => candidate.DeliveryId64 == lease.DeliveryId);
        Assert.False(row.DeliveryAcknowledged);
    }

    [Fact]
    public async Task Exact_proof_replays_after_ack_but_changed_proof_conflicts()
    {
        await CreateNativeTerminalAsync();
        var validator = new FixedProofValidator(RuleDeliveryProofValidationDisposition.Equivalent);
        var store = CreateStore(proofValidator: validator);
        var lease = Assert.IsType<RuleDeliveryLease>((await store.ClaimDeliveryAsync(new("delivery-worker", [1]))).Lease);
        var proof = ProofFor(await store.ReadDeliveryAsync(lease));

        var first = await store.AcknowledgeDeliveryAsync(lease, new(proof));
        var replay = await store.AcknowledgeDeliveryAsync(lease, new(proof));
        var changed = await store.AcknowledgeDeliveryAsync(lease, new(new RuleDeliveryProof(proof.Kind,
            proof.DestinationIdentity, proof.TerminalId, proof.PayloadSha256, "different-parent-commit")));

        Assert.Equal(RuleTransitionDisposition.Acknowledged, first.Disposition);
        Assert.Equal(RuleTransitionDisposition.Replay, replay.Disposition);
        Assert.Equal(first.TransitionId, replay.TransitionId);
        Assert.Equal(RuleTransitionDisposition.Conflict, changed.Disposition);
        Assert.Equal(1, validator.Calls);
    }

    [Fact]
    public async Task Unavailable_proof_validation_replays_when_another_acknowledgement_wins()
    {
        await CreateNativeTerminalAsync();
        var validator = new UnavailableFirstProofValidator();
        var store = CreateStore(proofValidator: validator);
        var lease = Assert.IsType<RuleDeliveryLease>((await store.ClaimDeliveryAsync(new("delivery-worker", [1]))).Lease);
        var proof = ProofFor(await store.ReadDeliveryAsync(lease));

        var unavailableAttempt = store.AcknowledgeDeliveryAsync(lease, new(proof)).AsTask();
        await validator.FirstValidationStarted;
        var winner = await store.AcknowledgeDeliveryAsync(lease, new(proof));
        byte[] winnerToken;
        await using (var winnerDb = fixture.Contexts.CreateDbContext())
        {
            var winnerRow = await winnerDb.Runs.AsNoTracking().SingleAsync(candidate => candidate.DeliveryId64 == lease.DeliveryId);
            winnerToken = winnerRow.ConcurrencyToken.ToArray();
        }
        validator.ReleaseFirstValidation();
        var retry = await unavailableAttempt;

        Assert.Equal(RuleTransitionDisposition.Acknowledged, winner.Disposition);
        Assert.Equal(RuleTransitionDisposition.Replay, retry.Disposition);
        Assert.Equal(winner.TransitionId, retry.TransitionId);
        Assert.Equal(2, validator.Calls);
        await using var db = fixture.Contexts.CreateDbContext();
        var row = await db.Runs.AsNoTracking().SingleAsync(candidate => candidate.DeliveryId64 == lease.DeliveryId);
        Assert.Equal(winnerToken, row.ConcurrencyToken);
        Assert.True(row.DeliveryAcknowledged);
        Assert.Equal(System.Text.Encoding.UTF8.GetString(RuleRecordCodec.DeliveryAcknowledgementBytes(proof)),
            row.DeliveryAcknowledgementJson);
    }

    private async Task<RuleInvocationEnvelope> CreateNativeTerminalAsync()
    {
        var invocation = Copy(RuleStoreTestFixture.NewInvocation(), destination: Destination());
        var result = await CreateStore().CancelAsync(new(invocation));
        Assert.Equal(RuleCancellationDisposition.Cancelled, result.Disposition);
        return invocation;
    }

    private EfRuleRunStore CreateStore(IRuleDeliveryProofValidator? proofValidator = null,
        TimeSpan? deliveryLeaseDuration = null, IInterceptor[]? interceptors = null) =>
        CreateStore(new RuleStoreTestFixture.SqliteContextFactory(fixture.DatabasePath, interceptors),
            proofValidator, deliveryLeaseDuration);

    private EfRuleRunStore CreateStore(RuleStoreTestFixture.SqliteContextFactory contexts,
        IRuleDeliveryProofValidator? proofValidator = null, TimeSpan? deliveryLeaseDuration = null) =>
        new(contexts, new RuleStoreTestFixture.FixedAdmissionValidator(RuleAdmissionValidationDisposition.Unavailable),
            deliveryLeaseDuration: deliveryLeaseDuration, deliveryProofValidator: proofValidator);

    private static RuleDeliveryProof ProofFor(RuleDeliveryReadResult read)
    {
        var envelope = Assert.IsType<RuleTerminalEnvelope>(read.Envelope);
        return new(RuleDeliveryProofKind.Consumed, RuleRecordCodec.DestinationIdentity(envelope.Destination),
            envelope.Result.TerminalId, envelope.CanonicalPayloadSha256, "parent-commit");
    }

    private static string DifferentSha256(string digest) => (digest[0] == '0' ? '1' : '0') + digest[1..];

    private static string Key(RuleRunIdentity identity) => RuleRecordCodec.Hash(RuleRecordCodec.ScopeBytes(identity));

    private sealed class FixedProofValidator(RuleDeliveryProofValidationDisposition disposition) : IRuleDeliveryProofValidator
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        public RuleInvocationEnvelope? Invocation { get; private set; }

        public ValueTask<RuleDeliveryProofValidationResult> ValidateAsync(RuleInvocationEnvelope invocation, RuleTerminalEnvelope envelope,
            RuleDeliveryProof proof, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _calls);
            Invocation = invocation;
            return ValueTask.FromResult(new RuleDeliveryProofValidationResult(disposition));
        }
    }

    private sealed class UnavailableFirstProofValidator : IRuleDeliveryProofValidator
    {
        private readonly TaskCompletionSource _firstValidationStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseFirstValidation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);
        public Task FirstValidationStarted => _firstValidationStarted.Task;
        public void ReleaseFirstValidation() => _releaseFirstValidation.TrySetResult();

        public async ValueTask<RuleDeliveryProofValidationResult> ValidateAsync(RuleInvocationEnvelope invocation, RuleTerminalEnvelope envelope,
            RuleDeliveryProof proof, CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref _calls);
            if (call == 1)
            {
                _firstValidationStarted.TrySetResult();
                await _releaseFirstValidation.Task.WaitAsync(cancellationToken);
                return new(RuleDeliveryProofValidationDisposition.Unavailable);
            }
            return new(RuleDeliveryProofValidationDisposition.Equivalent);
        }
    }
}
