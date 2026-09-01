using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.Fixtures;
using Elsa.Foundation.Identity.Persistence.Groundwork;
using Elsa.Foundation.Identity.Persistence.Groundwork.Documents;
using Elsa.Foundation.Identity.Persistence.Groundwork.Exceptions;
using Elsa.Foundation.Identity.Persistence.Groundwork.Stores;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Testing;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Sqlite;
using Groundwork.Store;
using Groundwork.Testing;
using System.Globalization;
using System.Text.Json;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests;

public sealed class AspNetCoreIdentityReconciliationTests
{
    [Fact]
    public async Task One_hundred_concurrent_exact_retries_converge_on_the_authoritative_receipt_outcome()
    {
        using var source = new ControlledSessionSource(LostAcknowledgementMode.None);
        var atomicWrite = AtomicWrite(source);
        var write = UserWrite("exact-retry-race");

        var results = await Task.WhenAll(Enumerable.Range(0, 100).Select(_ =>
            atomicWrite.SaveAsync(write, CancellationToken.None).AsTask()));

        Assert.All(results, result =>
        {
            Assert.True(result.Succeeded);
            Assert.Equal(1, result.Row?.Version);
        });
        Assert.Equal(1, source.Read(write.UnitId, write.Id)?.Version);
    }

    [Fact]
    public async Task Atomic_mutation_commits_multiple_rows_and_returns_the_declared_result()
    {
        using var source = new ControlledSessionSource(LostAcknowledgementMode.None);
        var first = UserWrite("multi-row-first");
        var second = RoleWrite("multi-row-second");
        var mutation = GroundworkIdentityAtomicMutation.Create(
            "save-user-role-pair",
            IdentityRequestFingerprint.FromParts(first.Id, second.Id),
            first.UnitId,
            second.UnitId);

        var result = await AtomicWrite(source).ExecuteAsync(
            mutation,
            (batch, cancellationToken) =>
            {
                Assert.True(batch.Save(first, cancellationToken).Succeeded);
                return Task.FromResult(batch.Save(second, cancellationToken));
            },
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(second.Id, result.Row?.Id);
        Assert.NotNull(source.Read(first.UnitId, first.Id));
        Assert.NotNull(source.Read(second.UnitId, second.Id));
        Assert.Equal(
            new[] { first.UnitId, second.UnitId, IdentityStorageManifest.IdentityMutationReceiptDocumentKind }
                .Order(StringComparer.Ordinal),
            source.State.LastCommitUnitIds.Order(StringComparer.Ordinal));
        Assert.Equal(IdentityStorageManifest.IdentityMutationReceiptDocumentKind, source.State.LastStagedUnitIds.Last());

        var receipt = source.Read(IdentityStorageManifest.IdentityMutationReceiptDocumentKind, mutation.MutationReceiptId);
        Assert.NotNull(receipt);
        using var receiptJson = JsonDocument.Parse(Content(receipt!));
        Assert.True(receiptJson.RootElement.GetProperty("createdAt").GetDateTimeOffset() <
                    receiptJson.RootElement.GetProperty("expiresAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task External_login_receipts_distinguish_authoritative_result_and_ownership_policy()
    {
        using var persistence = new AspNetCoreIdentityTestPersistence();
        var fixture = new AspNetCoreIdentityGroundworkStoreFixture(
            AspNetCoreIdentityScenarioData.Ids.PrimaryTenant,
            persistence);
        var user = AspNetCoreIdentityScenarioData.CreateIdentityUser(AspNetCoreIdentityScenarioData.PrimaryUser);
        Assert.True((await fixture.UserStore().CreateAsync(user, CancellationToken.None)).Succeeded);
        var login = AspNetCoreIdentityScenarioData.Logins.First(value => value.UserId == user.Id);
        var document = new IdentityExternalLoginDocument(
            IdentityCompositeDocumentId.Normalize(login.TenantId),
            IdentityCompositeDocumentId.Normalize(login.UserId),
            IdentityCompositeDocumentId.Normalize(login.LoginProvider),
            IdentityCompositeDocumentId.Normalize(login.ProviderKey),
            IdentityDocumentId.From(login.TenantId, login.LoginProvider, login.ProviderKey),
            login.ProviderDisplayName,
            AspNetCoreIdentityScenarioData.CreateExternalIdentityRecord(login),
            IdentityDocumentId.From(login.TenantId, login.UserId));
        var coordinator = GroundworkIdentityAuthorityRelationshipCoordinator.ForRows(fixture.Rows);
        var initialReceiptCount = ReceiptCount(fixture.Rows);

        var childResult = await coordinator.SaveExternalLoginAsync(
            document, null, null, false, GroundworkExternalLoginOwnershipPolicy.CreateOrSameOwner,
            returnOwnerResult: false, CancellationToken.None);
        var ownerResult = await coordinator.SaveExternalLoginAsync(
            document, null, null, false, GroundworkExternalLoginOwnershipPolicy.CreateOrSameOwner,
            returnOwnerResult: true, CancellationToken.None);
        var rebindResult = await coordinator.SaveExternalLoginAsync(
            document, null, null, false, GroundworkExternalLoginOwnershipPolicy.RevisionEnforcedRebind,
            returnOwnerResult: false, CancellationToken.None);

        Assert.Equal(IdentityStorageManifest.ExternalLoginDocumentKind, childResult.Row?.UnitId);
        Assert.Equal(IdentityStorageManifest.IdentityUserDocumentKind, ownerResult.Row?.UnitId);
        Assert.Equal(IdentityStorageManifest.ExternalLoginDocumentKind, rebindResult.Row?.UnitId);
        Assert.Equal(initialReceiptCount + 3, ReceiptCount(fixture.Rows));
    }

    [Fact]
    public async Task Failure_before_domain_staging_does_not_create_domain_state_or_a_receipt()
    {
        using var source = new ControlledSessionSource(LostAcknowledgementMode.None);
        var write = UserWrite("before-stage-user");
        var mutation = Mutation("before-stage", write);

        await Assert.ThrowsAsync<IOException>(() => AtomicWrite(source).ExecuteAsync(
            mutation,
            (_, _) => throw new IOException("Failure before domain staging."),
            CancellationToken.None).AsTask());

        Assert.Null(source.Read(write.UnitId, write.Id));
        Assert.Null(source.Read(IdentityStorageManifest.IdentityMutationReceiptDocumentKind, mutation.MutationReceiptId));
    }

    [Fact]
    public async Task Failure_after_domain_staging_but_before_commit_rolls_back_domain_state_and_receipt()
    {
        using var source = new ControlledSessionSource(LostAcknowledgementMode.None);
        var write = UserWrite("before-commit-user");
        var mutation = Mutation("before-commit", write);

        await Assert.ThrowsAsync<IOException>(() => AtomicWrite(source).ExecuteAsync(
            mutation,
            (batch, cancellationToken) =>
            {
                Assert.True(batch.Save(write, cancellationToken).Succeeded);
                throw new IOException("Failure before commit.");
            },
            CancellationToken.None).AsTask());

        Assert.Null(source.Read(write.UnitId, write.Id));
        Assert.Null(source.Read(IdentityStorageManifest.IdentityMutationReceiptDocumentKind, mutation.MutationReceiptId));
    }

    [Theory]
    [InlineData(LostAcknowledgementMode.ThrowCancellationAfterCommit)]
    [InlineData(LostAcknowledgementMode.ThrowTransportAfterCommit)]
    public async Task Atomic_write_reconciles_lost_acknowledgement_after_the_row_was_committed(
        LostAcknowledgementMode mode)
    {
        using var source = new ControlledSessionSource(mode);
        var write = UserWrite("reconcile-user");

        var result = await AtomicWrite(source).SaveAsync(write, CancellationToken.None);
        var persisted = source.Read(write.UnitId, write.Id);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Row);
        Assert.Equal(write.CanonicalJson, Content(persisted!));
        Assert.False(source.State.ReceiptWasReadWhileUnitOfWorkActive);
        Assert.True(source.State.SessionOpenCount >= 3);
    }

    [Fact]
    public async Task Authority_aggregate_reconciles_lost_ack_with_owner_and_reservations_committed_once()
    {
        using var source = new ControlledSessionSource(LostAcknowledgementMode.ThrowTransportAfterCommit);
        var coordinator = new GroundworkIdentityAuthorityAggregateCoordinator(AtomicWrite(source));
        var scenario = AspNetCoreIdentityScenarioData.PrimaryUser;
        var document = new IdentityUserDocument(
            IdentityCompositeDocumentId.Normalize(scenario.TenantId),
            IdentityCompositeDocumentId.Normalize(scenario.Id),
            IdentityCompositeDocumentId.Normalize(scenario.NormalizedUserName),
            IdentityCompositeDocumentId.Normalize(scenario.NormalizedEmail),
            IdentityDocumentId.From(scenario.TenantId, scenario.NormalizedUserName),
            IdentityDocumentId.From(scenario.TenantId, scenario.NormalizedEmail),
            AspNetCoreIdentityScenarioData.CreateUserRecord(scenario));

        var result = await coordinator.SaveUserAsync(document, 0, requireUniqueEmail: true, CancellationToken.None);

        Assert.True(result.WriteResult.Succeeded);
        Assert.Equal(1, source.Read(IdentityStorageManifest.IdentityUserDocumentKind,
            IdentityCompositeDocumentId.From(scenario.TenantId, scenario.Id))?.Version);
        Assert.Equal(1, source.Read(IdentityStorageManifest.UserNameReservationDocumentKind,
            IdentityDocumentId.From(scenario.TenantId, scenario.NormalizedUserName))?.Version);
        Assert.Equal(1, source.Read(IdentityStorageManifest.EmailReservationDocumentKind,
            IdentityDocumentId.From(scenario.TenantId, scenario.NormalizedEmail))?.Version);
    }

    [Fact]
    public async Task Lost_acknowledgement_returns_the_committed_result_even_when_the_target_advances_before_reconciliation()
    {
        using var source = new ControlledSessionSource(LostAcknowledgementMode.ThrowTransportAfterCommitAndAdvanceTarget);
        var write = UserWrite("advanced-after-commit-user");

        var result = await AtomicWrite(source).SaveAsync(write, CancellationToken.None);
        var persisted = source.Read(write.UnitId, write.Id);

        Assert.Equal(1, result.Row?.Version);
        Assert.Equal(write.CanonicalJson, result.Row?.CanonicalJson);
        Assert.Equal(2, persisted?.Version);
        Assert.Equal(write.CanonicalJson + Environment.NewLine, Content(persisted!));
    }

    [Fact]
    public async Task Exact_retry_recovers_the_existing_receipt_without_staging_the_domain_write_again()
    {
        using var source = new ControlledSessionSource(LostAcknowledgementMode.None);
        var atomicWrite = AtomicWrite(source);
        var write = UserWrite("exact-retry-user");
        var first = await atomicWrite.SaveAsync(write, CancellationToken.None);
        var beginCount = source.State.BeginCount;

        var replay = await atomicWrite.SaveAsync(write, CancellationToken.None);

        AssertEquivalent(first, replay);
        Assert.Equal(beginCount, source.State.BeginCount);
    }

    [Fact]
    public async Task Active_mutation_receipt_replays_the_exact_outcome_until_expiry()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 7, 16, 10, 0, 0, TimeSpan.Zero));
        using var source = new ControlledSessionSource(LostAcknowledgementMode.None);
        var atomicWrite = AtomicWrite(source, time, receiptLifetime: TimeSpan.FromHours(1));
        var write = UserWrite("active-receipt-user");
        var first = await atomicWrite.SaveAsync(write, CancellationToken.None);
        var beginCount = source.State.BeginCount;

        time.Advance(TimeSpan.FromMinutes(59));
        var replay = await atomicWrite.SaveAsync(write, CancellationToken.None);

        AssertEquivalent(first, replay);
        Assert.Equal(beginCount, source.State.BeginCount);
        Assert.Equal(1, replay.Row?.Version);
    }

