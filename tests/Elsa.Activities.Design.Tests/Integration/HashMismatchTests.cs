using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Reconciliation.Core;
using Elsa.Activities.Design.Reconciliation.Core.Models;
using Elsa.Primitives.Enums;
using Elsa.Primitives.Models;
using Xunit;

namespace Elsa.Activities.Design.Tests.Integration;

/// <summary>
/// US5 test (SC-004). The Model X duplicate path: when a second pass contributes the same
/// <c>(DefinitionId, Version)</c> as an already-persisted row, the reconciler compares the
/// incoming content hash to the persisted one.
/// - Different content (here, a changed <c>Description</c>) → <see cref="ActivityVersionHashMismatchException"/>,
///   surfacing the broken source loudly and persisting nothing new.
/// - Identical content → a genuine duplicate, handled per the <see cref="DuplicateHandling"/> option:
///   skipped silently by default, or thrown when configured to throw.
/// </summary>
public sealed class HashMismatchTests
{
    [Fact]
    public async Task SameVersionDifferentContent_ThrowsHashMismatch_AndAppendsNothing()
    {
        var store = new InMemoryReconcilerHarness.CatalogStore();

        await InMemoryReconcilerHarness.BuildReconciler(store, Source(Model("first"))).Reconcile(CancellationToken.None);
        Assert.Single(store.Versions);

        var ex = await Assert.ThrowsAsync<ActivityVersionHashMismatchException>(
            () => InMemoryReconcilerHarness.BuildReconciler(store, Source(Model("second"))).Reconcile(CancellationToken.None));

        Assert.Equal("1.0.0", ex.Version);
        Assert.Single(store.Versions);
    }

    [Fact]
    public async Task SameVersionSameContent_SkippedByDefault()
    {
        var store = new InMemoryReconcilerHarness.CatalogStore();

        await InMemoryReconcilerHarness.BuildReconciler(store, Source(Model("same"))).Reconcile(CancellationToken.None);
        await InMemoryReconcilerHarness.BuildReconciler(store, Source(Model("same"))).Reconcile(CancellationToken.None);

        Assert.Single(store.Versions);
    }

    [Fact]
    public async Task SameVersionSameContent_ThrowsWhenDuplicateHandlingIsThrow()
    {
        var store = new InMemoryReconcilerHarness.CatalogStore();

        await InMemoryReconcilerHarness.BuildReconciler(store, Source(Model("same"))).Reconcile(CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => InMemoryReconcilerHarness.BuildReconciler(store, Source(Model("same")), DuplicateHandling.Throw).Reconcile(CancellationToken.None));

        Assert.Single(store.Versions);
    }

    private static IActivityReconciliationSource Source(ActivityVersionReconciliationModel model) =>
        new InMemoryReconcilerHarness.InMemorySource("stub", "CLR", model);

    private static ActivityVersionReconciliationModel Model(string description) =>
        new(
            Id: null,
            Version: "1.0.0",
            ActivityTypeKey: "Acme.Activities.Greet",
            DisplayName: null,
            Category: "Acme",
            Description: description,
            ImplementationKind: ClrImplementationDescriptor.KindValue,
            ImplementationDescriptor: new ClrImplementationDescriptor(TypeInformation.Object),
            Inputs: [],
            Outputs: [],
            Ports: []);
}
