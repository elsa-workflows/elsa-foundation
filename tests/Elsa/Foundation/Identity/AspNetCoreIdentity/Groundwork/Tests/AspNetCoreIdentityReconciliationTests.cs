using System.IO;
using System.Text.Json;
using Elsa.Foundation.Identity.Persistence.Groundwork;
using Elsa.Foundation.Identity.Persistence.Groundwork.Exceptions;
using Elsa.Foundation.Identity.Persistence.Groundwork.Documents;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.Fixtures;
using Elsa.Foundation.Identity.Persistence.Groundwork.Stores;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Scoping;
using Elsa.Persistence.Groundwork.Testing;
using Groundwork.Core.Queries;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests;

public sealed class AspNetCoreIdentityReconciliationTests
{
    [Fact]
    public async Task One_hundred_concurrent_exact_retries_converge_on_the_authoritative_receipt_outcome()
    {
        var source = new ControlledSessionSource(LostAcknowledgementMode.None);
        var atomicWrite = new GroundworkIdentityAtomicWrite(SessionFactory(source));
        var request = SaveUserRequest("exact-retry-race");

        var results = await Task.WhenAll(Enumerable.Range(0, 100).Select(_ =>
            atomicWrite.SaveAsync(request, CancellationToken.None).AsTask()));

        Assert.All(results, result =>
        {
            Assert.Equal(DocumentStoreWriteStatus.Saved, result.Status);
            Assert.Equal(1, result.Document?.Version);
        });
        Assert.Equal(1, (await source.Inner.LoadAsync(request.DocumentKind, request.Id, CancellationToken.None))?.Version);
    }

