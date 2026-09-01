using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Runtime;
using Elsa.Persistence.Groundwork.V2.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Kernel;
using Groundwork.Store;
using Xunit;

namespace Elsa.Persistence.Groundwork.V2.Runtime.Tests;

public sealed class GroundworkV2WorkflowActivationAuthorityTests
{
    [Fact]
    public async Task Sqlite_proves_first_claim_revision_cas_foreign_owner_and_deactivation()
    {
        await using var persistence = GroundworkV2TestPersistence.Create(
            "sqlite",
            [ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.WorkflowActivationSlotDocumentKind)]);
        var authority = new GroundworkV2WorkflowActivationAuthority(
            persistence.Sessions,
            persistence.Access(),
            new GroundworkStorageTransactionFactory(persistence.Sessions, persistence.Access()));
        var now = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
        var publishing = WorkflowActivationSource.Publishing;
        var importer = WorkflowActivationSource.ArtifactReconciliation("prod-drop");

        var first = await authority.TryActivateAsync(new("definition-1", "default", "activation-1", publishing, 0, now));
        Assert.True(first.Succeeded);
        Assert.Equal(1, first.Slot.Revision);

        var canary = await authority.TryActivateAsync(new("definition-1", "canary", "activation-2", importer, 0, now));
        Assert.True(canary.Succeeded);
        Assert.True((await authority.TryDeactivateAsync("definition-1", "canary", importer, 1, now)).Succeeded);
        var beta = await authority.TryActivateAsync(new("definition-1", "beta", "activation-3", importer, 0, now));
        Assert.True(beta.Succeeded);
        Assert.True((await authority.TryDeactivateAsync("definition-1", "beta", importer, 1, now)).Succeeded);
        var duplicate = await authority.TryActivateAsync(new("definition-1", "canary", "activation-1", publishing, 2, now));
        Assert.Equal(WorkflowActivationConflict.RevisionMismatch, duplicate.Conflict);
        var lanes = await authority.ListByDefinitionAsync("definition-1");
        Assert.Equal(new[] { "beta", "canary", "default" }, lanes.Select(slot => slot.SlotName));
        Assert.All(lanes.Where(slot => slot.SlotName != "default"), slot => Assert.Null(slot.ActiveActivationId));

        var stale = await authority.TryActivateAsync(new("definition-1", "default", "activation-2", publishing, 0, now));
        Assert.Equal(WorkflowActivationConflict.RevisionMismatch, stale.Conflict);
        var foreign = await authority.TryActivateAsync(new("definition-1", "default", "activation-2", importer, 1, now));
        Assert.Equal(WorkflowActivationConflict.ForeignSource, foreign.Conflict);

        var takeover = await authority.TryActivateAsync(new("definition-1", "default", "activation-2", importer, 1, now, WorkflowActivationOwnershipIntent.TakeOver));
        Assert.True(takeover.Succeeded);
        Assert.Equal(publishing, takeover.ReplacedSource);

        var cleared = await authority.TryDeactivateAsync("definition-1", "default", importer, 2, now);
        Assert.True(cleared.Succeeded);
        var claimed = await authority.TryActivateAsync(new("definition-1", "default", "activation-2", importer, 3, now));
        Assert.True(claimed.Succeeded);
        Assert.Equal(4, claimed.Slot.Revision);

