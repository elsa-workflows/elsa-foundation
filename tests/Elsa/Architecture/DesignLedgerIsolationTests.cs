using Elsa.Activities.Design.Persistence.Groundwork;
using Groundwork.Kernel;
using Elsa.Workflows.Design.Persistence.Groundwork;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Each design catalog owns a private operation ledger. Crash-safety and replay are per-catalog: a
/// commit spans many rows and tables inside one catalog, never across both. That is deliberate — the
/// catalogs are a supported split-database topology, so a cross-catalog transaction could not be
/// honoured anyway and cross-lane work goes through the post-commit outbox instead.
/// <para>
/// The v2 catalog keys a storage unit by (target, unit id) and refuses two shapes under one id. Both
/// lanes once named their ledger <c>designOperation</c> with different tables and different columns, so
/// registering both against one target — what the shipped single-database preset does — threw during
/// service composition. Nothing caught it: every Groundwork composition test project was failing to
/// build behind an unrelated error, so those suites never ran.
/// </para>
/// </summary>
public sealed class DesignLedgerIsolationTests
{
    [Fact]
    public void The_two_design_catalogs_share_no_storage_unit_id()
    {
        var workflow = WorkflowsDesignStorageManifest.CreateUnits().Select(unit => unit.Id.Value).ToArray();
        var activity = ActivitiesDesignStorageManifest.CreateUnits().Select(unit => unit.Id.Value).ToArray();

        var shared = workflow.Intersect(activity, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

        Assert.True(
            shared.Length == 0,
            $"The design catalogs both declare storage unit(s) [{string.Join(", ", shared)}]. A single-target " +
            "host declares both lanes against one target, so identical ids with different schemas fail " +
            "composition. Give each catalog's unit its own id.");
    }

    [Fact]
    public void Each_design_catalog_declares_its_own_operation_ledger()
    {
        Assert.Equal("workflowDesignOperation", WorkflowsDesignStorageManifest.DesignOperationDocumentKind);
        Assert.Equal("activityDesignOperation", ActivitiesDesignStorageManifest.DesignOperationDocumentKind);

        Assert.NotEqual(
            PhysicalNameOf(WorkflowsDesignStorageManifest.CreateUnits(), WorkflowsDesignStorageManifest.DesignOperationDocumentKind),
            PhysicalNameOf(ActivitiesDesignStorageManifest.CreateUnits(), ActivitiesDesignStorageManifest.DesignOperationDocumentKind));
    }

    private static string PhysicalNameOf(IEnumerable<StorageUnit> units, string unitId) =>
        units.Single(unit => unit.Id.Value == unitId).Name;
}
