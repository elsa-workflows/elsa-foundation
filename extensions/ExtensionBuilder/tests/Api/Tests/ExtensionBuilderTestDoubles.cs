using Elsa.Modularity.Core.Contracts;
using Elsa.Modularity.Core.Models;
using Nuplane.Abstractions;
using Nuplane.Admin;
using Nuplane.Operational;
using Nuplane.Reconciliation;
using Nuplane.Reconciliation.Models;

namespace Elsa.ExtensionBuilder.Api.Tests;

internal sealed class FakeNuplaneAdmin : INuplaneAdminOperations
{
    public IReadOnlyList<ActivePackage> Packages { get; set; } = [];

    public Task<ActivePackagesSnapshot> GetPackagesAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new ActivePackagesSnapshot(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Packages, "corr"));

    public Task<OperationalStateSnapshot> GetStateAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new OperationalStateSnapshot(DateTimeOffset.UtcNow, new("corr", DateTimeOffset.UtcNow, false, false, []), default, [], "corr"));

    public Task<ManualReconcileOutcome> TriggerReconcileAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new ManualReconcileOutcome(
            ManualReconcileOutcomeCode.Completed,
            "corr",
            new ReconciliationRunResult(false, new PackageChangeSet([], [], [], "corr", DateTimeOffset.UtcNow), [], false),
            null));
}

internal sealed class FakeFeatureManagement : IFeatureManagementService
{
    public IReadOnlyList<FeatureCatalogItem> Features { get; set; } = [];

    public Task<FeatureCatalogResponse> GetCatalogAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new FeatureCatalogResponse("rev", Features));

    public Task<FeatureApplyResult> ApplyAsync(FeatureApplyRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
