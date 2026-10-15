using Elsa.Activities.Design.Core.Reconciliation;
using Elsa.Activities.Design.Core.Reconciliation.Models;
using Xunit;

namespace Elsa.Activities.Design.Tests.Integration;

/// <summary>
/// A reconciliation source can know the version id its entry will receive before reconciliation runs,
/// so it can reference that activity from a workflow it also contributes. The source below uses only
/// Elsa.Activities.Design.Core types.
/// </summary>
public sealed class SourceComputedVersionIdTests
{
    [Fact]
    public async Task PersistedVersionId_EqualsTheIdTheSourceComputed()
    {
        var source = new SelfReferencingSource();
        var store = new InMemoryReconcilerHarness.CatalogStore();

        await InMemoryReconcilerHarness.BuildReconciler(store, source).Reconcile(CancellationToken.None);

        var persisted = Assert.Single(store.Versions);
        Assert.Equal(source.ComputedVersionId, persisted.Id);
    }

    private sealed class SelfReferencingSource : IActivityReconciliationSource
    {
        private const string ActivityTypeKey = "Acme.Activities.ReusableApproval";
        private const string Version = "1.2.0-rc.1+build.7";

        public string ComputedVersionId { get; } = ActivityCatalogStableIds.VersionId(ActivityTypeKey, Version)!;

        public string SourceId => "self-referencing";

        public string SourceKind => "Test";

        public ValueTask<IEnumerable<ActivityVersionReconciliationModel>> Read(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IEnumerable<ActivityVersionReconciliationModel>>(
            [
                new ActivityVersionReconciliationModel(
                    Id: null,
                    Version: Version,
                    ActivityTypeKey: ActivityTypeKey,
                    DisplayName: "Reusable approval",
                    Category: null,
                    Description: null,
                    ProviderKey: "test.provider",
                    ProviderSchemaVersion: "1",
                    ConsumerKey: "test.consumer",
                    ConsumerSchemaVersion: "1",
                    Descriptor: new { workflow = "approval" },
                    Inputs: [],
                    Outputs: [],
                    DesignFacets: [])
            ]);
    }
}
