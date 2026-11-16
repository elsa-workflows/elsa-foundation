using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Reconciliation;
using Elsa.Activities.Design.Core.Reconciliation.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Xunit;

namespace Elsa.Activities.Design.Tests.Integration;

public sealed class ActivityDefinitionVersionRefreshConcurrencyTests
{
    [Fact]
    public async Task StaleExpectedHash_WhenTheRowAlreadyHasTheIncomingHash_Succeeds()
    {
        var store = new InMemoryReconcilerHarness.CatalogStore();
        var unset = Input();
        await InMemoryReconcilerHarness.BuildReconciler(store, Source(Model(inputs: [unset]))).Reconcile(CancellationToken.None);
        var persisted = Assert.Single(store.Versions);
        var originalHash = persisted.Hash;

        await InMemoryReconcilerHarness.BuildReconciler(
            store,
            Source(Model(inputs: [unset with { Tier = ActivityInputTiers.Optional }]))).Reconcile(CancellationToken.None);

        var refreshed = Assert.Single(store.Versions);
        Assert.NotEqual(originalHash, refreshed.Hash);

        await InMemoryReconcilerHarness.RefreshCommand(store).RefreshAsync(
            new DesignOperationKey("stale-winner"),
            CopyWithHash(refreshed, refreshed.Hash),
            originalHash);
    }

    [Fact]
    public async Task StaleExpectedHash_WhenTheRowHasADifferentHash_ThrowsMismatch()
    {
        var store = new InMemoryReconcilerHarness.CatalogStore();
        var unset = Input();
        await InMemoryReconcilerHarness.BuildReconciler(store, Source(Model(inputs: [unset]))).Reconcile(CancellationToken.None);
        var persisted = Assert.Single(store.Versions);
        var originalHash = persisted.Hash;

        await InMemoryReconcilerHarness.BuildReconciler(
            store,
            Source(Model(inputs: [unset with { Tier = ActivityInputTiers.Optional }]))).Reconcile(CancellationToken.None);

        var refreshed = Assert.Single(store.Versions);

        var ex = await Assert.ThrowsAsync<ActivityVersionHashMismatchException>(() =>
            InMemoryReconcilerHarness.RefreshCommand(store).RefreshAsync(
                new DesignOperationKey("stale-conflict"),
                CopyWithHash(refreshed, "some-other-hash"),
                originalHash));

        Assert.Equal(refreshed.Hash, ex.PersistedHash);
        Assert.Equal("some-other-hash", ex.IncomingHash);
        Assert.Equal(ActivityInputTiers.Optional, Assert.Single(refreshed.Inputs).Tier);
    }

    private static IActivityReconciliationSource Source(ActivityVersionReconciliationModel model) =>
        new InMemoryReconcilerHarness.InMemorySource("stub", "CLR", model);

    private static ActivityVersionReconciliationModel Model(IReadOnlyCollection<InputDefinition> inputs) =>
        new(
            Id: null,
            Version: "1.0.0",
            ActivityTypeKey: "Acme.Activities.Greet",
            DisplayName: null,
            Category: "Acme",
            Description: "same",
            ProviderKey: "elsa.clr-activity",
            ProviderSchemaVersion: "1",
            ConsumerKey: "elsa.clr-activity",
            ConsumerSchemaVersion: "1",
            Descriptor: new ClrActivityDescriptor("Object"),
            Inputs: inputs,
            Outputs: [],
            DesignFacets: []);

    private static InputDefinition Input() =>
        new(
            "message",
            "Message",
            new TypeReference("String"),
            StorageDriverType: null,
            DisplayName: "Message",
            Category: null,
            IsNullable: false);

    private static ActivityDefinitionVersion CopyWithHash(ActivityDefinitionVersion source, string? hash)
    {
        var copy = ActivityDefinitionVersion.From(source);
        copy.Id = source.Id;
        copy.TenantId = source.TenantId;
        copy.Hash = hash;
        copy.Definition = source.Definition;
        return copy;
    }
}
