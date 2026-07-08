using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Reconciliation.Core;
using Elsa.Activities.Design.Reconciliation.Core.Models;
using Elsa.Primitives.Models;
using Xunit;

namespace Elsa.Activities.Design.Tests.Integration;

/// <summary>
/// Result-equivalence coverage for the batched reconciler read path (issue #417 item 1). The batch
/// change replaces per-candidate store reads with a read-only prefetch plus in-loop indexes that are
/// updated on every create/append. These tests drive the real reconciler through
/// <see cref="InMemoryReconcilerHarness"/> and assert the batched behaviour is identical to the
/// pre-batch per-read behaviour, with particular focus on the same-new-definition-twice ordering trap
/// (two candidates for one not-yet-persisted definition inside a SINGLE pass) which had no prior
/// single-pass coverage.
/// </summary>
public sealed class ReconcilerBatchEquivalenceTests
{
    [Fact]
    public async Task EmptyBatch_PersistsNothing()
    {
        var store = new InMemoryReconcilerHarness.CatalogStore();

        await InMemoryReconcilerHarness.BuildReconciler(store, Source()).Reconcile(CancellationToken.None);

        Assert.Empty(store.Definitions);
        Assert.Empty(store.Versions);
    }

    [Fact]
    public async Task AllNewDistinct_SinglePass_CreatesOneDefinitionAndVersionEach()
    {
        var store = new InMemoryReconcilerHarness.CatalogStore();

        await InMemoryReconcilerHarness.BuildReconciler(
            store,
            Source(Model("A", "Acme.A"), Model("B", "Acme.B"), Model("C", "Acme.C")))
            .Reconcile(CancellationToken.None);

        Assert.Equal(3, store.Definitions.Count);
        Assert.Equal(3, store.Versions.Count);
        Assert.Equal(
            new[] { "Acme.A", "Acme.B", "Acme.C" },
            store.Definitions.Select(d => d.ActivityTypeKey).OrderBy(k => k).ToArray());
    }

    [Fact]
    public async Task MultipleVersionsForOneNewDefinition_SinglePass_CreatesOnceThenAppends()
    {
        // THE ordering trap: two candidates target the same not-yet-persisted definition (same
        // ActivityTypeKey, different versions) in ONE pass. Candidate 1 must create the definition +
        // its first version; candidate 2 must OBSERVE that just-created definition (via the in-loop
        // index) and APPEND — not create a duplicate definition. A naive prefetch-before-loop would
        // miss the row created mid-loop and wrongly create a second definition.
        var store = new InMemoryReconcilerHarness.CatalogStore();

        await InMemoryReconcilerHarness.BuildReconciler(
            store,
            Source(Model("first", "Acme.Greet", version: "1.0.0"), Model("second", "Acme.Greet", version: "2.0.0")))
            .Reconcile(CancellationToken.None);

        Assert.Single(store.Definitions);
        Assert.Equal(2, store.Versions.Count);

        var definition = Assert.Single(store.Definitions);
        var versions = store.Versions.Where(v => v.DefinitionId == definition.Id).Select(v => v.Version).OrderBy(v => v).ToArray();
        Assert.Equal(new[] { "1.0.0", "2.0.0" }, versions);
    }

    [Fact]
    public async Task ManyVersionsForOneNewDefinition_SinglePass_CreatesOnceThenAppendsAll()
    {
        var store = new InMemoryReconcilerHarness.CatalogStore();

        await InMemoryReconcilerHarness.BuildReconciler(
            store,
            Source(
                Model("v1", "Acme.Greet", version: "1.0.0"),
                Model("v2", "Acme.Greet", version: "2.0.0"),
                Model("v3", "Acme.Greet", version: "3.0.0"),
                Model("v4", "Acme.Greet", version: "4.0.0")))
            .Reconcile(CancellationToken.None);

        Assert.Single(store.Definitions);
        Assert.Equal(4, store.Versions.Count);
    }

    [Fact]
    public async Task SameVersionDifferentContent_SinglePass_CreatesFirstThenThrowsHashMismatch()
    {
        // Single-pass variant of the cross-pass HashMismatchTests case: candidate 1 creates the
        // definition + version 1.0.0 and seeds the index; candidate 2 (same key + version, different
        // content) observes it via the index and throws the same hash-mismatch, having appended nothing.
        var store = new InMemoryReconcilerHarness.CatalogStore();

        var ex = await Assert.ThrowsAsync<ActivityVersionHashMismatchException>(
            () => InMemoryReconcilerHarness.BuildReconciler(
                    store,
                    Source(Model("first", "Acme.Greet"), Model("second", "Acme.Greet")))
                .Reconcile(CancellationToken.None));

        Assert.Equal("1.0.0", ex.PersistedVersion);
        Assert.Equal("1.0.0", ex.IncomingVersion);
        Assert.Single(store.Definitions);
        Assert.Single(store.Versions);
    }