    [Fact]
    public async Task Expired_mutation_receipt_is_reclaimed_instead_of_replayed()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 7, 16, 10, 0, 0, TimeSpan.Zero));
        using var source = new ControlledSessionSource(LostAcknowledgementMode.None);
        var atomicWrite = AtomicWrite(source, time, receiptLifetime: TimeSpan.FromHours(1));
        var write = UserWrite("expired-receipt-user");
        var first = await atomicWrite.SaveAsync(write, CancellationToken.None);

        time.Advance(TimeSpan.FromHours(1));
        var afterExpiry = await atomicWrite.SaveAsync(write, CancellationToken.None);

        Assert.Equal(1, first.Row?.Version);
        Assert.Equal(2, afterExpiry.Row?.Version);
        Assert.Equal(1, source.State.DeleteCount);
    }

    [Fact]
    public async Task Opportunistic_cleanup_uses_a_finite_oldest_expired_query_and_is_amortized()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 7, 16, 10, 0, 0, TimeSpan.Zero));
        using var source = new ControlledSessionSource(LostAcknowledgementMode.None);
        var atomicWrite = AtomicWrite(source, time, receiptLifetime: TimeSpan.FromHours(1));
        for (var index = 0; index < 32; index++)
            await atomicWrite.SaveAsync(UserWrite($"cleanup-{index}"), CancellationToken.None);
        Assert.Single(source.State.ReceiptCleanupQueries);

        time.Advance(TimeSpan.FromHours(1));
        await atomicWrite.SaveAsync(UserWrite("cleanup-trigger"), CancellationToken.None);
        await atomicWrite.SaveAsync(UserWrite("cleanup-throttled"), CancellationToken.None);

        Assert.Equal(2, source.State.ReceiptCleanupQueries.Count);
        Assert.Equal(32, source.State.DeleteCount);
        var query = source.State.ReceiptCleanupQueries[^1];
        Assert.Equal(64, query.Paging.Limit);
        Assert.Null(query.Paging.Offset);
        Assert.Equal(IdentityStorageManifest.MutationReceiptExpiresAtField, query.Order[0].Column.Name);
        Assert.Equal(IdentityV2StorageManifest.IdField, query.Order[1].Column.Name);
        Assert.Equal(
            IdentityStorageManifest.MutationReceiptExpiresAtField,
            Assert.IsType<Predicate.Range>(query.Where).Column.Name);
    }

    [Fact]
    public async Task Sustained_mutation_volume_drains_expired_receipt_backlog_at_least_as_fast_as_it_is_created()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 7, 16, 10, 0, 0, TimeSpan.Zero));
        using var source = new ControlledSessionSource(LostAcknowledgementMode.None);
        var atomicWrite = AtomicWrite(source, time, receiptLifetime: TimeSpan.FromHours(1));
        for (var index = 0; index < 96; index++)
            await atomicWrite.SaveAsync(UserWrite($"backlog-seed-{index}"), CancellationToken.None);

        time.Advance(TimeSpan.FromHours(1));
        for (var index = 0; index < 33; index++)
            await atomicWrite.SaveAsync(UserWrite($"backlog-drain-{index}"), CancellationToken.None);

        Assert.Equal(0, ExpiredReceiptCount(source.Rows, time.GetUtcNow()));
        Assert.Equal(96, source.State.DeleteCount);
        Assert.All(source.State.ReceiptCleanupQueries, query => Assert.Equal(64, query.Paging.Limit));
    }

    [Fact]
    public async Task Concurrent_cleaners_tolerate_a_stale_delete_without_corrupting_mutations()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 7, 16, 10, 0, 0, TimeSpan.Zero));
        using var source = new ControlledSessionSource(LostAcknowledgementMode.None, useSqlite: true);
        await AtomicWrite(source, time, receiptLifetime: TimeSpan.FromHours(1))
            .SaveAsync(UserWrite("expired-before-cleanup-race"), CancellationToken.None);
        time.Advance(TimeSpan.FromHours(1));
        source.State.ReceiptCleanupBarrier = new AsyncBarrier(2);

        var results = await Task.WhenAll(
            Task.Run(async () => await AtomicWrite(source, time).SaveAsync(UserWrite("cleanup-race-first"), CancellationToken.None)),
            Task.Run(async () => await AtomicWrite(source, time).SaveAsync(UserWrite("cleanup-race-second"), CancellationToken.None)));

        Assert.All(results, result => Assert.True(result.Succeeded));
        Assert.NotNull(source.Read(IdentityStorageManifest.IdentityUserDocumentKind,
            IdentityCompositeDocumentId.From("tenant-alpha", "cleanup-race-first")));
        Assert.NotNull(source.Read(IdentityStorageManifest.IdentityUserDocumentKind,
            IdentityCompositeDocumentId.From("tenant-alpha", "cleanup-race-second")));
    }

    [Fact]
    public async Task Cleanup_provider_failure_prevents_the_authority_write_and_remains_truthful()
    {
        using var source = new ControlledSessionSource(LostAcknowledgementMode.None);
        source.State.ReceiptCleanupFailure = new IOException("The cleanup query failed.");
        var write = UserWrite("cleanup-failure");

        var exception = await Assert.ThrowsAsync<GroundworkIdentityStoreException>(() =>
            AtomicWrite(source).SaveAsync(write, CancellationToken.None).AsTask());

        Assert.Contains("identity_mutation_receipt_by_expiry", exception.Message, StringComparison.Ordinal);
        var providerFailure = Assert.IsType<IOException>(exception.InnerException);
        Assert.Equal("The cleanup query failed.", providerFailure.Message);
        Assert.Equal(0, source.State.BeginCount);
        Assert.Null(source.Read(write.UnitId, write.Id));
    }

    [Fact]
    public async Task Cleanup_cancellation_prevents_the_authority_write()
    {
        using var source = new ControlledSessionSource(LostAcknowledgementMode.None);
        source.State.BlockReceiptCleanup = true;
        var write = UserWrite("cleanup-cancellation");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            AtomicWrite(source).SaveAsync(write, cancellation.Token).AsTask());

        Assert.Equal(0, source.State.BeginCount);
        Assert.Null(source.Read(write.UnitId, write.Id));
    }

    [Fact]
    public async Task Atomic_write_propagates_not_committed_failures_without_creating_the_row()
    {
        using var source = new ControlledSessionSource(LostAcknowledgementMode.ThrowBeforeCommit);
        var write = UserWrite("not-committed-user");

        await Assert.ThrowsAsync<IOException>(() => AtomicWrite(source).SaveAsync(write, CancellationToken.None).AsTask());

        Assert.Null(source.Read(write.UnitId, write.Id));
        Assert.Null(source.Read(IdentityStorageManifest.IdentityMutationReceiptDocumentKind,
            Mutation("save-row", write).MutationReceiptId));
    }

    [Fact]
    public async Task Atomic_write_does_not_reconcile_an_equivalent_external_write_without_the_request_token()
    {
        using var source = new ControlledSessionSource(LostAcknowledgementMode.ThrowBeforeCommitAfterEquivalentExternalWrite);
        var write = UserWrite("external-equivalent-user");

        await Assert.ThrowsAsync<IOException>(() => AtomicWrite(source).SaveAsync(write, CancellationToken.None).AsTask());

        var persisted = source.Read(write.UnitId, write.Id);
        Assert.Equal(write.CanonicalJson, Content(persisted!));
    }

    [Theory]
    [InlineData(LostAcknowledgementMode.ReturnMalformedReceiptAfterCommit)]
    [InlineData(LostAcknowledgementMode.ThrowReceiptReadAfterCommit)]
    [InlineData(LostAcknowledgementMode.TimeoutReceiptReadAfterCommit)]
    public async Task Unclassifiable_reconciliation_throws_the_dedicated_bounded_uncertain_commit_exception(
        LostAcknowledgementMode mode)
    {
        using var source = new ControlledSessionSource(mode);
        var write = UserWrite($"uncertain-{mode}");
        var atomicWrite = AtomicWrite(source, reconciliationTimeout: TimeSpan.FromSeconds(5));

        var exception = await Assert.ThrowsAsync<GroundworkIdentityUncertainCommitException>(() =>
            atomicWrite.SaveAsync(write, CancellationToken.None).AsTask());

        Assert.Contains("uncertain commit outcome", exception.Message, StringComparison.Ordinal);
        Assert.True(source.State.CommitAcknowledgementLost);
        Assert.NotNull(source.Read(write.UnitId, write.Id));
    }

    private static GroundworkIdentityAtomicWrite AtomicWrite(
        ControlledSessionSource source,
        TimeProvider? time = null,
        TimeSpan? reconciliationTimeout = null,
        TimeSpan? receiptLifetime = null) =>
        new(source.Rows, time, reconciliationTimeout, receiptLifetime);

    private static GroundworkIdentityRowWrite UserWrite(string userId)
    {
        var content = $$"""
            {
              "tenantId": "tenant-alpha",
              "userId": "{{userId}}",
              "normalizedUserName": "{{userId.ToUpperInvariant()}}",
              "normalizedEmail": null,
              "normalizedUserNameKey": "identity:test-{{userId}}",
              "normalizedEmailKey": null,
              "user": {
                "id": "{{userId}}",
                "tenantId": "tenant-alpha",
                "userName": "{{userId}}",
                "email": null,
                "displayName": "{{userId}}",
                "status": "Active",
                "ownership": "Foundation",
                "roleIds": [],
                "directPermissions": []
              }
            }
            """;
        return new GroundworkIdentityRowWrite(
            IdentityStorageManifest.IdentityUserDocumentKind,
            IdentityCompositeDocumentId.From("tenant-alpha", userId),
            content,
            new Dictionary<string, object?>
            {
                [IdentityStorageManifest.NormalizedUserNameKeyField] = $"identity:test-{userId}",
                [IdentityStorageManifest.NormalizedEmailKeyField] = null
            },
            GroundworkIdentityRowWriteCondition.Unconditional);
    }

    private static GroundworkIdentityRowWrite RoleWrite(string roleId)
    {
        var content = $$"""
            {
              "tenantId": "tenant-alpha",
              "roleId": "{{roleId}}",
              "normalizedRoleName": "{{roleId.ToUpperInvariant()}}",
              "normalizedRoleNameKey": "identity:test-{{roleId}}",
              "role": {
                "id": "{{roleId}}",
                "tenantId": "tenant-alpha",
                "name": "{{roleId}}",
                "displayName": "{{roleId}}",
                "permissions": [],
                "system": false
              }
            }
            """;
        return new GroundworkIdentityRowWrite(
            IdentityStorageManifest.IdentityRoleDocumentKind,
            IdentityCompositeDocumentId.From("tenant-alpha", roleId),
            content,
            new Dictionary<string, object?>
            {
                [IdentityStorageManifest.NormalizedRoleNameKeyField] = $"identity:test-{roleId}",
                [IdentityStorageManifest.TenantIdField] = "tenant-alpha"
            },
            GroundworkIdentityRowWriteCondition.Unconditional);
    }

    private static GroundworkIdentityAtomicMutation Mutation(string operationName, GroundworkIdentityRowWrite write) =>
        GroundworkIdentityAtomicMutation.Create(
            operationName,
            IdentityRequestFingerprint.FromParts(
                write.UnitId,
                write.Id,
                IdentityStorageManifest.SchemaVersion,
                write.CanonicalJson,
                write.Condition.Kind.ToString(),
                write.Condition.ExpectedVersion?.ToString(CultureInfo.InvariantCulture)),
            write.UnitId);

    private static int ReceiptCount(GroundworkIdentityRowStore rows) => ReceiptCount(rows, DateTimeOffset.MaxValue);

    private static int ExpiredReceiptCount(GroundworkIdentityRowStore rows, DateTimeOffset cutoff) => ReceiptCount(rows, cutoff);

    private static int ReceiptCount(GroundworkIdentityRowStore rows, DateTimeOffset cutoff) => rows.Query(
        IdentityStorageManifest.IdentityMutationReceiptDocumentKind,
        new GroundworkIdentityRowQuery(
            IdentityStorageManifest.MutationReceiptExpiresAtField,
            GroundworkIdentityRowComparison.LessThanOrEqual,
            cutoff,
            IdentityStorageManifest.MutationReceiptExpiresAtField,
            Take: 100_000)).Count;

    private static string Content(StoredEntry entry) => entry.Values.Values[IdentityV2StorageManifest.ContentField] switch
    {
        string text => text,
        JsonElement element => element.GetRawText(),
        _ => throw new InvalidDataException("The Identity test row did not contain JSON content.")
    };

    private static void AssertEquivalent(GroundworkIdentityWriteResult expected, GroundworkIdentityWriteResult actual)
    {
        Assert.Equal(expected.Status, actual.Status);
        Assert.Equal(expected.Version, actual.Version);
        Assert.Equal(expected.Message, actual.Message);
        Assert.Equal(expected.AuthoritativeId, actual.AuthoritativeId);
        Assert.Equal(expected.Row?.UnitId, actual.Row?.UnitId);
        Assert.Equal(expected.Row?.Id, actual.Row?.Id);
        Assert.Equal(expected.Row?.Version, actual.Row?.Version);
        Assert.Equal(expected.Row?.CanonicalJson, actual.Row?.CanonicalJson);
        Assert.Equal(expected.Row?.ProjectedValues, actual.Row?.ProjectedValues);
    }

    public enum LostAcknowledgementMode
    {
        None,
        ThrowBeforeCommit,
        ThrowBeforeCommitAfterEquivalentExternalWrite,
        ThrowCancellationAfterCommit,
        ThrowTransportAfterCommit,
        ThrowTransportAfterCommitAndAdvanceTarget,
        ReturnMalformedReceiptAfterCommit,
        ThrowReceiptReadAfterCommit,
        TimeoutReceiptReadAfterCommit
    }

    private sealed class FixedAccessContextAccessor : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } =
            PersistenceAccessContext.Scoped(new PersistenceScope("tenant-alpha"));
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset current = utcNow;
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(TimeSpan duration) => current = current.Add(duration);
    }

    private sealed class ControlledSessionSource : IGroundworkStorageSessionSource, IDisposable
    {
        private readonly IReadOnlyDictionary<string, StorageUnit> units = IdentityV2StorageManifest.CreateUnits()
            .ToDictionary(unit => unit.Id.Value, StringComparer.Ordinal);
        private readonly IStorageProviderConnection connection;
        private readonly StorageAccess access = StorageAccess.Scoped(new StorageScope("tenant-alpha"));
        private readonly string? databasePath;

        public ControlledSessionSource(LostAcknowledgementMode mode, bool useSqlite = false)
        {
            State = new ControlledSessionState(mode);
            if (useSqlite)
            {
                databasePath = Path.Combine(Path.GetTempPath(), $"identity-reconciliation-{Guid.NewGuid():N}.db");
                connection = new SqliteProviderFactory().Create($"Data Source={databasePath}");
            }
            else
            {
                connection = new InMemoryProviderFactory().Create($"identity-reconciliation:{Guid.NewGuid():N}");
            }
            foreach (var unit in units.Values)
                connection.Schema.Apply(unit);
            Rows = new GroundworkIdentityRowStore(this, new FixedAccessContextAccessor());
        }

        public ControlledSessionState State { get; }
        public GroundworkIdentityRowStore Rows { get; }

        public IStorageSession Open(string unitId, StorageAccess requestedAccess, string? targetName = null)
        {
            State.SessionOpenCount++;
            return new ControlledStorageSession(connection.OpenSession(Unit(unitId), requestedAccess), State);
        }

        public IUnitOfWork BeginUnitOfWork(
            StorageAccess requestedAccess,
            BatchWriteOptions options,
            IReadOnlyList<string> unitIds,
            string? targetName = null)
        {
            State.BeginCount++;
            State.ActiveUnitOfWorkCount++;
            State.LastCommitUnitIds = unitIds.ToArray();
            return new ControlledUnitOfWork(
                connection.BeginUnitOfWork(requestedAccess, options, unitIds.Select(unitId => Unit(unitId)).ToArray()),
                this,
                State);
        }

        public StorageUnit Unit(string unitId, string? targetName = null) => units[unitId];

        public StoredEntry? Read(string unitId, string id) => connection.OpenSession(Unit(unitId), access).Read(Key(id));

        public void Execute(RowWrite write)
        {
            var session = connection.OpenSession(write.Unit, access);
            _ = write.Mode switch
            {
                RowWriteMode.Insert => session.Insert(write.Values!, write.Options),
                RowWriteMode.Update => session.Update(write.Values!, write.Options),
                RowWriteMode.Upsert or RowWriteMode.ConditionalUpsert => session.Upsert(write.Values!, write.Options),
                RowWriteMode.Delete => session.Delete(write.Key!, write.Options),
                _ => throw new ArgumentOutOfRangeException(nameof(write.Mode))
            };
        }

        public void Advance(RowWrite write)
        {
            var values = write.Values!.Values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            values[IdentityV2StorageManifest.ContentField] = (string)values[IdentityV2StorageManifest.ContentField]! + Environment.NewLine;
            var outcome = connection.OpenSession(write.Unit, access).Upsert(
                new StorageValues(values),
                WriteOptions.IfVersion(1));
            Assert.True(outcome.Succeeded);
        }

        public void Dispose()
        {
            State.CleanupRelease.TrySetResult();
            State.ReceiptReadRelease.Set();
            connection.Dispose();
            if (databasePath is not null)
            {
                if (File.Exists(databasePath))
                    File.Delete(databasePath);
                if (File.Exists(databasePath + ".schema.lock"))
                    File.Delete(databasePath + ".schema.lock");
            }
        }

        private static StorageKey Key(string id) => new(new Dictionary<string, object?>
        {
            [IdentityV2StorageManifest.IdField] = id
        });
    }

    private sealed class ControlledStorageSession(IStorageSession inner, ControlledSessionState state) : SynchronousStorageSessionTestDouble, IStorageSession,
        IConcurrencyStorageSession
    {
        public StorageUnit Unit => inner.Unit;
        public StorageAccess Access => inner.Access;

        public StoredEntry? Read(StorageKey key)
        {
            if (Unit.Id.Value != IdentityStorageManifest.IdentityMutationReceiptDocumentKind ||
                !state.CommitAcknowledgementLost)
                return inner.Read(key);

            state.ReceiptWasReadWhileUnitOfWorkActive |= state.ActiveUnitOfWorkCount > 0;
            return state.Mode switch
            {
                LostAcknowledgementMode.ThrowReceiptReadAfterCommit =>
                    throw new IOException("The mutation receipt read failed."),
                LostAcknowledgementMode.TimeoutReceiptReadAfterCommit => BlockReceiptRead(),
                LostAcknowledgementMode.ReturnMalformedReceiptAfterCommit => Malformed(inner.Read(key)),
                _ => inner.Read(key)
            };
        }

        public QueryMaterializedResult Query(QueryRequest request, QueryRenderOptions? options = null)
        {
            if (Unit.Id.Value == IdentityStorageManifest.IdentityMutationReceiptDocumentKind &&
                request.Paging.Limit == 64)
            {
                state.ReceiptCleanupQueries.Add(request);
                if (state.ReceiptCleanupFailure is { } failure)
                    throw failure;
                if (state.BlockReceiptCleanup)
                    state.CleanupRelease.Task.GetAwaiter().GetResult();
            }

            var result = inner.Query(request, options);
            if (Unit.Id.Value == IdentityStorageManifest.IdentityMutationReceiptDocumentKind &&
                request.Paging.Limit == 64 &&
                state.ReceiptCleanupBarrier is { } barrier)
                barrier.SignalAndWait();
            return result;
        }

        public AggregationResult Aggregate(AggregationQuery query) => inner.Aggregate(query);
        public WriteOutcome Insert(StorageValues values, WriteOptions? options = null) => inner.Insert(values, options);
        public WriteOutcome Update(StorageValues values, WriteOptions? options = null) => inner.Update(values, options);
        public WriteOutcome Upsert(StorageValues values, WriteOptions? options = null) => inner.Upsert(values, options);
        public WriteOutcome ConditionalUpsert(StorageValues values, WriteOptions? options = null) =>
            Assert.IsAssignableFrom<IConcurrencyStorageSession>(inner).ConditionalUpsert(values, options);

        public WriteOutcome Delete(StorageKey key, WriteOptions? options = null)
        {
            var outcome = inner.Delete(key, options);
            if (Unit.Id.Value == IdentityStorageManifest.IdentityMutationReceiptDocumentKind &&
                outcome.Status == WriteOutcomeStatus.Deleted)
                Interlocked.Increment(ref state.DeleteCount);
            return outcome;
        }

        public WriteOutcome Append(OperationId operationId, IReadOnlyList<StorageValues> values) =>
            inner.Append(operationId, values);

        private static StoredEntry? Malformed(StoredEntry? entry)
        {
            if (entry is null)
                return null;
            var values = entry.Values.Values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            values[IdentityV2StorageManifest.ContentField] = "{";
            return new StoredEntry(new StorageValues(values), entry.Version);
        }

        private StoredEntry? BlockReceiptRead()
        {
            state.ReceiptReadRelease.Wait();
            return null;
        }
    }

    private sealed class ControlledUnitOfWork(
        IUnitOfWork inner,
        ControlledSessionSource source,
        ControlledSessionState state) : IUnitOfWork
    {
        private readonly List<RowWrite> staged = [];
        private bool committed;
        private bool disposed;

        public IStorageSession OpenSession(StorageUnit unit) => inner.OpenSession(unit);

        public void Stage(RowWrite write)
        {
            staged.Add(write);
            inner.Stage(write);
        }

        public BatchWriteSummary Commit() => CommitCore(inner.Commit);
        public BatchWriteReport CommitWithOutcomes() => CommitCore(inner.CommitWithOutcomes);
        public ValueTask<BatchWriteReport> CommitWithOutcomesAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(CommitWithOutcomes());
        public ValueTask<BatchWriteSummary> CommitAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Commit());

        public void Rollback()
        {
            if (!committed)
                inner.Rollback();
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            try
            {
                inner.Dispose();
            }
            finally
            {
                state.ActiveUnitOfWorkCount--;
            }
        }

        private T CommitCore<T>(Func<T> commit)
        {
            state.LastStagedUnitIds = staged.Select(write => write.Unit.Id.Value).ToArray();
            if (state.Mode == LostAcknowledgementMode.ThrowBeforeCommit)
                throw new IOException("The provider failed before the write was committed.");
            if (state.Mode == LostAcknowledgementMode.ThrowBeforeCommitAfterEquivalentExternalWrite)
            {
                source.Execute(DomainWrite());
                throw new IOException("The provider failed before this request was committed, but another actor wrote equivalent content.");
            }

            var result = commit();
            committed = true;
            if (state.Mode == LostAcknowledgementMode.ThrowTransportAfterCommitAndAdvanceTarget)
                source.Advance(DomainWrite());

            if (ThrowsAfterCommit(state.Mode))
                state.CommitAcknowledgementLost = true;
            if (state.Mode == LostAcknowledgementMode.ThrowCancellationAfterCommit)
                throw new OperationCanceledException("The caller lost acknowledgement after the write was committed.");
            if (ThrowsAfterCommit(state.Mode))
                throw new IOException("The provider lost acknowledgement after the write was committed.");
            return result;
        }

        private RowWrite DomainWrite() => staged.First(write =>
            write.Unit.Id.Value != IdentityStorageManifest.IdentityMutationReceiptDocumentKind);

        private static bool ThrowsAfterCommit(LostAcknowledgementMode mode) => mode is
            LostAcknowledgementMode.ThrowCancellationAfterCommit or
            LostAcknowledgementMode.ThrowTransportAfterCommit or
            LostAcknowledgementMode.ThrowTransportAfterCommitAndAdvanceTarget or
            LostAcknowledgementMode.ReturnMalformedReceiptAfterCommit or
            LostAcknowledgementMode.ThrowReceiptReadAfterCommit or
            LostAcknowledgementMode.TimeoutReceiptReadAfterCommit;
    }

    private sealed class ControlledSessionState(LostAcknowledgementMode mode)
    {
        public LostAcknowledgementMode Mode { get; } = mode;
        public int SessionOpenCount { get; set; }
        public int BeginCount { get; set; }
        public int ActiveUnitOfWorkCount { get; set; }
        public bool CommitAcknowledgementLost { get; set; }
        public bool ReceiptWasReadWhileUnitOfWorkActive { get; set; }
        public IReadOnlyList<string> LastCommitUnitIds { get; set; } = [];
        public IReadOnlyList<string> LastStagedUnitIds { get; set; } = [];
        public List<QueryRequest> ReceiptCleanupQueries { get; } = [];
        public AsyncBarrier? ReceiptCleanupBarrier { get; set; }
        public Exception? ReceiptCleanupFailure { get; set; }
        public bool BlockReceiptCleanup { get; set; }
        public TaskCompletionSource CleanupRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim ReceiptReadRelease { get; } = new();
        public int DeleteCount;
    }

    private sealed class AsyncBarrier(int participants)
    {
        private readonly ManualResetEventSlim released = new();
        private int remaining = participants;

        public void SignalAndWait()
        {
            if (Interlocked.Decrement(ref remaining) == 0)
                released.Set();
            released.Wait();
        }
    }
}
