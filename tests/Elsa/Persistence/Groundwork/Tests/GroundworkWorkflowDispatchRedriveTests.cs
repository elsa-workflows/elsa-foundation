using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Services;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Persistence.Core;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.Queries;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

public sealed class GroundworkWorkflowDispatchRedriveTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 16, 14, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task EligibleDetachedFailure_RedrivesAtomicallyAndSurvivesProviderRecreation(string provider)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create(provider);
        var seed = await SeedFailureAsync(fixture, WorkflowDispatchMode.FireAndForget);
        var request = new WorkflowDispatchRedriveRequest(seed.Dispatch.DispatchId, "redrive-1", Now.AddMinutes(1));

        var result = await seed.Outbox.RedriveAsync(request);

        Assert.Equal(WorkflowDispatchRedriveDisposition.Accepted, result.Disposition);
        Assert.Equal(WorkflowDispatchStatus.Pending, result.Status);
        Assert.Equal(1, result.Generation);
        Assert.Equal(seed.Identity.DeliveryIncidentId(0), result.IncidentId);
        Assert.Equal(seed.Start.OutboxItemId, result.DeadLetterId);

        var recovered = Stores(fixture, seed.Access);
        var dispatch = Assert.IsType<WorkflowDispatchRecord>(await recovered.Dispatches.FindAsync(seed.Dispatch.DispatchId));
        var start = Assert.IsType<RuntimePostCommitOutboxItem>(await recovered.Outbox.FindAsync(seed.Start.OutboxItemId));
        Assert.Equal(WorkflowDispatchStatus.Pending, dispatch.Status);
        Assert.Equal(1, WorkflowDispatchLifecycle.ReadDeliveryGeneration(dispatch));
        Assert.Equal(request.RequestId, WorkflowDispatchLifecycle.ReadDeliveryRedriveRequestId(dispatch));
        Assert.Null(WorkflowDispatchLifecycle.ReadDeliveryIncidentId(dispatch));
        Assert.Null(WorkflowDispatchLifecycle.ReadDeliveryDeadLetterId(dispatch));
        Assert.Equal(RuntimePostCommitOutboxStatus.Pending, start.Status);
        Assert.Equal(0, start.DeliveryAttemptCount);
        Assert.Null(start.LastFailureMessage);
        Assert.Equal(request.RequestedAt, start.AvailableAt);
        Assert.True(start.DeliveryFencingToken > seed.Claim.FencingToken);
        Assert.Equal(seed.Start.Intent.IntentId, start.Intent.IntentId);
        Assert.Equal(seed.Start.Intent.IdempotencyKey, start.Intent.IdempotencyKey);
        Assert.Equal(seed.Start.Intent.Payload?.GetRawText(), start.Intent.Payload?.GetRawText());
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task RetentionSnapshotCapturedBeforeRedrive_CannotDeleteReopenedDispatch(string provider)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create(provider);
        var seed = await SeedFailureAsync(fixture, WorkflowDispatchMode.FireAndForget);
        var terminalSnapshot = Assert.IsType<WorkflowDispatchRecord>(
            await seed.Dispatches.FindAsync(seed.Dispatch.DispatchId));
        await seed.Outbox.RedriveAsync(new WorkflowDispatchRedriveRequest(
            seed.Dispatch.DispatchId,
            "redrive-retention-race",
            Now.AddMinutes(1)));

        var deleted = await seed.Dispatches.TryDeleteAsync(terminalSnapshot);
        var dispatch = Assert.IsType<WorkflowDispatchRecord>(
            await seed.Dispatches.FindAsync(seed.Dispatch.DispatchId));

        Assert.False(deleted);
        Assert.Equal(WorkflowDispatchStatus.Pending, dispatch.Status);
        Assert.Equal(1, WorkflowDispatchLifecycle.ReadDeliveryGeneration(dispatch));
        Assert.Equal("redrive-retention-race", WorkflowDispatchLifecycle.ReadDeliveryRedriveRequestId(dispatch));
        Assert.Equal(
            RuntimePostCommitOutboxStatus.Pending,
            (await seed.Outbox.FindAsync(seed.Start.OutboxItemId))!.Status);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task SameRequestIsIdempotent_AndDistinctActiveRequestConflicts(string provider)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create(provider);
        var seed = await SeedFailureAsync(fixture, WorkflowDispatchMode.FireAndForget);
        var request = new WorkflowDispatchRedriveRequest(seed.Dispatch.DispatchId, "redrive-same", Now.AddMinutes(1));

        var accepted = await seed.Outbox.RedriveAsync(request);
        var replay = await seed.Outbox.RedriveAsync(request);
        var conflict = await seed.Outbox.RedriveAsync(new WorkflowDispatchRedriveRequest(
            seed.Dispatch.DispatchId,
            "redrive-other",
            Now.AddMinutes(2)));

        Assert.Equal(WorkflowDispatchRedriveDisposition.Accepted, accepted.Disposition);
        Assert.Equal(WorkflowDispatchRedriveDisposition.AlreadyApplied, replay.Disposition);
        Assert.Equal(WorkflowDispatchRedriveDisposition.ActiveConflict, conflict.Disposition);
        Assert.Equal(1, replay.Generation);
        Assert.Equal(1, conflict.Generation);
        var persisted = Assert.IsType<WorkflowDispatchRecord>(await seed.Dispatches.FindAsync(seed.Dispatch.DispatchId));
        Assert.Equal("redrive-same", WorkflowDispatchLifecycle.ReadDeliveryRedriveRequestId(persisted));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task WaitFailureIsPermanentlyIneligible(string provider)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create(provider);
        var seed = await SeedFailureAsync(fixture, WorkflowDispatchMode.WaitForCompletion);

        var result = await seed.Outbox.RedriveAsync(new WorkflowDispatchRedriveRequest(
            seed.Dispatch.DispatchId,
            "redrive-wait",
            Now.AddMinutes(1)));

        Assert.Equal(WorkflowDispatchRedriveDisposition.NotEligible, result.Disposition);
        Assert.Equal(WorkflowDispatchStatus.DispatchFailed, (await seed.Dispatches.FindAsync(seed.Dispatch.DispatchId))!.Status);
        Assert.Equal(RuntimePostCommitOutboxStatus.FailedFinal, (await seed.Outbox.FindAsync(seed.Start.OutboxItemId))!.Status);
        Assert.NotNull(await seed.Outbox.FindAsync(seed.Identity.WaitFailureResumeOutboxItemId(0)));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task TenantMismatchFailsClosedWithoutMutation(string provider)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create(provider);
        var tenantA = GroundworkTestAccess.AccessContext("tenant-a");
        var seed = await SeedFailureAsync(
            fixture,
            WorkflowDispatchMode.FireAndForget,
            tenantId: "tenant-a",
            access: tenantA);
        var wrongTenant = Stores(fixture, GroundworkTestAccess.AccessContext("tenant-b"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => wrongTenant.Outbox.RedriveAsync(
            new WorkflowDispatchRedriveRequest(seed.Dispatch.DispatchId, "redrive-cross-tenant", Now.AddMinutes(1))).AsTask());

        Assert.Equal(WorkflowDispatchStatus.DispatchFailed, (await seed.Dispatches.FindAsync(seed.Dispatch.DispatchId))!.Status);
        Assert.Equal(RuntimePostCommitOutboxStatus.FailedFinal, (await seed.Outbox.FindAsync(seed.Start.OutboxItemId))!.Status);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task RedriveAdvancesFenceAndRejectsStalePreRedriveClaim(string provider)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create(provider);
        var seed = await SeedFailureAsync(fixture, WorkflowDispatchMode.FireAndForget);
        var requestedAt = Now.AddMinutes(1);

        await seed.Outbox.RedriveAsync(new WorkflowDispatchRedriveRequest(
            seed.Dispatch.DispatchId,
            "redrive-fence",
            requestedAt));
        var newClaim = Assert.Single(await seed.Outbox.ClaimAsync(
            new RuntimePostCommitOutboxClaimRequest("new-owner", requestedAt, TimeSpan.FromMinutes(1), 1)));

        Assert.True(newClaim.FencingToken > seed.Claim.FencingToken);
        await Assert.ThrowsAsync<InvalidOperationException>(() => seed.Outbox.RecordDeliveryResultAsync(
            seed.Claim,
            new RuntimePostCommitOutboxDeliveryResult(
                seed.Start.OutboxItemId,
                RuntimePostCommitOutboxStatus.FailedFinal,
                requestedAt.AddSeconds(1),
                "safe stale failure")).AsTask());
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivering, (await seed.Outbox.FindAsync(seed.Start.OutboxItemId))!.Status);
        Assert.Equal(newClaim.FencingToken, (await seed.Outbox.FindAsync(seed.Start.OutboxItemId))!.DeliveryFencingToken);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task MissingDeadLetterReturnsNotEligibleWithoutPartialDispatchMutation(string provider)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create(provider);
        var stores = Stores(fixture, GroundworkTestAccess.DefaultAccessContextAccessor);
        var pending = NewDispatch(WorkflowDispatchMode.FireAndForget);
        var failed = WorkflowDispatchLifecycle.TransitionToDispatchFailed(
            pending,
            "missing-dead-letter",
            0,
            1,
            Now,
            Now.AddSeconds(1));
        await stores.Dispatches.SaveAsync(pending);
        await stores.Dispatches.SaveAsync(failed);

        var result = await stores.Outbox.RedriveAsync(new WorkflowDispatchRedriveRequest(
            pending.DispatchId,
            "redrive-missing",
            Now.AddMinutes(1)));

        Assert.Equal(WorkflowDispatchRedriveDisposition.NotEligible, result.Disposition);
        var persisted = Assert.IsType<WorkflowDispatchRecord>(await stores.Dispatches.FindAsync(pending.DispatchId));
        Assert.True(WorkflowDispatchLifecycle.RecordsEqual(failed, persisted));
        Assert.Null(await stores.Outbox.FindAsync("missing-dead-letter"));
    }

    [Fact]
    public async Task DispatchWriteFailureRollsBackAlreadyStagedOutboxRedrive()
    {
        var inner = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.Create());
        var failing = new DispatchSaveFailingDocumentStore(inner);
        await using var fixture = new GroundworkDocumentStoreFixture(failing, inner);
        var seed = await SeedFailureAsync(fixture, WorkflowDispatchMode.FireAndForget);
        failing.FailNextDispatchSave = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => seed.Outbox.RedriveAsync(
            new WorkflowDispatchRedriveRequest(seed.Dispatch.DispatchId, "redrive-rollback", Now.AddMinutes(1))).AsTask());

        var recovered = Stores(fixture, seed.Access);
        var dispatch = Assert.IsType<WorkflowDispatchRecord>(await recovered.Dispatches.FindAsync(seed.Dispatch.DispatchId));
        var deadLetter = Assert.IsType<RuntimePostCommitOutboxItem>(await recovered.Outbox.FindAsync(seed.Start.OutboxItemId));
        Assert.Equal(WorkflowDispatchStatus.DispatchFailed, dispatch.Status);
        Assert.Equal(0, WorkflowDispatchLifecycle.ReadDeliveryGeneration(dispatch));
        Assert.Null(WorkflowDispatchLifecycle.ReadDeliveryRedriveRequestId(dispatch));
        Assert.Equal(RuntimePostCommitOutboxStatus.FailedFinal, deadLetter.Status);
        Assert.Equal(seed.Claim.FencingToken, deadLetter.DeliveryFencingToken);
    }

    [Fact]
    public async Task OneHundredConcurrentDuplicateRequestsConvergeOnOneGeneration()
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create("sqlite");
        var seed = await SeedFailureAsync(fixture, WorkflowDispatchMode.FireAndForget);
        var request = new WorkflowDispatchRedriveRequest(seed.Dispatch.DispatchId, "redrive-race", Now.AddMinutes(1));
        var contenders = Enumerable.Range(0, 100)
            .Select(_ => Stores(fixture, seed.Access).Outbox.RedriveAsync(request).AsTask())
            .ToArray();

        var results = await Task.WhenAll(contenders);

        Assert.Single(results, result => result.Disposition == WorkflowDispatchRedriveDisposition.Accepted);
        Assert.Equal(99, results.Count(result => result.Disposition == WorkflowDispatchRedriveDisposition.AlreadyApplied));
        Assert.All(results, result => Assert.Equal(1, result.Generation));
        var persisted = Assert.IsType<WorkflowDispatchRecord>(await seed.Dispatches.FindAsync(seed.Dispatch.DispatchId));
        Assert.Equal(1, WorkflowDispatchLifecycle.ReadDeliveryGeneration(persisted));
        Assert.Equal("redrive-race", WorkflowDispatchLifecycle.ReadDeliveryRedriveRequestId(persisted));
    }

    [Fact]
    public void GroundworkRegistrationExposesScopedRedriveCapability()
    {
        var services = new ServiceCollection();

        services.AddGroundworkRuntimeStores();

        var descriptor = Assert.Single(services, item => item.ServiceType == typeof(IWorkflowDispatchRedriveStore));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.NotNull(descriptor.ImplementationFactory);
    }

    private static async Task<SeededFailure> SeedFailureAsync(
        GroundworkDocumentStoreFixture fixture,
        WorkflowDispatchMode mode,
        string? tenantId = null,
        IPersistenceAccessContextAccessor? access = null)
    {
        access ??= GroundworkTestAccess.DefaultAccessContextAccessor;
        var stores = Stores(fixture, access);
        var dispatch = NewDispatch(mode, tenantId);
        var identity = Identity(dispatch);
        var start = new RuntimePostCommitOutboxItem(
            $"outbox:{dispatch.ParentActivityExecutionId}",
            new RuntimePostCommitIntent(
                identity.StartIntentId,
                dispatch.ParentWorkflowExecutionId,
                DispatchWorkflowConstants.StartChildIntentKind,
                Now.AddMinutes(-1),
                dispatch.ParentActivityExecutionId,
                identity.StartIdempotencyKey,
                System.Text.Json.JsonSerializer.SerializeToElement(new { safe = "payload" }),
                new Dictionary<string, string>
                {
                    [RuntimeMetadataKeys.DispatchId] = dispatch.DispatchId,
                    [RuntimeMetadataKeys.ChildWorkflowExecutionId] = dispatch.ChildWorkflowExecutionId
                }),
            RuntimePostCommitOutboxStatus.Pending,
            Now.AddMinutes(-1),
            Now,
            new RuntimePostCommitRetryPolicy(1, TimeSpan.FromSeconds(1)));
        await stores.Dispatches.SaveAsync(dispatch);
        await stores.Outbox.SavePendingAsync(start);
        var claim = Assert.Single(await stores.Outbox.ClaimAsync(
            new RuntimePostCommitOutboxClaimRequest("failure-owner", Now, TimeSpan.FromMinutes(1), 1)));
        var failure = new RuntimePostCommitOutboxDeliveryResult(
            start.OutboxItemId,
            RuntimePostCommitOutboxStatus.FailedFinal,
            Now.AddSeconds(1),
            "safe classified delivery failure");
        var projection = Assert.IsType<PostCommitFailureProjection>(
            await new WorkflowDispatchDeliveryFailureProjector(stores.Dispatches).ProjectAsync(claim.Item, failure));
        await stores.Outbox.CompleteClaimAsync(new RuntimePostCommitOutboxClaimCompletion(
            claim,
            failure,
            projection.WorkflowDispatch,
            projection.FollowUpOutboxItem));
        return new SeededFailure(
            access,
            stores.Outbox,
            stores.Dispatches,
            dispatch,
            start,
            claim,
            identity);
    }

    private static WorkflowDispatchRecord NewDispatch(WorkflowDispatchMode mode, string? tenantId = null)
    {
        var activityId = $"activity-{Guid.NewGuid():N}";
        var parentId = $"parent-{Guid.NewGuid():N}";
        var identity = new WorkflowDispatchIdentity(parentId, activityId);
        return new WorkflowDispatchRecord(
            identity.DispatchId,
            parentId,
            activityId,
            identity.ChildWorkflowExecutionId,
            new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1", "hash-1"),
            new WorkflowExecutableSourceProvenance("source-1", "WorkflowDefinitionVersion", "version-1", "1", "definition-1", "version-1", "1", "publication-1", "slot-1"),
            mode,
            WorkflowDispatchStatus.Pending,
            null,
            tenantId,
            new WorkflowExecutionPartition(WorkflowExecutionPartition.DefaultValue),
            WorkflowRunKind.PublishedRun,
            new WorkflowExecutionAuthoritySnapshot(parentId, "initiator-1"),
            [],
            Now.AddMinutes(-2),
            Now.AddMinutes(-2));
    }

    private static StoresPair Stores(
        GroundworkDocumentStoreFixture fixture,
        IPersistenceAccessContextAccessor access) =>
        new(
            new GroundworkRuntimePostCommitOutboxStore(
                fixture.DocumentStore,
                GroundworkTestSerialization.Serializer,
                fixture.BoundedDocumentStore,
                access),
            new GroundworkWorkflowDispatchStore(
                fixture.DocumentStore,
                GroundworkTestSerialization.Serializer,
                access,
                fixture.BoundedDocumentStore));

    private static WorkflowDispatchIdentity Identity(WorkflowDispatchRecord dispatch) =>
        new(dispatch.ParentWorkflowExecutionId, dispatch.ParentActivityExecutionId);

    private sealed record StoresPair(
        GroundworkRuntimePostCommitOutboxStore Outbox,
        GroundworkWorkflowDispatchStore Dispatches);

    private sealed record SeededFailure(
        IPersistenceAccessContextAccessor Access,
        GroundworkRuntimePostCommitOutboxStore Outbox,
        GroundworkWorkflowDispatchStore Dispatches,
        WorkflowDispatchRecord Dispatch,
        RuntimePostCommitOutboxItem Start,
        RuntimePostCommitOutboxClaim Claim,
        WorkflowDispatchIdentity Identity);

    private sealed class DispatchSaveFailingDocumentStore(InMemoryDocumentStore inner) : IDocumentStore
    {
        public bool FailNextDispatchSave { get; set; }
        public TransactionBoundary TransactionBoundary => inner.TransactionBoundary;
        public DocumentStoreAccess Access => inner.Access;

        public Task<DocumentStoreWriteResult> SaveAsync(SaveDocumentRequest request, CancellationToken cancellationToken = default) =>
            inner.SaveAsync(request, cancellationToken);

        public Task<DocumentEnvelope?> LoadAsync(string documentKind, string id, CancellationToken cancellationToken = default) =>
            inner.LoadAsync(documentKind, id, cancellationToken);

        public Task<DocumentStoreWriteResult> DeleteAsync(DeleteDocumentRequest request, CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(request, cancellationToken);

#pragma warning disable GW0004
        public Task<IReadOnlyList<DocumentEnvelope>> QueryAsync(DocumentStoreQuery query, CancellationToken cancellationToken = default) =>
            inner.QueryAsync(query, cancellationToken);

        public Task<DocumentQueryResult> QueryAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            inner.QueryAsync(query, cancellationToken);

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            inner.FirstOrDefaultAsync(query, cancellationToken);

        public Task<bool> AnyAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            inner.AnyAsync(query, cancellationToken);
#pragma warning restore GW0004

        public async Task<IDocumentUnitOfWork> BeginAsync(
            DocumentCommitScope scope,
            CancellationToken cancellationToken = default) =>
            new DispatchSaveFailingUnitOfWork(await inner.BeginAsync(scope, cancellationToken), this);
    }

    private sealed class DispatchSaveFailingUnitOfWork(
        IDocumentUnitOfWork inner,
        DispatchSaveFailingDocumentStore owner) : IDocumentUnitOfWork
    {
        public Task<DocumentStoreWriteResult> SaveAsync(
            SaveDocumentRequest request,
            CancellationToken cancellationToken = default)
        {
            if (owner.FailNextDispatchSave &&
                StringComparer.Ordinal.Equals(request.DocumentKind, ElsaRuntimeStorageManifest.WorkflowDispatchDocumentKind))
            {
                owner.FailNextDispatchSave = false;
                throw new InvalidOperationException("Injected dispatch redrive save failure.");
            }
            return inner.SaveAsync(request, cancellationToken);
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

        public Task CommitAsync(CancellationToken cancellationToken = default) =>
            inner.CommitAsync(cancellationToken);

        public Task RollbackAsync(CancellationToken cancellationToken = default) =>
            inner.RollbackAsync(cancellationToken);

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
