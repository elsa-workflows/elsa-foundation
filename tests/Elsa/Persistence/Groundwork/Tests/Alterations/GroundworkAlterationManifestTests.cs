using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Serialization;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
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
        Assert.Contains(job.Queries, query => query.Identity == ElsaRuntimeStorageManifest.ListClaimableWorkflowAlterationJobsByPlanQuery);
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
        Assert.Contains(plan.PhysicalStorage.BoundedQueries, query => query.Identity == ElsaRuntimeStorageManifest.PageActiveWorkflowAlterationPlansByTenantQuery);
        Assert.Contains(job.PhysicalStorage!.BoundedQueries, query => query.Identity == ElsaRuntimeStorageManifest.PageWorkflowAlterationJobsByPlanQuery);
        Assert.Contains(job.PhysicalStorage.BoundedQueries, query => query.Identity == ElsaRuntimeStorageManifest.ListClaimableWorkflowAlterationJobsQuery);
        Assert.Contains(job.PhysicalStorage.BoundedQueries, query => query.Identity == ElsaRuntimeStorageManifest.ListClaimableWorkflowAlterationJobsByPlanQuery);
        Assert.Contains(job.PhysicalStorage.BoundedQueries, query => query.Identity == ElsaRuntimeStorageManifest.ListRunningClaimableWorkflowAlterationJobsByPlanQuery);
        Assert.Contains(job.PhysicalStorage.BoundedQueries, query => query.Identity == ElsaRuntimeStorageManifest.ListPendingClaimableWorkflowAlterationJobsByPlanQuery);
        Assert.Contains(job.PhysicalStorage.BoundedQueries, query => query.Identity == ElsaRuntimeStorageManifest.ListPendingWorkflowAlterationJobsByPlanQuery);
        Assert.Contains(job.PhysicalStorage.BoundedQueries, query => query.Identity == ElsaRuntimeStorageManifest.CountWorkflowAlterationJobsByPlanAndStatusQuery);
        Assert.Contains(job.PhysicalStorage.BoundedQueries, query => query.Identity == ElsaRuntimeStorageManifest.FindWorkflowAlterationJobByCheckpointCommitIdQuery);
        Assert.Contains(execution.PhysicalStorage!.BoundedQueries, query => query.Identity == ElsaRuntimeStorageManifest.PageWorkflowExecutionsForAlterationCaptureQuery);
        Assert.Equal(1, ElsaRuntimeDocumentVersions.CurrentFor(ElsaRuntimeStorageManifest.WorkflowAlterationPlanDocumentKind));
        Assert.Equal(1, ElsaRuntimeDocumentVersions.CurrentFor(ElsaRuntimeStorageManifest.WorkflowAlterationJobDocumentKind));
    }

    [Fact]
    public void Physicalized_alteration_indexes_fit_portable_compound_key_limits()
    {
        var manifest = ElsaRuntimeStorageManifest.CreatePhysicalized();
        var plan = manifest.StorageUnits.Single(unit =>
            unit.Identity.Value == ElsaRuntimeStorageManifest.WorkflowAlterationPlanDocumentKind);
        var job = manifest.StorageUnits.Single(unit =>
            unit.Identity.Value == ElsaRuntimeStorageManifest.WorkflowAlterationJobDocumentKind);
        var planTable = Assert.IsType<PhysicalStoragePolicy.ExplicitPolicy>(plan.PhysicalStorage!.Policy).Definition;
        var jobTable = Assert.IsType<PhysicalStoragePolicy.ExplicitPolicy>(job.PhysicalStorage!.Policy).Definition;
        var envelope = new DocumentEnvelopeDefinition();

        AssertProjectionLength(
            planTable,
            ElsaRuntimeStorageManifest.WorkflowAlterationPlanIdField,
            ElsaRuntimeStorageManifest.RuntimeExecutionIdProjectionLength);
        AssertProjectionLength(
            planTable,
            ElsaRuntimeStorageManifest.WorkflowAlterationPlanTenantPartitionField,
            ElsaRuntimeStorageManifest.RuntimeTenantProjectionLength);
        AssertProjectionLength(
            planTable,
            ElsaRuntimeStorageManifest.WorkflowAlterationPlanActiveOrderKeyField,
            19 + 1 + ElsaRuntimeStorageManifest.RuntimeExecutionIdProjectionLength);
        AssertProjectionLength(
            planTable,
            ElsaRuntimeStorageManifest.WorkflowAlterationPlanIdempotencyKeyHashField,
            64);
        AssertProjectionLength(
            planTable,
            ElsaRuntimeStorageManifest.WorkflowAlterationPlanStatusField,
            ElsaRuntimeStorageManifest.RuntimeStatusProjectionLength);
        AssertProjectionLength(
            planTable,
            ElsaRuntimeStorageManifest.WorkflowAlterationPlanTenantIdempotencyKeyField,
            ElsaRuntimeStorageManifest.RuntimeTenantProjectionLength + 1 + 64);
        AssertProjectionLength(
            jobTable,
            ElsaRuntimeStorageManifest.WorkflowAlterationJobIdField,
            ElsaRuntimeStorageManifest.RuntimeExecutionIdProjectionLength);
        AssertProjectionLength(
            jobTable,
            ElsaRuntimeStorageManifest.WorkflowAlterationJobPlanIdField,
            ElsaRuntimeStorageManifest.RuntimeExecutionIdProjectionLength);
        AssertProjectionLength(
            jobTable,
            ElsaRuntimeStorageManifest.WorkflowAlterationJobStatusField,
            ElsaRuntimeStorageManifest.RuntimeStatusProjectionLength);
        AssertProjectionLength(
            jobTable,
            ElsaRuntimeStorageManifest.WorkflowAlterationJobCheckpointCommitIdField,
            ElsaRuntimeStorageManifest.RuntimeExecutionIdProjectionLength);

        foreach (var logicalName in new[] { "alteration_jobs_counts", "alteration_job_checkpoint" })
        {
            var index = Assert.Single(jobTable.Indexes, candidate => candidate.LogicalName == logicalName);
            Assert.DoesNotContain(index.Columns, column => column.ColumnLogicalName == envelope.IdLookupKeyColumn);
            Assert.Equal(envelope.StorageScopeColumn, index.Columns[0].ColumnLogicalName);
        }

        var uniqueAdmission = Assert.Single(
            plan.PhysicalStorage.LogicalIndexes,
            index => index.Identity == ElsaRuntimeStorageManifest.WorkflowAlterationPlanIdempotencyUniqueness);
        Assert.True(uniqueAdmission.IsUnique);
        var uniquePhysical = Assert.Single(
            planTable.Indexes,
            index => index.LogicalName == ElsaRuntimeStorageManifest.WorkflowAlterationPlanIdempotencyUniqueness);
        Assert.DoesNotContain(uniquePhysical.Columns, column => column.ColumnLogicalName == envelope.IdLookupKeyColumn);
    }

    private static void AssertProjectionLength(
        PhysicalTableDefinition table,
        string path,
        int expectedLength) =>
        Assert.Equal(
            expectedLength,
            Assert.Single(table.ProjectedColumns, column => column.Path == path).Length);
}