    [Fact]
    public async Task Atomic_mutation_commits_multiple_documents_and_returns_the_declared_result()
    {
        var source = new ControlledSessionSource(LostAcknowledgementMode.None);
        var atomicWrite = new GroundworkIdentityAtomicWrite(SessionFactory(source));
        var first = SaveUserRequest("multi-document-first");
        var second = SaveRoleRequest("multi-document-second");
        var mutation = GroundworkIdentityAtomicMutation.Create(
            "save-user-role-pair",
            IdentityRequestFingerprint.FromParts(first.Id, second.Id),
            first.DocumentKind,
            second.DocumentKind);

        var result = await atomicWrite.ExecuteAsync(
            mutation,
            async (unitOfWork, cancellationToken) =>
            {
                Assert.Equal(DocumentStoreWriteStatus.Saved, (await unitOfWork.SaveAsync(first, cancellationToken)).Status);
                return await unitOfWork.SaveAsync(second, cancellationToken);
            },
            CancellationToken.None);

        Assert.Equal(DocumentStoreWriteStatus.Saved, result.Status);
        Assert.Equal(second.Id, result.Document?.Id);
        Assert.NotNull(await source.Inner.LoadAsync(first.DocumentKind, first.Id, CancellationToken.None));
        Assert.NotNull(await source.Inner.LoadAsync(second.DocumentKind, second.Id, CancellationToken.None));
        Assert.Equal(
            new[]
            {
                first.DocumentKind,
                second.DocumentKind,
                IdentityStorageManifest.IdentityMutationReceiptDocumentKind
            }.Order(StringComparer.Ordinal),
            source.State.LastCommitScope?.Kinds);
        Assert.Equal(
            IdentityStorageManifest.IdentityMutationReceiptDocumentKind,
            source.State.LastStagedDocumentKinds.Last());

        var receipt = await source.Inner.LoadAsync(
            IdentityStorageManifest.IdentityMutationReceiptDocumentKind,
            mutation.MutationReceiptId,
            CancellationToken.None);
        Assert.NotNull(receipt);
        using var receiptJson = JsonDocument.Parse(receipt!.ContentJson);
        Assert.True(receiptJson.RootElement.GetProperty("createdAt").GetDateTimeOffset() <
                    receiptJson.RootElement.GetProperty("expiresAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task External_login_receipts_distinguish_authoritative_result_and_ownership_policy()
    {
        var fixture = new AspNetCoreIdentityGroundworkStoreFixture(AspNetCoreIdentityScenarioData.Ids.PrimaryTenant);
        var user = AspNetCoreIdentityScenarioData.CreateIdentityUser(AspNetCoreIdentityScenarioData.PrimaryUser);
        Assert.True((await fixture.UserStore().CreateAsync(user, CancellationToken.None)).Succeeded);
        var source = AspNetCoreIdentityScenarioData.Logins.First(login => login.UserId == user.Id);
        var document = new IdentityExternalLoginDocument(
            IdentityCompositeDocumentId.Normalize(source.TenantId),
            IdentityCompositeDocumentId.Normalize(source.UserId),
            IdentityCompositeDocumentId.Normalize(source.LoginProvider),
            IdentityCompositeDocumentId.Normalize(source.ProviderKey),
            IdentityDocumentId.From(source.TenantId, source.LoginProvider, source.ProviderKey),
            source.ProviderDisplayName,
            AspNetCoreIdentityScenarioData.CreateExternalIdentityRecord(source),
            IdentityDocumentId.From(source.TenantId, source.UserId));
        var coordinator = GroundworkIdentityAuthorityRelationshipCoordinator.ForDirectStore(fixture.Documents);
        var initialReceiptCount = fixture.Documents.Snapshot(IdentityStorageManifest.IdentityMutationReceiptDocumentKind).Count;

        var childResult = await coordinator.SaveExternalLoginAsync(
            document, expectedNewOwnerVersion: null, expectedLoginVersion: null,
            enforceLoginVersion: false,
            ownershipPolicy: GroundworkExternalLoginOwnershipPolicy.CreateOrSameOwner,
            returnOwnerResult: false,
            CancellationToken.None);
        var ownerResult = await coordinator.SaveExternalLoginAsync(
            document, expectedNewOwnerVersion: null, expectedLoginVersion: null,
            enforceLoginVersion: false,
            ownershipPolicy: GroundworkExternalLoginOwnershipPolicy.CreateOrSameOwner,
            returnOwnerResult: true,
            CancellationToken.None);
        var rebindPolicyResult = await coordinator.SaveExternalLoginAsync(
            document, expectedNewOwnerVersion: null, expectedLoginVersion: null,
            enforceLoginVersion: false,
            ownershipPolicy: GroundworkExternalLoginOwnershipPolicy.RevisionEnforcedRebind,
            returnOwnerResult: false,
            CancellationToken.None);

        Assert.Equal(IdentityStorageManifest.ExternalLoginDocumentKind, childResult.Document?.DocumentKind);
        Assert.Equal(IdentityStorageManifest.IdentityUserDocumentKind, ownerResult.Document?.DocumentKind);
        Assert.Equal(IdentityStorageManifest.ExternalLoginDocumentKind, rebindPolicyResult.Document?.DocumentKind);
        Assert.Equal(
            initialReceiptCount + 3,
            fixture.Documents.Snapshot(IdentityStorageManifest.IdentityMutationReceiptDocumentKind).Count);
    }

    [Fact]
    public async Task Failure_before_domain_staging_does_not_create_domain_state_or_a_receipt()
    {
        var source = new ControlledSessionSource(LostAcknowledgementMode.None);
        var atomicWrite = new GroundworkIdentityAtomicWrite(SessionFactory(source));
        var request = SaveUserRequest("before-stage-user");
        var mutation = Mutation("before-stage", request);

        await Assert.ThrowsAsync<IOException>(() => atomicWrite.ExecuteAsync(
            mutation,
            (_, _) => throw new IOException("Failure before domain staging."),
            CancellationToken.None).AsTask());

        Assert.Null(await source.Inner.LoadAsync(request.DocumentKind, request.Id, CancellationToken.None));
        Assert.Null(await LoadReceiptAsync(source, mutation));
    }

    [Fact]
    public async Task Failure_after_domain_staging_but_before_commit_rolls_back_domain_state_and_receipt()
    {
        var source = new ControlledSessionSource(LostAcknowledgementMode.None);
        var atomicWrite = new GroundworkIdentityAtomicWrite(SessionFactory(source));
        var request = SaveUserRequest("before-commit-user");
        var mutation = Mutation("before-commit", request);

        await Assert.ThrowsAsync<IOException>(() => atomicWrite.ExecuteAsync(
            mutation,
            async (unitOfWork, cancellationToken) =>
            {
                Assert.Equal(DocumentStoreWriteStatus.Saved, (await unitOfWork.SaveAsync(request, cancellationToken)).Status);
                throw new IOException("Failure before commit.");
            },
            CancellationToken.None).AsTask());

        Assert.Null(await source.Inner.LoadAsync(request.DocumentKind, request.Id, CancellationToken.None));
        Assert.Null(await LoadReceiptAsync(source, mutation));
    }

    [Theory]
    [InlineData(LostAcknowledgementMode.ThrowCancellationAfterCommit)]
    [InlineData(LostAcknowledgementMode.ThrowTransportAfterCommit)]
    public async Task Atomic_write_reconciles_lost_acknowledgement_after_the_document_was_committed(
        LostAcknowledgementMode mode)
    {
        var source = new ControlledSessionSource(mode);
        var atomicWrite = new GroundworkIdentityAtomicWrite(SessionFactory(source));
        var request = SaveUserRequest("reconcile-user");

        var result = await atomicWrite.SaveAsync(request, CancellationToken.None);
        var persisted = await source.Inner.LoadAsync(request.DocumentKind, request.Id, CancellationToken.None);

        Assert.Equal(DocumentStoreWriteStatus.Saved, result.Status);
        Assert.NotNull(result.Document);
        Assert.NotNull(persisted);
        Assert.Equal(request.ContentJson, persisted!.ContentJson);
        Assert.False(source.State.ReceiptWasReadWhileUnitOfWorkActive);
        Assert.True(source.State.SessionOpenCount >= 3);
    }

    [Fact]
    public async Task Authority_aggregate_reconciles_lost_ack_with_owner_and_reservations_committed_once()
    {
        var source = new ControlledSessionSource(LostAcknowledgementMode.ThrowTransportAfterCommit);
        var coordinator = new GroundworkIdentityAuthorityAggregateCoordinator(
            new GroundworkIdentityAtomicWrite(SessionFactory(source)));
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

        Assert.Equal(DocumentStoreWriteStatus.Saved, result.WriteResult.Status);
        Assert.Equal(1, (await source.Inner.LoadAsync(
            IdentityStorageManifest.IdentityUserDocumentKind,
            IdentityCompositeDocumentId.From(scenario.TenantId, scenario.Id),
            CancellationToken.None))?.Version);
        Assert.Equal(1, (await source.Inner.LoadAsync(
            IdentityStorageManifest.UserNameReservationDocumentKind,
            IdentityDocumentId.From(scenario.TenantId, scenario.NormalizedUserName),
            CancellationToken.None))?.Version);
        Assert.Equal(1, (await source.Inner.LoadAsync(
            IdentityStorageManifest.EmailReservationDocumentKind,
            IdentityDocumentId.From(scenario.TenantId, scenario.NormalizedEmail),
            CancellationToken.None))?.Version);
    }

    [Fact]
    public async Task Lost_acknowledgement_returns_the_committed_result_even_when_the_target_advances_before_reconciliation()
    {
        var source = new ControlledSessionSource(LostAcknowledgementMode.ThrowTransportAfterCommitAndAdvanceTarget);
        var atomicWrite = new GroundworkIdentityAtomicWrite(SessionFactory(source));
        var request = SaveUserRequest("advanced-after-commit-user");

        var result = await atomicWrite.SaveAsync(request, CancellationToken.None);
        var persisted = await source.Inner.LoadAsync(request.DocumentKind, request.Id, CancellationToken.None);

        Assert.Equal(1, result.Document?.Version);
        Assert.Equal(request.ContentJson, result.Document?.ContentJson);
        Assert.Equal(2, persisted?.Version);
        Assert.Equal(request.ContentJson + Environment.NewLine, persisted?.ContentJson);
    }

    [Fact]
    public async Task Exact_retry_recovers_the_existing_receipt_without_staging_the_domain_write_again()
    {
        var source = new ControlledSessionSource(LostAcknowledgementMode.None);
        var atomicWrite = new GroundworkIdentityAtomicWrite(SessionFactory(source));
        var request = SaveUserRequest("exact-retry-user");
        var first = await atomicWrite.SaveAsync(request, CancellationToken.None);
        var beginCount = source.State.BeginCount;

        var replay = await atomicWrite.SaveAsync(request, CancellationToken.None);

        Assert.Equal(first, replay);
        Assert.Equal(beginCount, source.State.BeginCount);
    }

    [Fact]
    public async Task Active_mutation_receipt_replays_the_exact_outcome_until_expiry()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 7, 16, 10, 0, 0, TimeSpan.Zero));
        var source = new ControlledSessionSource(LostAcknowledgementMode.None);
        var atomicWrite = new GroundworkIdentityAtomicWrite(
            SessionFactory(source),
            time,
            receiptLifetime: TimeSpan.FromHours(1));
        var request = SaveUserRequest("active-receipt-user");
        var first = await atomicWrite.SaveAsync(request, CancellationToken.None);
        var beginCount = source.State.BeginCount;

        time.Advance(TimeSpan.FromMinutes(59));
        var replay = await atomicWrite.SaveAsync(request, CancellationToken.None);

        Assert.Equal(first, replay);
        Assert.Equal(beginCount, source.State.BeginCount);
        Assert.Equal(1, replay.Document?.Version);
    }

