using System.Text;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Persistence.EntityFrameworkCore;
using Elsa.Rules.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using static Elsa.Rules.Tests.RuleStoreTestInterceptors;

namespace Elsa.Rules.Tests;

public sealed class RuleLeaseStoreTests : IAsyncLifetime
{
    private readonly RuleStoreTestFixture fixture = new();
    private static readonly long InitialUtcTicks = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).Ticks;

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    [Fact]
    public async Task Claim_selects_compatible_pin_and_schemas_without_changing_model_revision()
    {
        var invocation = RuleStoreTestFixture.NewInvocation();
        await AdmitAsync(invocation);
        var clock = new SqliteClock(InitialUtcTicks);
        var store = CreateStore(clock, TimeSpan.FromSeconds(30));
        var before = await SnapshotAsync(invocation.Identity);

        var result = await store.ClaimWorkAsync(ClaimRequest(invocation));

        Assert.Equal(RuleWorkClaimDisposition.Claimed, result.Disposition);
        Assert.NotNull(result.Token);
        Assert.Equal(1, result.Token.ExpectedModelRevision);
        Assert.Equal("worker", result.Token.Lease.Owner);
        Assert.Equal(1, result.Token.Lease.Fence);
        Assert.Equal(new DateTimeOffset(InitialUtcTicks + TimeSpan.FromSeconds(30).Ticks, TimeSpan.Zero), result.Token.Lease.ExpiresAt);
        var after = await SnapshotAsync(invocation.Identity);
        Assert.Equal(before.ModelRevision, after.ModelRevision);
        Assert.Equal(before.Status, after.Status);
        Assert.Equal("worker", after.WorkOwner);
        Assert.Equal(1, after.WorkFence);
        Assert.Equal(result.Token.Lease.ExpiresAt.UtcDateTime.Ticks, after.WorkExpiresAtUtcTicks);
        Assert.NotEqual(before.ConcurrencyToken, after.ConcurrencyToken);
    }

    [Theory]
    [InlineData("pin")]
    [InlineData("invocation-schema")]
    [InlineData("receipt-schema")]
    [InlineData("state-schema")]
    public async Task Claim_skips_work_without_a_compatible_pin_and_schema(string incompatibility)
    {
        var invocation = RuleStoreTestFixture.NewInvocation();
        await AdmitAsync(invocation);
        var clock = new SqliteClock(InitialUtcTicks);
        var store = CreateStore(clock, TimeSpan.FromSeconds(30));
        var before = await SnapshotAsync(invocation.Identity);
        var request = incompatibility switch
        {
            "pin" => ClaimRequest(invocation, pins: [DifferentPin(invocation.ExpectedImplementation)]),
            "invocation-schema" => ClaimRequest(invocation, invocationSchemas: [invocation.InvocationSchemaVersion + 1]),
            "receipt-schema" => ClaimRequest(invocation, receiptSchemas: [invocation.ExpectedImplementation.ReceiptSchemaVersion + 1]),
            "state-schema" => ClaimRequest(invocation, stateSchemas: [invocation.ExpectedImplementation.StateSchemaVersion + 1]),
            _ => throw new ArgumentOutOfRangeException(nameof(incompatibility))
        };

        var result = await store.ClaimWorkAsync(request);

        Assert.Equal(RuleWorkClaimDisposition.NoEligibleWork, result.Disposition);
        Assert.Null(result.Token);
        Assert.Equal(before, await SnapshotAsync(invocation.Identity));
    }

    [Fact]
    public async Task An_unsupported_older_candidate_does_not_hide_later_compatible_work()
    {
        var invocations = new[] { RuleStoreTestFixture.NewInvocation(), RuleStoreTestFixture.NewInvocation() }
            .OrderBy(value => RuleRecordCodec.Hash(RuleRecordCodec.ScopeBytes(value.Identity)), StringComparer.Ordinal).ToArray();
        foreach (var invocation in invocations) await AdmitAsync(invocation);
        await using (var db = fixture.Contexts.CreateDbContext())
        {
            var first = await db.Runs.OrderBy(row => row.RunKey).FirstAsync();
            // Simulate a retained future envelope; this worker cannot reconstruct its canonical representation.
            first.InvocationJson = first.InvocationJson.Replace("\"invocationSchemaVersion\":1", "\"invocationSchemaVersion\":2", StringComparison.Ordinal);
            first.Fingerprint64 = RuleRecordCodec.Hash(Encoding.UTF8.GetBytes(first.InvocationJson));
            await db.SaveChangesAsync();
        }
        var before = await SnapshotAsync(invocations[0].Identity);
        var store = CreateStore(new SqliteClock(InitialUtcTicks), TimeSpan.FromSeconds(30));

        var result = await store.ClaimWorkAsync(ClaimRequest(invocations[1]));

        Assert.Equal(RuleWorkClaimDisposition.Claimed, result.Disposition);
        Assert.Equal(invocations[1].Identity, result.Token?.Lease.Identity);
        Assert.Equal(before, await SnapshotAsync(invocations[0].Identity));
    }

    [Fact]
    public async Task Renewal_uses_current_persisted_expiry_and_preserves_fence_and_revision()
    {
        var invocation = RuleStoreTestFixture.NewInvocation();
        var clock = new SqliteClock(InitialUtcTicks);
        var duration = TimeSpan.FromSeconds(10);
        var store = await AdmitAndCreateStoreAsync(invocation, clock, duration);
        var claim = await store.ClaimWorkAsync(ClaimRequest(invocation));
        var originalLease = Assert.IsType<RuleWorkToken>(claim.Token).Lease;

        clock.Set(InitialUtcTicks + TimeSpan.FromSeconds(8).Ticks);
        var firstRenewal = await store.RenewAsync(originalLease);
        Assert.Equal(RuleLeaseDisposition.Renewed, firstRenewal.Disposition);
        var firstExtendedLease = Assert.IsType<RuleWorkLease>(firstRenewal.Lease);
        Assert.Equal(new DateTimeOffset(InitialUtcTicks + TimeSpan.FromSeconds(18).Ticks, TimeSpan.Zero), firstExtendedLease.ExpiresAt);

        // This caller still presents the original, now stale expiry. The persisted lease was extended above.
        clock.Set(InitialUtcTicks + TimeSpan.FromSeconds(11).Ticks);
        var secondRenewal = await store.RenewAsync(originalLease);

        Assert.Equal(RuleLeaseDisposition.Renewed, secondRenewal.Disposition);
        var extendedLease = Assert.IsType<RuleWorkLease>(secondRenewal.Lease);
        Assert.Equal(originalLease.Fence, extendedLease.Fence);
        Assert.Equal(originalLease.Owner, extendedLease.Owner);
        Assert.Equal(new DateTimeOffset(InitialUtcTicks + TimeSpan.FromSeconds(21).Ticks, TimeSpan.Zero), extendedLease.ExpiresAt);
        var after = await SnapshotAsync(invocation.Identity);
        Assert.Equal(1, after.ModelRevision);
        Assert.Equal(originalLease.Fence, after.WorkFence);
        Assert.Equal(extendedLease.ExpiresAt.UtcDateTime.Ticks, after.WorkExpiresAtUtcTicks);
    }

    [Fact]
    public async Task Renewal_with_an_unchanged_expiry_still_rechecks_and_advances_the_concurrency_token()
    {
        var invocation = RuleStoreTestFixture.NewInvocation();
        var clock = new SqliteClock(InitialUtcTicks);
        var duration = TimeSpan.FromSeconds(10);
        var store = await AdmitAndCreateStoreAsync(invocation, clock, duration);
        var claim = await store.ClaimWorkAsync(ClaimRequest(invocation));
        var lease = Assert.IsType<RuleWorkToken>(claim.Token).Lease;
        var before = await SnapshotAsync(invocation.Identity);

        var renewal = await store.RenewAsync(lease);

        Assert.Equal(RuleLeaseDisposition.Renewed, renewal.Disposition);
        Assert.Equal(lease.ExpiresAt, renewal.Lease?.ExpiresAt);
        var after = await SnapshotAsync(invocation.Identity);
        Assert.Equal(lease.Fence, after.WorkFence);
        Assert.Equal(before.ModelRevision, after.ModelRevision);
        Assert.Equal(before.WorkExpiresAtUtcTicks, after.WorkExpiresAtUtcTicks);
        Assert.NotEqual(before.ConcurrencyToken, after.ConcurrencyToken);
    }

    [Fact]
    public async Task Release_invalidates_fence_and_reclaim_increments_it_again()
    {
        var invocation = RuleStoreTestFixture.NewInvocation();
        var clock = new SqliteClock(InitialUtcTicks);
        var store = await AdmitAndCreateStoreAsync(invocation, clock, TimeSpan.FromSeconds(30));
        var claim = await store.ClaimWorkAsync(ClaimRequest(invocation));
        var originalLease = Assert.IsType<RuleWorkToken>(claim.Token).Lease;

        var released = await store.ReleaseAsync(originalLease);

        Assert.Equal(RuleLeaseDisposition.Released, released.Disposition);
        Assert.Null(released.Lease);
        var afterRelease = await SnapshotAsync(invocation.Identity);
        Assert.Null(afterRelease.WorkOwner);
        Assert.Null(afterRelease.WorkExpiresAtUtcTicks);
        Assert.Equal(2, afterRelease.WorkFence);
        Assert.Equal(1, afterRelease.ModelRevision);
        Assert.Equal(RuleLeaseDisposition.LeaseLost, (await store.RenewAsync(originalLease)).Disposition);

        var reclaimed = await store.ClaimWorkAsync(ClaimRequest(invocation, owner: "replacement-worker"));

        Assert.Equal(RuleWorkClaimDisposition.Claimed, reclaimed.Disposition);
        Assert.Equal("replacement-worker", reclaimed.Token?.Lease.Owner);
        Assert.Equal(3, reclaimed.Token?.Lease.Fence);
        Assert.Equal(1, reclaimed.Token?.ExpectedModelRevision);
    }

    [Fact]
    public async Task Expired_work_is_reclaimed_at_the_expiry_boundary_and_the_original_owner_is_rejected()
    {
        var invocation = RuleStoreTestFixture.NewInvocation();
        var clock = new SqliteClock(InitialUtcTicks);
        var store = await AdmitAndCreateStoreAsync(invocation, clock, TimeSpan.FromSeconds(30));
        var old = Assert.IsType<RuleWorkToken>((await store.ClaimWorkAsync(ClaimRequest(invocation))).Token);
        clock.Set(old.Lease.ExpiresAt.UtcTicks);

        var replacement = await store.ClaimWorkAsync(ClaimRequest(invocation, owner: "replacement"));

        Assert.Equal(RuleWorkClaimDisposition.Claimed, replacement.Disposition);
        Assert.Equal(old.Lease.Fence + 1, replacement.Token?.Lease.Fence);
        Assert.Equal(old.ExpectedModelRevision, replacement.Token?.ExpectedModelRevision);
        var before = await SnapshotAsync(invocation.Identity);
        Assert.Equal(RuleLeaseDisposition.LeaseLost, (await store.RenewAsync(old.Lease)).Disposition);
        Assert.Equal(RuleLeaseDisposition.LeaseLost, (await store.ReleaseAsync(old.Lease)).Disposition);
        Assert.Equal(before, await SnapshotAsync(invocation.Identity));
    }

    [Fact]
    public async Task Competing_claims_on_the_same_pending_run_have_one_winner()
    {
        var invocation = RuleStoreTestFixture.NewInvocation();
        await AdmitAsync(invocation);
        var clock = new SqliteClock(InitialUtcTicks);
        var barrier = new FirstTwoRunReads();
        var contexts = new RuleStoreTestFixture.SqliteContextFactory(fixture.DatabasePath, [clock, barrier]);
        var validator = new RuleStoreTestFixture.FixedAdmissionValidator(RuleAdmissionValidationDisposition.Ready);
        var first = new EfRuleRunStore(contexts, validator, TimeSpan.FromSeconds(30));
        var second = new EfRuleRunStore(contexts, validator, TimeSpan.FromSeconds(30));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var results = await Task.WhenAll(
            first.ClaimWorkAsync(ClaimRequest(invocation, owner: "worker-a"), deadline.Token).AsTask(),
            second.ClaimWorkAsync(ClaimRequest(invocation, owner: "worker-b"), deadline.Token).AsTask());

        Assert.Single(results, result => result.Disposition == RuleWorkClaimDisposition.Claimed);
        Assert.Single(results, result => result.Disposition == RuleWorkClaimDisposition.NoEligibleWork);
        var row = await SnapshotAsync(invocation.Identity);
        Assert.Equal(1, row.WorkFence);
        Assert.Equal(1, row.ModelRevision);
        Assert.Contains(row.WorkOwner, new[] { "worker-a", "worker-b" });
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("stale-fence")]
    [InlineData("wrong-owner")]
    [InlineData("wrong-identity")]
    [InlineData("wrong-fingerprint")]
    [InlineData("cancelled")]
    public async Task Renew_and_release_reject_invalid_leases_without_mutating_the_row(string invalidation)
    {
        var invocation = RuleStoreTestFixture.NewInvocation();
        var clock = new SqliteClock(InitialUtcTicks);
        var store = await AdmitAndCreateStoreAsync(invocation, clock, TimeSpan.FromSeconds(30));
        var claim = await store.ClaimWorkAsync(ClaimRequest(invocation));
        var validLease = Assert.IsType<RuleWorkToken>(claim.Token).Lease;
        var invalidLease = invalidation switch
        {
            "expired" => validLease,
            "stale-fence" => new(validLease.Identity, validLease.Fingerprint, validLease.Owner, validLease.Fence + 1, validLease.ExpiresAt),
            "wrong-owner" => new(validLease.Identity, validLease.Fingerprint, "another-worker", validLease.Fence, validLease.ExpiresAt),
            "wrong-identity" => new(RuleStoreTestFixture.NewInvocation().Identity, validLease.Fingerprint,
                validLease.Owner, validLease.Fence, validLease.ExpiresAt),
            "wrong-fingerprint" => new(validLease.Identity, new RuleInvocationFingerprint(new string('c', 64)),
                validLease.Owner, validLease.Fence, validLease.ExpiresAt),
            "cancelled" => validLease,
            _ => throw new ArgumentOutOfRangeException(nameof(invalidation))
        };

        if (invalidation == "expired")
            clock.Set(validLease.ExpiresAt.UtcDateTime.Ticks + 1);
        else if (invalidation == "cancelled")
            Assert.Equal(RuleCancellationDisposition.Cancelled,
                (await fixture.CreateStore(new RuleStoreTestFixture.FixedAdmissionValidator(RuleAdmissionValidationDisposition.Ready))
                    .CancelAsync(new(invocation))).Disposition);

        var before = await SnapshotAsync(invocation.Identity);
        var renewed = await store.RenewAsync(invalidLease);
        var released = await store.ReleaseAsync(invalidLease);

        Assert.Equal(RuleLeaseDisposition.LeaseLost, renewed.Disposition);
        Assert.Null(renewed.Lease);
        Assert.Equal(RuleLeaseDisposition.LeaseLost, released.Disposition);
        Assert.Null(released.Lease);
        Assert.Equal(before, await SnapshotAsync(invocation.Identity));
    }

    [Fact]
    public async Task Lease_identity_hash_collision_returns_conflict_without_exposing_or_mutating_the_other_row()
    {
        var invocation = RuleStoreTestFixture.NewInvocation();
        var store = await AdmitAndCreateStoreAsync(invocation, new SqliteClock(InitialUtcTicks), TimeSpan.FromSeconds(30));
        var lease = Assert.IsType<RuleWorkToken>((await store.ClaimWorkAsync(ClaimRequest(invocation))).Token).Lease;
        await using (var db = fixture.Contexts.CreateDbContext())
        {
            var row = await db.Runs.SingleAsync();
            row.IdentityBase64 = Convert.ToBase64String(RuleRecordCodec.ScopeBytes(RuleStoreTestFixture.NewInvocation().Identity));
            await db.SaveChangesAsync();
        }
        var before = await SnapshotAsync(invocation.Identity);

        foreach (var result in new[] { await store.RenewAsync(lease), await store.ReleaseAsync(lease) })
        {
            Assert.Equal(RuleLeaseDisposition.Conflict, result.Disposition);
            Assert.Null(result.Lease);
        }
        Assert.Equal(before, await SnapshotAsync(invocation.Identity));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Claim_requires_the_pending_receipt_to_exist_and_fit_the_actual_projection(bool oversized)
    {
        var invocation = RuleStoreTestFixture.NewInvocation();
        await AdmitAsync(invocation);
        await using (var db = fixture.Contexts.CreateDbContext())
        {
            var row = await db.Runs.SingleAsync();
            row.PendingReceiptId64 = new string('c', 64);
            if (oversized)
                db.Receipts.Add(new RuleRoundReceiptRecord
                {
                    RunKey = row.RunKey, Ordinal = 0, ReceiptId64 = row.PendingReceiptId64,
                    InputFactsSha25664 = row.FactsSha25664, ReceiptJson = new string(' ', RuleLimits.MaxEnvelopeBytes + 1),
                    AcceptedAttemptId = "attempt", AcceptedWorkerId = "worker", AcceptedFence = 1,
                    AcceptedModelRevision = row.ModelRevision, SchemaVersion = RulesEfModule.SchemaVersion
                });
            await db.SaveChangesAsync();
        }
        var before = await SnapshotAsync(invocation.Identity);
        var store = CreateStore(new SqliteClock(InitialUtcTicks), TimeSpan.FromSeconds(30));

        var result = await store.ClaimWorkAsync(ClaimRequest(invocation));

        Assert.Equal(RuleWorkClaimDisposition.Unavailable, result.Disposition);
        Assert.Null(result.Token);
        Assert.Equal(before, await SnapshotAsync(invocation.Identity));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Claim_rolls_back_all_lease_fields_if_expiry_passes_or_clock_moves_back_after_save(bool backwards)
    {
        var invocation = RuleStoreTestFixture.NewInvocation();
        await AdmitAsync(invocation);
        var duration = TimeSpan.FromSeconds(5);
        var clock = new SqliteClock(InitialUtcTicks);
        var before = await SnapshotAsync(invocation.Identity);
        var expirer = new ExpireAfterSave(clock, backwards ? InitialUtcTicks - 1 : InitialUtcTicks + duration.Ticks);
        var store = new EfRuleRunStore(
            new RuleStoreTestFixture.SqliteContextFactory(fixture.DatabasePath, [clock, expirer]),
            new RuleStoreTestFixture.FixedAdmissionValidator(RuleAdmissionValidationDisposition.Ready), duration);

        var result = await store.ClaimWorkAsync(ClaimRequest(invocation));

        Assert.Equal(RuleWorkClaimDisposition.Unavailable, result.Disposition);
        Assert.Null(result.Token);
        Assert.Equal(before, await SnapshotAsync(invocation.Identity));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Renew_and_release_roll_back_all_lease_fields_if_expiry_passes_after_save(bool release)
    {
        var invocation = RuleStoreTestFixture.NewInvocation();
        var duration = TimeSpan.FromSeconds(5);
        var clock = new SqliteClock(InitialUtcTicks);
        var store = await AdmitAndCreateStoreAsync(invocation, clock, duration);
        var claim = await store.ClaimWorkAsync(ClaimRequest(invocation));
        var lease = Assert.IsType<RuleWorkToken>(claim.Token).Lease;
        var before = await SnapshotAsync(invocation.Identity);
        clock.Set(InitialUtcTicks);
        var expirer = new ExpireAfterSave(clock, InitialUtcTicks + duration.Ticks + 1);
        var expiringStore = CreateStore(clock, duration, expirer);

        var result = release
            ? await expiringStore.ReleaseAsync(lease)
            : await expiringStore.RenewAsync(lease);

        Assert.Equal(RuleLeaseDisposition.LeaseLost, result.Disposition);
        Assert.Null(result.Lease);
        Assert.Equal(before, await SnapshotAsync(invocation.Identity));
    }

    private async Task AdmitAsync(RuleInvocationEnvelope invocation) =>
        Assert.Equal(RuleAdmissionDisposition.Admitted,
            (await fixture.CreateStore(new RuleStoreTestFixture.FixedAdmissionValidator(RuleAdmissionValidationDisposition.Ready))
                .AdmitAsync(new(invocation))).Disposition);

    private async Task<EfRuleRunStore> AdmitAndCreateStoreAsync(RuleInvocationEnvelope invocation, SqliteClock clock, TimeSpan duration)
    {
        await AdmitAsync(invocation);
        return CreateStore(clock, duration);
    }

    private EfRuleRunStore CreateStore(SqliteClock clock, TimeSpan duration, SaveChangesInterceptor? saveInterceptor = null) =>
        new(new RuleStoreTestFixture.SqliteContextFactory(fixture.DatabasePath,
                saveInterceptor is null ? [clock] : [clock, saveInterceptor]),
            new RuleStoreTestFixture.FixedAdmissionValidator(RuleAdmissionValidationDisposition.Ready), duration);

    private static RuleClaimRequest ClaimRequest(RuleInvocationEnvelope invocation, string owner = "worker",
        IReadOnlyList<RuleSemanticPin>? pins = null, IReadOnlyList<int>? invocationSchemas = null,
        IReadOnlyList<int>? receiptSchemas = null, IReadOnlyList<int>? stateSchemas = null) =>
        new(owner, pins ?? [invocation.ExpectedImplementation], invocationSchemas ?? [invocation.InvocationSchemaVersion],
            receiptSchemas ?? [invocation.ExpectedImplementation.ReceiptSchemaVersion],
            stateSchemas ?? [invocation.ExpectedImplementation.StateSchemaVersion]);

    private async Task<StoredRunSnapshot> SnapshotAsync(RuleRunIdentity identity)
    {
        var key = RuleRecordCodec.Hash(RuleRecordCodec.ScopeBytes(identity));
        await using var db = fixture.Contexts.CreateDbContext();
        var row = await db.Runs.SingleAsync(candidate => candidate.RunKey == key);
        return new(row.IdentityBase64, row.InvocationJson, row.Fingerprint64, row.PinHash64, row.Admitted,
            row.ModelRevision, Convert.ToBase64String(row.ConcurrencyToken), row.FactsJson, row.FactsSha25664,
            row.CommittedRounds, row.PendingReceiptId64, row.Status, row.TerminalId64, row.TerminalJson,
            row.TerminalModelRevision, row.WorkOwner, row.WorkFence, row.WorkExpiresAtUtcTicks,
            row.DeliveryId64, row.DeliveryPayloadJson, row.DeliveryPayloadSha25664, row.DeliveryOwner,
            row.DeliveryFence, row.DeliveryExpiresAtUtcTicks, row.DeliveryAcknowledged,
            row.DeliveryAcknowledgementJson, row.SchemaVersion);
    }

    private static RuleSemanticPin DifferentPin(RuleSemanticPin pin)
    {
        var packages = pin.Packages.Select(package => new RulePackagePin(package.PackageId, package.NuGetVersion,
            package.NuGetContentHash, package.SelectedAssets.Select(asset => new RuleAssetPin(asset.PackageRelativeAssetPath,
                asset.AssemblyFullName, asset.AssetSha256[0] == '0' ? new string('1', 64) : new string('0', 64))).ToArray())).ToArray();
        return new(pin.PinSchemaVersion, packages, pin.LanguageId, pin.LanguageVersion, pin.ReceiptSchemaVersion, pin.StateSchemaVersion);
    }

    private sealed record StoredRunSnapshot(string IdentityBase64, string InvocationJson, string Fingerprint64,
        string PinHash64, bool Admitted, long ModelRevision, string ConcurrencyToken, string FactsJson,
        string FactsSha25664, int CommittedRounds, string? PendingReceiptId64, int Status, string? TerminalId64,
        string? TerminalJson, long? TerminalModelRevision, string? WorkOwner, long WorkFence,
        long? WorkExpiresAtUtcTicks, string? DeliveryId64, string? DeliveryPayloadJson,
        string? DeliveryPayloadSha25664, string? DeliveryOwner, long DeliveryFence,
        long? DeliveryExpiresAtUtcTicks, bool DeliveryAcknowledged, string? DeliveryAcknowledgementJson,
        string SchemaVersion);
}