        var duplicateActive = persistence.Sessions
            .Open(
                ElsaRuntimeV2StorageManifest.WorkflowActivationSlotDocumentKind,
                StorageAccess.Scoped(new StorageScope("tenant-a")))
            .Insert(
                GroundworkV2WorkflowActivationSlotStorageConventions.Values(
                    new(
                        WorkflowActivationSlotIdentity.Create("definition-1", "duplicate-active"),
                        "definition-1",
                        "duplicate-active",
                        "activation-2",
                        importer,
                        0,
                        now)),
                WriteOptions.CreateOnly);
        Assert.Equal(WriteOutcomeStatus.UniqueViolation, duplicateActive.Status);
    }

    [Fact]
    public async Task In_memory_concurrent_first_claims_have_one_winner_without_leaking_a_provider_throw()
    {
        await using var persistence = GroundworkV2TestPersistence.Create(
            "memory",
            [ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.WorkflowActivationSlotDocumentKind)]);
        var first = new GroundworkV2WorkflowActivationAuthority(
            persistence.Sessions, persistence.Access(),
            new GroundworkStorageTransactionFactory(persistence.Sessions, persistence.Access()));
        var second = new GroundworkV2WorkflowActivationAuthority(
            persistence.Sessions, persistence.Access(),
            new GroundworkStorageTransactionFactory(persistence.Sessions, persistence.Access()));
        var now = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
        var results = await Task.WhenAll(
            first.TryActivateAsync(new("definition-1", "default", "activation-a", WorkflowActivationSource.Publishing, 0, now)).AsTask(),
            second.TryActivateAsync(new("definition-1", "default", "activation-b", WorkflowActivationSource.Publishing, 0, now)).AsTask());

        Assert.Single(results, result => result.Succeeded);
        Assert.Single(results, result => !result.Succeeded && result.Conflict == WorkflowActivationConflict.RevisionMismatch);
    }

    [Fact]
    public async Task Sqlite_concurrent_first_claims_return_one_winner_and_one_revision_conflict()
    {
        await using var persistence = GroundworkV2TestPersistence.Create(
            "sqlite",
            [ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.WorkflowActivationSlotDocumentKind)]);
        IWorkflowActivationAuthority first = CreateAuthority(persistence);
        IWorkflowActivationAuthority second = CreateAuthority(persistence);
        var now = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var slotName = $"concurrent-{attempt}";
            using var barrier = new Barrier(2);
            var results = await Task.WhenAll(
                Task.Run(async () =>
                {
                    barrier.SignalAndWait();
                    return await first.TryActivateAsync(new(
                        "definition-concurrent",
                        slotName,
                        $"activation-a-{attempt}",
                        WorkflowActivationSource.Publishing,
                        0,
                        now));
                }),
                Task.Run(async () =>
                {
                    barrier.SignalAndWait();
                    return await second.TryActivateAsync(new(
                        "definition-concurrent",
                        slotName,
                        $"activation-b-{attempt}",
                        WorkflowActivationSource.Publishing,
                        0,
                        now));
                }));

            Assert.Single(results, result => result.Succeeded);
            Assert.Single(results, result => !result.Succeeded && result.Conflict == WorkflowActivationConflict.RevisionMismatch);
        }
    }

    [Fact]
    public async Task Sqlite_concurrent_update_and_deactivation_cas_races_return_revision_conflicts()
    {
        await using var persistence = GroundworkV2TestPersistence.Create(
            "sqlite",
            [ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.WorkflowActivationSlotDocumentKind)]);
        IWorkflowActivationAuthority first = CreateAuthority(persistence);
        IWorkflowActivationAuthority second = CreateAuthority(persistence);
        var now = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
        var initial = await first.TryActivateAsync(new(
            "definition-cas",
            "default",
            "activation-initial",
            WorkflowActivationSource.Publishing,
            0,
            now));
        Assert.True(initial.Succeeded);

        using (var barrier = new Barrier(2))
        {
            var updates = await Task.WhenAll(
                Task.Run(async () =>
                {
                    barrier.SignalAndWait();
                    return await first.TryActivateAsync(new(
                        "definition-cas",
                        "default",
                        "activation-a",
                        WorkflowActivationSource.Publishing,
                        initial.Slot.Revision,
                        now));
                }),
                Task.Run(async () =>
                {
                    barrier.SignalAndWait();
                    return await second.TryActivateAsync(new(
                        "definition-cas",
                        "default",
                        "activation-b",
                        WorkflowActivationSource.Publishing,
                        initial.Slot.Revision,
                        now));
                }));

            Assert.Single(updates, result => result.Succeeded);
            Assert.Single(updates, result => !result.Succeeded && result.Conflict == WorkflowActivationConflict.RevisionMismatch);
        }

        var updated = (await first.FindAsync("definition-cas", "default"))!;
        Assert.Equal(2, updated.Revision);
        Assert.Contains(updated.ActiveActivationId, new[] { "activation-a", "activation-b" });

        using (var barrier = new Barrier(2))
        {
            var deactivations = await Task.WhenAll(
                Task.Run(async () =>
                {
                    barrier.SignalAndWait();
                    return await first.TryDeactivateAsync(
                        "definition-cas",
                        "default",
                        WorkflowActivationSource.Publishing,
                        updated.Revision,
                        now);
                }),
                Task.Run(async () =>
                {
                    barrier.SignalAndWait();
                    return await second.TryDeactivateAsync(
                        "definition-cas",
                        "default",
                        WorkflowActivationSource.Publishing,
                        updated.Revision,
                        now);
                }));

            Assert.Single(deactivations, result => result.Succeeded);
            Assert.Single(deactivations, result => !result.Succeeded && result.Conflict == WorkflowActivationConflict.RevisionMismatch);
        }

        var deactivated = (await first.FindAsync("definition-cas", "default"))!;
        Assert.Equal(3, deactivated.Revision);
        Assert.Null(deactivated.ActiveActivationId);
        Assert.Null(deactivated.Source);
    }

    [Theory]
    [InlineData(WriteOutcomeStatus.ConcurrencyConflict)]
    [InlineData(WriteOutcomeStatus.UniqueViolation)]
    public async Task Modeled_batch_conflict_is_retried_without_leaking_provider_exception(WriteOutcomeStatus status)
    {
        await using var persistence = GroundworkV2TestPersistence.Create(
            "memory",
            [ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.WorkflowActivationSlotDocumentKind)]);
        var source = new FaultingBatchSessionSource(persistence.Sessions)
        {
            NextFailureStatus = status
        };
        IWorkflowActivationAuthority authority = CreateAuthority(source, persistence);

        var result = await authority.TryActivateAsync(new(
            "definition-retry",
            "default",
            "activation-retry",
            WorkflowActivationSource.Publishing,
            0,
            new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero)));

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Slot.Revision);
        Assert.Equal(2, source.BeginCount);
    }

    [Theory]
    [InlineData(WriteOutcomeStatus.ConcurrencyConflict)]
    [InlineData(WriteOutcomeStatus.UniqueViolation)]
    public async Task Modeled_batch_conflict_with_winner_returns_revision_mismatch(WriteOutcomeStatus status)
    {
        await using var persistence = GroundworkV2TestPersistence.Create(
            "memory",
            [ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.WorkflowActivationSlotDocumentKind)]);
        var source = new FaultingBatchSessionSource(persistence.Sessions)
        {
            NextFailureStatus = status
        };
        source.BeforeFailure = (writes, access) =>
        {
            var write = writes.Single();
            var values = write.Values ?? throw new InvalidOperationException("The activation-slot test write has no values.");
            var winner = persistence.Sessions.Open(write.Unit.Id.Value, access).Insert(
                values,
                WriteOptions.CreateOnly);
            Assert.Equal(WriteOutcomeStatus.Inserted, winner.Status);
        };
        IWorkflowActivationAuthority authority = CreateAuthority(source, persistence);

        var result = await authority.TryActivateAsync(new(
            "definition-winner",
            "default",
            "activation-winner",
            WorkflowActivationSource.Publishing,
            0,
            new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero)));

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowActivationConflict.RevisionMismatch, result.Conflict);
        Assert.Equal(1, result.Slot.Revision);
        Assert.Equal(1, source.BeginCount);
    }

    [Fact]
    public async Task Unexpected_batch_outcome_is_rethrown()
    {
        await using var persistence = GroundworkV2TestPersistence.Create(
            "memory",
            [ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.WorkflowActivationSlotDocumentKind)]);
        var source = new FaultingBatchSessionSource(persistence.Sessions)
        {
            NextFailureStatus = WriteOutcomeStatus.NotFound
        };
        IWorkflowActivationAuthority authority = CreateAuthority(source, persistence);

        var exception = await Assert.ThrowsAsync<BatchWriteException>(() => authority.TryActivateAsync(new(
            "definition-unexpected",
            "default",
            "activation-unexpected",
            WorkflowActivationSource.Publishing,
            0,
            new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero))).AsTask());

        Assert.Equal(WriteOutcomeStatus.NotFound, exception.Outcomes.Single().Outcome.Status);
        Assert.Equal(1, source.BeginCount);
    }

    [Fact]
    public async Task Caller_cancellation_during_batch_commit_is_rethrown()
    {
        await using var persistence = GroundworkV2TestPersistence.Create(
            "memory",
            [ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.WorkflowActivationSlotDocumentKind)]);
        using var cancellation = new CancellationTokenSource();
        var source = new FaultingBatchSessionSource(persistence.Sessions)
        {
            BeforeFailure = (_, _) => cancellation.Cancel(),
            ThrowCancellation = true
        };
        IWorkflowActivationAuthority authority = CreateAuthority(source, persistence);

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => authority.TryActivateAsync(new(
            "definition-cancel",
            "default",
            "activation-cancel",
            WorkflowActivationSource.Publishing,
            0,
            new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero)), cancellation.Token).AsTask());

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal(1, source.BeginCount);
    }

    private static IWorkflowActivationAuthority CreateAuthority(GroundworkV2TestPersistence persistence) =>
        new GroundworkV2WorkflowActivationAuthority(
            persistence.Sessions,
            persistence.Access(),
            new GroundworkStorageTransactionFactory(persistence.Sessions, persistence.Access()));

    private static IWorkflowActivationAuthority CreateAuthority(
        FaultingBatchSessionSource source,
        GroundworkV2TestPersistence persistence) =>
        new GroundworkV2WorkflowActivationAuthority(
            source,
            persistence.Access(),
            new GroundworkStorageTransactionFactory(source, persistence.Access()));

    private sealed class FaultingBatchSessionSource(IGroundworkStorageSessionSource inner) :
        IGroundworkStorageSessionSource,
        IGroundworkStorageCapabilitySource
    {
        public WriteOutcomeStatus? NextFailureStatus { get; set; }

        public Action<IReadOnlyList<RowWrite>, StorageAccess>? BeforeFailure { get; set; }

        public bool ThrowCancellation { get; set; }

        public int BeginCount { get; private set; }

        public IReadOnlyList<CapabilityDescriptor> Capabilities(string? targetName = null) =>
            ((IGroundworkStorageCapabilitySource)inner).Capabilities(targetName);

        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null) =>
            inner.Open(unitId, access, targetName);

        public IUnitOfWork BeginUnitOfWork(
            StorageAccess access,
            BatchWriteOptions options,
            IReadOnlyList<string> unitIds,
            string? targetName = null)
        {
            BeginCount++;
            var unitOfWork = inner.BeginUnitOfWork(access, options, unitIds, targetName);
            if (NextFailureStatus is not null || BeforeFailure is not null || ThrowCancellation)
            {
                var status = NextFailureStatus;
                var beforeFailure = BeforeFailure;
                var throwCancellation = ThrowCancellation;
                NextFailureStatus = null;
                BeforeFailure = null;
                ThrowCancellation = false;
                return new FaultingBatchUnitOfWork(unitOfWork, access, status, beforeFailure, throwCancellation);
            }

            return unitOfWork;
        }

        public StorageUnit Unit(string unitId, string? targetName = null) => inner.Unit(unitId, targetName);
    }

    private sealed class FaultingBatchUnitOfWork(
        IUnitOfWork inner,
        StorageAccess access,
        WriteOutcomeStatus? failureStatus,
        Action<IReadOnlyList<RowWrite>, StorageAccess>? beforeFailure,
        bool throwCancellation) : IUnitOfWork
    {
        private readonly List<RowWrite> staged = [];

        public IStorageSession OpenSession(StorageUnit unit) => inner.OpenSession(unit);

        public void Stage(RowWrite write)
        {
            staged.Add(write);
            inner.Stage(write);
        }

        public BatchWriteSummary Commit() => throw CreateFailure(CancellationToken.None);

        public BatchWriteReport CommitWithOutcomes() => throw CreateFailure(CancellationToken.None);

        public ValueTask<BatchWriteSummary> CommitAsync(CancellationToken cancellationToken = default) =>
            throw CreateFailure(cancellationToken);

        public ValueTask<BatchWriteReport> CommitWithOutcomesAsync(CancellationToken cancellationToken = default) =>
            throw CreateFailure(cancellationToken);

        public void Rollback() => inner.Rollback();

        public void Dispose() => inner.Dispose();

        private BatchWriteException CreateFailure(CancellationToken cancellationToken)
        {
            if (staged.Count != 1)
                throw new InvalidOperationException("The deterministic activation-slot failure requires one staged row.");
            beforeFailure?.Invoke(staged, access);
            if (throwCancellation)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new OperationCanceledException(cancellationToken);
            }
            var status = failureStatus ?? throw new InvalidOperationException("The deterministic batch failure has no outcome.");
            return new BatchWriteException(
                $"Deterministic activation-slot failure: {status}.",
                [new RowWriteOutcome(staged[0], new WriteOutcome(status))]);
        }
    }
}
