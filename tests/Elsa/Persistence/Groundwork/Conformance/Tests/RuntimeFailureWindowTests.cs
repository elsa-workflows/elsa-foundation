using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Persistence.Groundwork.Testing;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;
using Xunit;

namespace Elsa.Persistence.Groundwork.Conformance.Tests;

public sealed class RuntimeFailureWindowTests
{
    private static readonly string[] OperationalDocumentKinds =
    [
        ElsaRuntimeStorageManifest.DurableTimerDocumentKind,
        ElsaRuntimeStorageManifest.IncidentStateDocumentKind,
        ElsaRuntimeStorageManifest.RecurringTriggerScheduleDocumentKind,
        ElsaRuntimeStorageManifest.PostCommitOutboxDocumentKind,
        ElsaRuntimeStorageManifest.SchedulerStateDocumentKind,
        ElsaRuntimeStorageManifest.WorkflowHoldStateDocumentKind,
        ElsaRuntimeStorageManifest.SchedulerPoisonDocumentKind,
        ElsaRuntimeStorageManifest.SchedulerWorkItemDocumentKind,
        ElsaRuntimeStorageManifest.WorkflowTriggerBindingDocumentKind,
        ElsaRuntimeStorageManifest.PublicationProjectionStateDocumentKind
    ];

    public static TheoryData<string, string, bool> ProviderWindows
    {
        get
        {
            var windows = GroundworkDocumentStoreFailureWindows.OperationalRuntime;
            var data = new TheoryData<string, string, bool>();
            foreach (var provider in new[] { "sqlite", "sqlserver", "postgresql", "mongodb" })
            {
                data.Add(provider, windows.BeforeProviderDecision.Value, false);
                data.Add(provider, windows.DuringProviderDecision!.Value.Value, true);
                data.Add(provider, windows.AfterDurableDecision.Value, true);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(ProviderWindows))]
    public async Task Operational_rows_converge_after_each_named_failure_window(
        string providerKey,
        string windowId,
        bool decisionIsDurable)
    {
        await using var driver = GroundworkProviderDriverFactory.Create(providerKey);
        await driver.InitializeAsync();
        driver.Descriptor.Topology.EnsureSupports(
            GroundworkTopologyCapabilities.PersistentStorage |
            GroundworkTopologyCapabilities.IndependentClients |
            GroundworkTopologyCapabilities.MultiDocumentTransactions);
        await driver.ResetPhysicalAsync([new OperationalManifestSource()]);

        var attemptedDocuments = new List<(string Kind, string Id)>();
        await using (var client = await driver.OpenPhysicalClientAsync())
        {
            foreach (var (kind, index) in OperationalDocumentKinds.Select((kind, index) => (kind, index)))
            {
                var id = $"failure-window-{index}";
                attemptedDocuments.Add((kind, id));
                var failures = new GroundworkFailureController();
                var window = new GroundworkFailureWindow(windowId);
                failures.FailAt(window);
                var decorated = Decorate(client, failures);

                var exception = await Assert.ThrowsAsync<InjectedGroundworkFailureException>(() =>
                    decorated.SaveAsync(
                        Request(kind, id),
                        CancellationToken.None));

                Assert.Equal(window, exception.Window);
            }

            await AssertAtomicBundleWindowAsync(client, windowId);
        }

        await using var reopened = await driver.OpenPhysicalClientAsync();
        foreach (var (kind, id) in attemptedDocuments)
        {
            Assert.Equal(
                decisionIsDurable,
                await reopened.DocumentStore.LoadAsync(kind, id) is not null);
        }

        Assert.Equal(
            decisionIsDurable,
            await reopened.DocumentStore.LoadAsync(
                ElsaRuntimeStorageManifest.WorkflowTriggerBindingDocumentKind,
                "failure-window-bundle-binding") is not null);
        Assert.Equal(
            decisionIsDurable,
            await reopened.DocumentStore.LoadAsync(
                ElsaRuntimeStorageManifest.PublicationProjectionStateDocumentKind,
                "failure-window-bundle-state") is not null);
    }

    private static async Task AssertAtomicBundleWindowAsync(
        GroundworkProviderClient client,
        string windowId)
    {
        var failures = new GroundworkFailureController();
        var window = new GroundworkFailureWindow(windowId);
        failures.FailAt(window);
        var decorated = Decorate(client, failures);
        await using var unitOfWork = await decorated.BeginAsync(
            DocumentCommitScope.Of(
                ElsaRuntimeStorageManifest.WorkflowTriggerBindingDocumentKind,
                ElsaRuntimeStorageManifest.PublicationProjectionStateDocumentKind));
        await unitOfWork.SaveAsync(Request(
            ElsaRuntimeStorageManifest.WorkflowTriggerBindingDocumentKind,
            "failure-window-bundle-binding"));
        await unitOfWork.SaveAsync(Request(
            ElsaRuntimeStorageManifest.PublicationProjectionStateDocumentKind,
            "failure-window-bundle-state"));

        var exception = await Assert.ThrowsAsync<InjectedGroundworkFailureException>(() =>
            unitOfWork.CommitAsync());

        Assert.Equal(window, exception.Window);
    }

    private static GroundworkFailureInjectingDocumentStore Decorate(
        GroundworkProviderClient client,
        GroundworkFailureController failures) =>
        new(
            client.DocumentStore,
            client.BoundedDocumentStore!,
            client.DocumentStore.Access,
            failures,
            GroundworkDocumentStoreFailureWindows.OperationalRuntime);

    private static SaveDocumentRequest Request(string kind, string id) =>
        new(
            kind,
            id,
            ElsaRuntimeDocumentVersions.Stamp(ElsaRuntimeDocumentVersions.CurrentFor(kind)),
            "{}",
            ExpectedVersion: 0);

    private sealed class OperationalManifestSource : IGroundworkStorageManifestSource
    {
        public string FeatureIdentity => "runtime-operational-failure-windows";

        public async ValueTask<GroundworkStorageManifestDeclaration> CreateDeclarationAsync(
            CancellationToken cancellationToken = default)
        {
            var runtime = await new RuntimeGroundworkStorageManifestSource()
                .CreateDeclarationAsync(cancellationToken);
            return new GroundworkStorageManifestDeclaration(
                FeatureIdentity,
                runtime.Manifest with
                {
                    StorageUnits = runtime.Manifest.StorageUnits
                        .Where(unit => OperationalDocumentKinds.Contains(
                            unit.Identity.Value,
                            StringComparer.Ordinal))
                        .ToArray()
                },
                [],
                [],
                [],
                []);
        }
    }
}
