using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Serialization;
using Groundwork.Core.Manifests;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests.Alterations;

/// <summary>
/// Pins the provider-neutral storage declaration used by every Groundwork adapter for durable
/// alteration plans and their independently leaseable target jobs.
/// </summary>
public sealed class GroundworkAlterationManifestTests
{
    [Fact]
    public void Alteration_plan_and_job_units_declare_the_durable_query_surface()
    {
        var manifest = ElsaRuntimeStorageManifest.Create();

        var plan = Assert.Single(
            manifest.StorageUnits,
            unit => unit.Identity.Value == ElsaRuntimeStorageManifest.WorkflowAlterationPlanDocumentKind);
        var job = Assert.Single(
            manifest.StorageUnits,
            unit => unit.Identity.Value == ElsaRuntimeStorageManifest.WorkflowAlterationJobDocumentKind);

        Assert.Contains(plan.Indexes, index => index.Identity == ElsaRuntimeStorageManifest.WorkflowAlterationPlanByCollection);
        Assert.Contains(plan.Indexes, index => index.Identity == ElsaRuntimeStorageManifest.WorkflowAlterationPlanByTenantAndIdempotency);
        Assert.Contains(plan.Indexes, index => index.Identity == ElsaRuntimeStorageManifest.WorkflowAlterationPlanIdempotencyUniqueness && index.IsUnique);
        Assert.Contains(plan.Queries, query => query.Identity == ElsaRuntimeStorageManifest.ListWorkflowAlterationPlansQuery);
        Assert.Contains(plan.Queries, query => query.Identity == ElsaRuntimeStorageManifest.FindWorkflowAlterationPlanByTenantAndIdempotencyQuery);

        Assert.Contains(job.Indexes, index => index.Identity == ElsaRuntimeStorageManifest.WorkflowAlterationJobByPlan);
        Assert.Contains(job.Indexes, index => index.Identity == ElsaRuntimeStorageManifest.WorkflowAlterationJobByClaimability);
        Assert.Contains(job.Queries, query => query.Identity == ElsaRuntimeStorageManifest.PageWorkflowAlterationJobsByPlanQuery);
        Assert.Contains(job.Queries, query => query.Identity == ElsaRuntimeStorageManifest.ListClaimableWorkflowAlterationJobsQuery);
    }

    [Fact]
    public void Alteration_units_are_tenant_scoped_and_optimistically_concurrent()
    {
        var manifest = ElsaRuntimeStorageManifest.Create();

        foreach (var documentKind in new[]
                 {
                     ElsaRuntimeStorageManifest.WorkflowAlterationPlanDocumentKind,
                     ElsaRuntimeStorageManifest.WorkflowAlterationJobDocumentKind
                 })
        {
            var unit = manifest.StorageUnits.Single(candidate => candidate.Identity.Value == documentKind);
            Assert.Equal(TenancyPolicy.Scoped, unit.Tenancy);
            Assert.Equal(ConcurrencyPolicy.Optimistic(), unit.Concurrency);
        }
    }

    [Fact]
    public void Physicalized_manifest_admits_bounded_alteration_page_claim_and_capture_routes()
    {
        var manifest = ElsaRuntimeStorageManifest.CreatePhysicalized();
        var plan = manifest.StorageUnits.Single(unit => unit.Identity.Value == ElsaRuntimeStorageManifest.WorkflowAlterationPlanDocumentKind);
        var job = manifest.StorageUnits.Single(unit => unit.Identity.Value == ElsaRuntimeStorageManifest.WorkflowAlterationJobDocumentKind);
        var execution = manifest.StorageUnits.Single(unit => unit.Identity.Value == ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind);

        Assert.Contains(plan.PhysicalStorage!.BoundedQueries, query => query.Identity == ElsaRuntimeStorageManifest.ListWorkflowAlterationPlansQuery);
        Assert.Contains(plan.PhysicalStorage.BoundedQueries, query => query.Identity == ElsaRuntimeStorageManifest.FindWorkflowAlterationPlanByTenantAndIdempotencyQuery);
        Assert.Contains(job.PhysicalStorage!.BoundedQueries, query => query.Identity == ElsaRuntimeStorageManifest.PageWorkflowAlterationJobsByPlanQuery);
        Assert.Contains(job.PhysicalStorage.BoundedQueries, query => query.Identity == ElsaRuntimeStorageManifest.ListClaimableWorkflowAlterationJobsQuery);
        Assert.Contains(execution.PhysicalStorage!.BoundedQueries, query => query.Identity == ElsaRuntimeStorageManifest.PageWorkflowExecutionsForAlterationCaptureQuery);
        Assert.Equal(1, ElsaRuntimeDocumentVersions.CurrentFor(ElsaRuntimeStorageManifest.WorkflowAlterationPlanDocumentKind));
        Assert.Equal(1, ElsaRuntimeDocumentVersions.CurrentFor(ElsaRuntimeStorageManifest.WorkflowAlterationJobDocumentKind));
    }
}
