using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Runtime;
using Elsa.Persistence.Groundwork.V2.Testing;
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
}