    [Fact]
    public async Task SameContentDifferentBuildMetadata_SinglePass_CreatesFirstThenSkipsDuplicate()
    {
        // Single-pass variant: candidate 1 creates 1.0.0+oldbuild; candidate 2 (same content, only
        // build-metadata differs) resolves to the same logical version via the sort key and is skipped.
        var store = new InMemoryReconcilerHarness.CatalogStore();

        await InMemoryReconcilerHarness.BuildReconciler(
            store,
            Source(Model("same", "Acme.Greet", version: "1.0.0+oldbuild"), Model("same", "Acme.Greet", version: "1.0.0+newbuild")))
            .Reconcile(CancellationToken.None);

        Assert.Single(store.Definitions);
        Assert.Single(store.Versions);
    }

    [Fact]
    public async Task ExistingDefinitionPrefetched_SecondPass_AppendsWithoutDuplicatingDefinition()
    {
        // Cross-pass: the definition is already persisted, so the prefetch (Id/ActivityTypeKey IN read)
        // must surface it and the second pass must append the new version to the existing definition.
        var store = new InMemoryReconcilerHarness.CatalogStore();

        await InMemoryReconcilerHarness.BuildReconciler(store, Source(Model("v1", "Acme.Greet", version: "1.0.0")))
            .Reconcile(CancellationToken.None);
        Assert.Single(store.Definitions);
        Assert.Single(store.Versions);

        await InMemoryReconcilerHarness.BuildReconciler(store, Source(Model("v2", "Acme.Greet", version: "2.0.0")))
            .Reconcile(CancellationToken.None);

        Assert.Single(store.Definitions);
        Assert.Equal(2, store.Versions.Count);
    }

    [Fact]
    public async Task UnchangedActivityTypeAndVersion_KeepStableIdsAcrossCatalogRebuild()
    {
        var firstCatalog = new InMemoryReconcilerHarness.CatalogStore();

        await InMemoryReconcilerHarness.BuildReconciler(firstCatalog, Source(Model("same", "Acme.Greet")))
            .Reconcile(CancellationToken.None);

        var firstDefinition = Assert.Single(firstCatalog.Definitions);
        var firstVersion = Assert.Single(firstCatalog.Versions);

        var rebuiltCatalog = new InMemoryReconcilerHarness.CatalogStore();

        await InMemoryReconcilerHarness.BuildReconciler(rebuiltCatalog, Source(Model("same", "Acme.Greet")))
            .Reconcile(CancellationToken.None);

        var rebuiltDefinition = Assert.Single(rebuiltCatalog.Definitions);
        var rebuiltVersion = Assert.Single(rebuiltCatalog.Versions);
        Assert.Equal(firstDefinition.Id, rebuiltDefinition.Id);
        Assert.Equal(firstVersion.Id, rebuiltVersion.Id);
        Assert.Equal(firstVersion.Id, rebuiltCatalog.Versions.Single(v => v.Id == firstVersion.Id).Id);
    }

    [Fact]
    public async Task LogicalVersionId_IgnoresBuildMetadataAcrossCatalogRebuild()
    {
        var firstCatalog = new InMemoryReconcilerHarness.CatalogStore();

        await InMemoryReconcilerHarness.BuildReconciler(firstCatalog, Source(Model("same", "Acme.Greet", version: "1.0.0+oldbuild")))
            .Reconcile(CancellationToken.None);

        var firstVersionId = Assert.Single(firstCatalog.Versions).Id;

        var rebuiltCatalog = new InMemoryReconcilerHarness.CatalogStore();

        await InMemoryReconcilerHarness.BuildReconciler(rebuiltCatalog, Source(Model("same", "Acme.Greet", version: "1.0.0+newbuild")))
            .Reconcile(CancellationToken.None);

        Assert.Equal(firstVersionId, Assert.Single(rebuiltCatalog.Versions).Id);
    }

    [Fact]
    public async Task DistinctAndSharedKeys_SinglePass_MixedCreatesAndAppends()
    {
        var store = new InMemoryReconcilerHarness.CatalogStore();

        await InMemoryReconcilerHarness.BuildReconciler(
            store,
            Source(
                Model("a1", "Acme.A", version: "1.0.0"),
                Model("b1", "Acme.B", version: "1.0.0"),
                Model("a2", "Acme.A", version: "2.0.0")))
            .Reconcile(CancellationToken.None);

        Assert.Equal(2, store.Definitions.Count);
        Assert.Equal(3, store.Versions.Count);

        var a = store.Definitions.Single(d => d.ActivityTypeKey == "Acme.A");
        Assert.Equal(2, store.Versions.Count(v => v.DefinitionId == a.Id));
    }

    private static IActivityReconciliationSource Source(params ActivityVersionReconciliationModel[] models) =>
        new InMemoryReconcilerHarness.InMemorySource("stub", "CLR", models);

    private static ActivityVersionReconciliationModel Model(string description, string activityTypeKey, string version = "1.0.0") =>
        new(
            Id: null,
            Version: version,
            ActivityTypeKey: activityTypeKey,
            DisplayName: null,
            Category: "Acme",
            Description: description,
            DescriptorType: typeof(ClrActivityDescriptor).FullName!,
            Descriptor: new ClrActivityDescriptor("Object"),
            Inputs: [],
            Outputs: [],
            DesignFacets: []);
}
