using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Runtime;
using Elsa.Persistence.Groundwork.V2.Testing;
using Elsa.Workflows.Runtime.Core.Models;
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

        var stale = await authority.TryActivateAsync(new("definition-1", "default", "activation-2", publishing, 0, now));
        Assert.Equal(WorkflowActivationConflict.RevisionMismatch, stale.Conflict);
        var foreign = await authority.TryActivateAsync(new("definition-1", "default", "activation-2", importer, 1, now));
        Assert.Equal(WorkflowActivationConflict.ForeignSource, foreign.Conflict);

        var cleared = await authority.TryDeactivateAsync("definition-1", "default", publishing, 1, now);
        Assert.True(cleared.Succeeded);
        var claimed = await authority.TryActivateAsync(new("definition-1", "default", "activation-2", importer, 2, now));
        Assert.True(claimed.Succeeded);
        Assert.Equal(3, claimed.Slot.Revision);
    }
}