    [Fact]
    public async Task Expired_mutation_receipt_is_reclaimed_instead_of_replayed()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 7, 16, 10, 0, 0, TimeSpan.Zero));
        var source = new ControlledSessionSource(LostAcknowledgementMode.None);
        var atomicWrite = new GroundworkIdentityAtomicWrite(
            SessionFactory(source),
            time,
            receiptLifetime: TimeSpan.FromHours(1));
        var request = SaveUserRequest("expired-receipt-user");
        var first = await atomicWrite.SaveAsync(request, CancellationToken.None);

        time.Advance(TimeSpan.FromHours(1));
        var afterExpiry = await atomicWrite.SaveAsync(request, CancellationToken.None);

        Assert.Equal(1, first.Document?.Version);
        Assert.Equal(2, afterExpiry.Document?.Version);
        Assert.Equal(1, source.Inner.DeleteCount);
    }

    [Fact]
    public async Task Opportunistic_cleanup_uses_a_finite_oldest_expired_query_and_is_amortized()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 7, 16, 10, 0, 0, TimeSpan.Zero));
        var source = new ControlledSessionSource(LostAcknowledgementMode.None);
        var atomicWrite = new GroundworkIdentityAtomicWrite(
            SessionFactory(source),
            time,
            receiptLifetime: TimeSpan.FromHours(1));

        for (var index = 0; index < 32; index++)
            await atomicWrite.SaveAsync(SaveUserRequest($"cleanup-{index}"), CancellationToken.None);
        Assert.Single(source.State.ReceiptCleanupQueries);

        time.Advance(TimeSpan.FromHours(1));
        var sessionsBeforeCleanup = source.State.SessionOpenCount;
        await atomicWrite.SaveAsync(SaveUserRequest("cleanup-trigger"), CancellationToken.None);
        Assert.Equal(2, source.State.SessionOpenCount - sessionsBeforeCleanup);
        await atomicWrite.SaveAsync(SaveUserRequest("cleanup-throttled"), CancellationToken.None);

        Assert.Equal(2, source.State.ReceiptCleanupQueries.Count);
        Assert.Equal(32, source.Inner.DeleteCount);
        var query = source.State.ReceiptCleanupQueries[^1];
        Assert.Equal(IdentityStorageManifest.IdentityMutationReceiptDocumentKind, query.DocumentKind);
        Assert.Equal(IdentityStorageManifest.ListExpiredMutationReceiptsQuery, query.QueryIdentity);
        Assert.Equal(64, query.Take);
        Assert.Null(query.Skip);
        Assert.Equal(IdentityStorageManifest.MutationReceiptExpiresAtField, Assert.Single(query.Order).Path);
        var comparison = Assert.Single(Assert.Single(query.Clauses).Comparisons);
        Assert.Equal(IdentityStorageManifest.MutationReceiptExpiresAtField, comparison.Path);
        Assert.Equal(QueryComparisonOperator.LessThanOrEqual, comparison.Operator);
    }

    [Fact]
    public async Task Sustained_mutation_volume_drains_expired_receipt_backlog_at_least_as_fast_as_it_is_created()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 7, 16, 10, 0, 0, TimeSpan.Zero));
        var source = new ControlledSessionSource(LostAcknowledgementMode.None);
        var atomicWrite = new GroundworkIdentityAtomicWrite(
            SessionFactory(source),
            time,
            receiptLifetime: TimeSpan.FromHours(1));
        for (var index = 0; index < 96; index++)
            await atomicWrite.SaveAsync(SaveUserRequest($"backlog-seed-{index}"), CancellationToken.None);

        time.Advance(TimeSpan.FromHours(1));
        for (var index = 0; index < 33; index++)
            await atomicWrite.SaveAsync(SaveUserRequest($"backlog-drain-{index}"), CancellationToken.None);

        var expired = source.Inner.Snapshot(IdentityStorageManifest.IdentityMutationReceiptDocumentKind)
            .Count(envelope => ReceiptExpiry(envelope) <= time.GetUtcNow());
        Assert.Equal(0, expired);
        Assert.Equal(96, source.Inner.DeleteCount);
        Assert.All(source.State.ReceiptCleanupQueries, query => Assert.Equal(64, query.Take));
    }

    [Fact]
    public async Task Concurrent_cleaners_tolerate_a_stale_delete_without_corrupting_mutations()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 7, 16, 10, 0, 0, TimeSpan.Zero));
        var source = new ControlledSessionSource(LostAcknowledgementMode.None);
        var seedWriter = new GroundworkIdentityAtomicWrite(
            SessionFactory(source),
            time,
            receiptLifetime: TimeSpan.FromHours(1));
        await seedWriter.SaveAsync(SaveUserRequest("expired-before-cleanup-race"), CancellationToken.None);
        time.Advance(TimeSpan.FromHours(1));
        source.State.ReceiptCleanupBarrier = new AsyncBarrier(2);
        var firstCleaner = new GroundworkIdentityAtomicWrite(SessionFactory(source), time);
        var secondCleaner = new GroundworkIdentityAtomicWrite(SessionFactory(source), time);

        var results = await Task.WhenAll(
            firstCleaner.SaveAsync(SaveUserRequest("cleanup-race-first"), CancellationToken.None).AsTask(),
            secondCleaner.SaveAsync(SaveUserRequest("cleanup-race-second"), CancellationToken.None).AsTask());

        Assert.All(results, result => Assert.Equal(DocumentStoreWriteStatus.Saved, result.Status));
        Assert.NotNull(await source.Inner.LoadAsync(
            IdentityStorageManifest.IdentityUserDocumentKind,
            IdentityCompositeDocumentId.From("tenant-alpha", "cleanup-race-first"),
            CancellationToken.None));
        Assert.NotNull(await source.Inner.LoadAsync(
            IdentityStorageManifest.IdentityUserDocumentKind,
            IdentityCompositeDocumentId.From("tenant-alpha", "cleanup-race-second"),
            CancellationToken.None));
    }

    [Fact]
    public async Task Cleanup_provider_failure_prevents_the_authority_write_and_remains_truthful()
    {
        var source = new ControlledSessionSource(LostAcknowledgementMode.None);
        source.State.ReceiptCleanupFailure = new IOException("The cleanup query failed.");
        var atomicWrite = new GroundworkIdentityAtomicWrite(SessionFactory(source));
        var request = SaveUserRequest("cleanup-provider-failure");

        var exception = await Assert.ThrowsAsync<IOException>(() =>
            atomicWrite.SaveAsync(request, CancellationToken.None).AsTask());

        Assert.Equal("The cleanup query failed.", exception.Message);
        Assert.Equal(0, source.State.BeginCount);
        Assert.Null(await source.Inner.LoadAsync(request.DocumentKind, request.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Cleanup_cancellation_prevents_the_authority_write()
    {
        var source = new ControlledSessionSource(LostAcknowledgementMode.None);
        source.State.BlockReceiptCleanup = true;
        var atomicWrite = new GroundworkIdentityAtomicWrite(SessionFactory(source));
        var request = SaveUserRequest("cleanup-cancellation");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            atomicWrite.SaveAsync(request, cancellation.Token).AsTask());

        Assert.Equal(0, source.State.BeginCount);
        Assert.Null(await source.Inner.LoadAsync(request.DocumentKind, request.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Atomic_write_propagates_not_committed_failures_without_creating_the_document()
    {
        var source = new ControlledSessionSource(LostAcknowledgementMode.ThrowBeforeCommit);
        var atomicWrite = new GroundworkIdentityAtomicWrite(SessionFactory(source));
        var request = SaveUserRequest("not-committed-user");

        await Assert.ThrowsAsync<IOException>(() => atomicWrite.SaveAsync(request, CancellationToken.None).AsTask());

        Assert.Null(await source.Inner.LoadAsync(request.DocumentKind, request.Id, CancellationToken.None));
        Assert.Null(await LoadReceiptAsync(source, Mutation("save-document", request)));
    }

    [Fact]
    public async Task Atomic_write_does_not_reconcile_an_equivalent_external_write_without_the_request_token()
    {
        var source = new ControlledSessionSource(LostAcknowledgementMode.ThrowBeforeCommitAfterEquivalentExternalWrite);
        var atomicWrite = new GroundworkIdentityAtomicWrite(SessionFactory(source));
        var request = SaveUserRequest("external-equivalent-user");

        await Assert.ThrowsAsync<IOException>(() => atomicWrite.SaveAsync(request, CancellationToken.None).AsTask());

        var persisted = await source.Inner.LoadAsync(request.DocumentKind, request.Id, CancellationToken.None);
        Assert.NotNull(persisted);
        Assert.Equal(request.ContentJson, persisted!.ContentJson);
    }

    [Theory]
    [InlineData(LostAcknowledgementMode.ReturnMalformedReceiptAfterCommit)]
    [InlineData(LostAcknowledgementMode.ThrowReceiptReadAfterCommit)]
    [InlineData(LostAcknowledgementMode.TimeoutReceiptReadAfterCommit)]
    public async Task Unclassifiable_reconciliation_throws_the_dedicated_bounded_uncertain_commit_exception(
        LostAcknowledgementMode mode)
    {
        var source = new ControlledSessionSource(mode);
        var atomicWrite = new GroundworkIdentityAtomicWrite(
            SessionFactory(source),
            reconciliationTimeout: TimeSpan.FromMilliseconds(50));
        var request = SaveUserRequest($"uncertain-{mode}");

        var exception = await Assert.ThrowsAsync<GroundworkIdentityUncertainCommitException>(() =>
            atomicWrite.SaveAsync(request, CancellationToken.None).AsTask());

        Assert.Contains("uncertain commit outcome", exception.Message, StringComparison.Ordinal);
        Assert.NotNull(await source.Inner.LoadAsync(request.DocumentKind, request.Id, CancellationToken.None));
    }

    private static GroundworkStoreSessionFactory SessionFactory(ControlledSessionSource source) =>
        new(new FixedAccessContextAccessor(), source);

    private static SaveDocumentRequest SaveUserRequest(string userId)
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
        return new SaveDocumentRequest(
            IdentityStorageManifest.IdentityUserDocumentKind,
            IdentityCompositeDocumentId.From("tenant-alpha", userId),
            IdentityStorageManifest.SchemaVersion,
            content);
    }

    private static GroundworkIdentityAtomicMutation Mutation(string operationName, SaveDocumentRequest request) =>
        GroundworkIdentityAtomicMutation.Create(
            operationName,
            IdentityRequestFingerprint.FromParts(
                request.DocumentKind,
                request.Id,
                request.SchemaVersion,
                request.ContentJson,
                request.ExpectedVersion?.ToString()),
            request.DocumentKind);

    private static SaveDocumentRequest SaveRoleRequest(string roleId)
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
        return new SaveDocumentRequest(
            IdentityStorageManifest.IdentityRoleDocumentKind,
            IdentityCompositeDocumentId.From("tenant-alpha", roleId),
            IdentityStorageManifest.SchemaVersion,
            content);
    }

    private static Task<DocumentEnvelope?> LoadReceiptAsync(
        ControlledSessionSource source,
        GroundworkIdentityAtomicMutation mutation) =>
        source.Inner.LoadAsync(
            IdentityStorageManifest.IdentityMutationReceiptDocumentKind,
            mutation.MutationReceiptId,
            CancellationToken.None);

    private static DateTimeOffset ReceiptExpiry(DocumentEnvelope envelope)
    {
        using var json = JsonDocument.Parse(envelope.ContentJson);
        return json.RootElement.GetProperty(IdentityStorageManifest.MutationReceiptExpiresAtField).GetDateTimeOffset();
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
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }

    private sealed class ControlledSessionSource(LostAcknowledgementMode mode) : IGroundworkStoreSessionSource
    {
        private InMemoryDocumentStore? _inner;

        public ControlledSessionState State { get; } = new(mode);

        public InMemoryDocumentStore Inner =>
            _inner ?? throw new InvalidOperationException("The session source has not been opened.");

        public ValueTask<GroundworkStoreSessionResources> OpenAsync(
            DocumentStoreAccess access,
            CancellationToken cancellationToken = default)
        {
            _inner ??= new InMemoryDocumentStore(IdentityStorageManifest.Create(), access);
            State.SessionOpenCount++;
            var store = new ControlledDocumentStore(_inner, State);
            return ValueTask.FromResult(new GroundworkStoreSessionResources(store, store));
        }
    }

    private sealed class ControlledDocumentStore(
        InMemoryDocumentStore inner,
        ControlledSessionState state) : IDocumentStore, IBoundedDocumentStore
    {
        public DocumentStoreAccess Access => inner.Access;

        public TransactionBoundary TransactionBoundary => inner.TransactionBoundary;

        public async Task<DocumentStoreWriteResult> SaveAsync(
            SaveDocumentRequest request,
            CancellationToken cancellationToken = default) =>
            await inner.SaveAsync(request, cancellationToken);

        public async Task<DocumentEnvelope?> LoadAsync(
            string documentKind,
            string id,
            CancellationToken cancellationToken = default)
        {
            if (documentKind != IdentityStorageManifest.IdentityMutationReceiptDocumentKind ||
                !state.CommitAcknowledgementLost)
                return await inner.LoadAsync(documentKind, id, cancellationToken);

            state.ReceiptWasReadWhileUnitOfWorkActive |= state.ActiveUnitOfWorkCount > 0;
            switch (state.Mode)
            {
                case LostAcknowledgementMode.ThrowReceiptReadAfterCommit:
                    throw new IOException("The mutation receipt read failed.");
                case LostAcknowledgementMode.TimeoutReceiptReadAfterCommit:
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    return null;
                case LostAcknowledgementMode.ReturnMalformedReceiptAfterCommit:
                    var receipt = await inner.LoadAsync(documentKind, id, cancellationToken);
                    return receipt is null ? null : receipt with { ContentJson = "{" };
                default:
                    return await inner.LoadAsync(documentKind, id, cancellationToken);
            }
        }

        public Task<DocumentStoreWriteResult> DeleteAsync(DeleteDocumentRequest request, CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(request, cancellationToken);

#pragma warning disable GW0004 // Legacy interface members are required by the controllable store wrapper.
        public Task<IReadOnlyList<DocumentEnvelope>> QueryAsync(DocumentStoreQuery query, CancellationToken cancellationToken = default) =>
            inner.QueryAsync(query, cancellationToken);

        public Task<DocumentQueryResult> QueryAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            inner.QueryAsync(query, cancellationToken);

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            inner.FirstOrDefaultAsync(query, cancellationToken);

        public Task<bool> AnyAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            inner.AnyAsync(query, cancellationToken);
#pragma warning restore GW0004

        public Task<DocumentQueryResult> QueryAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            query.QueryIdentity == IdentityStorageManifest.ListExpiredMutationReceiptsQuery
                ? QueryExpiredReceiptsAsync(query, cancellationToken)
                : inner.QueryAsync(query, cancellationToken);

        private async Task<DocumentQueryResult> QueryExpiredReceiptsAsync(
            DocumentQuery query,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            state.ReceiptCleanupQueries.Add(query);
            if (state.ReceiptCleanupFailure is { } failure)
                throw failure;
            if (state.BlockReceiptCleanup)
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            var comparison = Assert.Single(Assert.Single(query.Clauses).Comparisons);
            var cutoff = DateTimeOffset.Parse(
                Assert.Single(comparison.Values)!,
                System.Globalization.CultureInfo.InvariantCulture);
            var matches = inner.Snapshot(IdentityStorageManifest.IdentityMutationReceiptDocumentKind)
                .Where(envelope => AspNetCoreIdentityReconciliationTests.ReceiptExpiry(envelope) <= cutoff)
                .OrderBy(AspNetCoreIdentityReconciliationTests.ReceiptExpiry)
                .ThenBy(envelope => envelope.Id, StringComparer.Ordinal)
                .ToArray();
            if (state.ReceiptCleanupBarrier is { } barrier)
                await barrier.SignalAndWaitAsync(cancellationToken);
            var window = matches.Skip(query.Skip ?? 0);
            if (query.Take is { } take)
                window = window.Take(take);
            return new DocumentQueryResult(window.ToArray(), matches.Length);
        }

        public Task<long> CountAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            inner.CountAsync(query, cancellationToken);

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            inner.FirstOrDefaultAsync(query, cancellationToken);

        public Task<bool> AnyAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            inner.AnyAsync(query, cancellationToken);

        public async Task<IDocumentUnitOfWork> BeginAsync(DocumentCommitScope scope, CancellationToken cancellationToken = default)
        {
            state.BeginCount++;
            state.ActiveUnitOfWorkCount++;
            state.LastCommitScope = scope;
            return new ControlledDocumentUnitOfWork(await inner.BeginAsync(scope, cancellationToken), inner, state);
        }
    }

    private sealed class ControlledDocumentUnitOfWork(
        IDocumentUnitOfWork inner,
        InMemoryDocumentStore store,
        ControlledSessionState state) : IDocumentUnitOfWork
    {
        private readonly List<SaveDocumentRequest> _stagedSaves = [];
        private bool _disposed;

        public async Task<DocumentStoreWriteResult> SaveAsync(
            SaveDocumentRequest request,
            CancellationToken cancellationToken = default)
        {
            _stagedSaves.Add(request);
            return await inner.SaveAsync(request, cancellationToken);
        }

        public Task<DocumentStoreWriteResult> DeleteAsync(
            DeleteDocumentRequest request,
            CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(request, cancellationToken);

        public Task<DocumentEnvelope?> LoadAsync(
            string documentKind,
            string id,
            CancellationToken cancellationToken = default) =>
            inner.LoadAsync(documentKind, id, cancellationToken);

        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            state.LastStagedDocumentKinds = _stagedSaves.Select(save => save.DocumentKind).ToArray();
            if (state.Mode == LostAcknowledgementMode.ThrowBeforeCommit)
                throw new IOException("The provider failed before the write was committed.");
            if (state.Mode == LostAcknowledgementMode.ThrowBeforeCommitAfterEquivalentExternalWrite)
            {
                var request = _stagedSaves.FirstOrDefault(save =>
                    save.DocumentKind != IdentityStorageManifest.IdentityMutationReceiptDocumentKind);
                if (request is not null)
                    await store.SaveAsync(request, cancellationToken);
                throw new IOException("The provider failed before this request was committed, but another actor wrote equivalent content.");
            }

            await inner.CommitAsync(cancellationToken);
            if (state.Mode == LostAcknowledgementMode.ThrowTransportAfterCommitAndAdvanceTarget)
            {
                var request = _stagedSaves.First(save =>
                    save.DocumentKind != IdentityStorageManifest.IdentityMutationReceiptDocumentKind);
                var committed = await store.LoadAsync(request.DocumentKind, request.Id, cancellationToken);
                Assert.NotNull(committed);
                var advance = await store.SaveAsync(
                    request with
                    {
                        ContentJson = request.ContentJson + Environment.NewLine,
                        ExpectedVersion = committed!.Version
                    },
                    cancellationToken);
                Assert.Equal(DocumentStoreWriteStatus.Saved, advance.Status);
            }

            if (ThrowsAfterCommit(state.Mode))
                state.CommitAcknowledgementLost = true;
            if (state.Mode == LostAcknowledgementMode.ThrowCancellationAfterCommit)
                throw new OperationCanceledException("The caller lost acknowledgement after the write was committed.", cancellationToken);
            if (ThrowsAfterCommit(state.Mode))
                throw new IOException("The provider lost acknowledgement after the write was committed.");
        }

        public Task RollbackAsync(CancellationToken cancellationToken = default) =>
            inner.RollbackAsync(cancellationToken);

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;

            _disposed = true;
            try
            {
                await inner.DisposeAsync();
            }
            finally
            {
                state.ActiveUnitOfWorkCount--;
            }
        }

        private static bool ThrowsAfterCommit(LostAcknowledgementMode mode) =>
            mode is LostAcknowledgementMode.ThrowCancellationAfterCommit or
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
        public DocumentCommitScope? LastCommitScope { get; set; }
        public IReadOnlyList<string> LastStagedDocumentKinds { get; set; } = [];
        public List<DocumentQuery> ReceiptCleanupQueries { get; } = [];
        public AsyncBarrier? ReceiptCleanupBarrier { get; set; }
        public Exception? ReceiptCleanupFailure { get; set; }
        public bool BlockReceiptCleanup { get; set; }
    }

    private sealed class AsyncBarrier(int participants)
    {
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _remaining = participants;

        public Task SignalAndWaitAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Decrement(ref _remaining) == 0)
                _released.TrySetResult();
            return _released.Task.WaitAsync(cancellationToken);
        }
    }
}
