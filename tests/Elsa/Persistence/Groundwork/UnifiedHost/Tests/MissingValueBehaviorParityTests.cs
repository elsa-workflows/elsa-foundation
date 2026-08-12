using Elsa.Persistence.Groundwork.ReferenceComposition;
using Groundwork.Core.Indexing;
using Groundwork.Core.PhysicalStorage;
using Xunit;

namespace Elsa.Persistence.Groundwork.UnifiedHost.Tests;

/// <summary>
/// A logical index and the physical index serving it must agree on <see cref="MissingValueBehavior"/>.
/// <para>
/// Groundwork enforces this for scale-bearing indexes only, and it enforces it at composition time, which
/// means an ordinary index can drift apart unnoticed until some host composes the wrong shape. The two
/// defaults are also independent, so an index states its behavior on one side and silently inherits the
/// other. Issue #1296 is what that costs: the default flipped upstream and 106 declarations changed meaning
/// without a line of this repository's code being touched.
/// </para>
/// <para>
/// A disagreement is never harmless. Excluding rows from an index that a query can return is how #1185
/// silently dropped documents on MongoDB and refused to plan at all on SQL Server.
/// </para>
/// </summary>
public sealed class MissingValueBehaviorParityTests
{
    [Fact]
    public void Every_physical_index_agrees_with_the_logical_index_it_serves()
    {
        var mismatched = Indexes()
            .Where(entry => entry.Physical is not null &&
                            entry.Logical.MissingValueBehavior != entry.Physical.MissingValueBehavior)
            .Select(entry =>
                $"{entry.Unit}.{entry.Logical.Identity}: logical declares " +
                $"{entry.Logical.MissingValueBehavior}, physical declares {entry.Physical!.MissingValueBehavior}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(mismatched);
    }

    /// <summary>
    /// The indexes that must keep every row, pinned by name. Each was made inclusive to fix a real defect
    /// (#1185, #1270): its query can match a document whose indexed field is absent, so excluding those rows
    /// returned wrong answers on SQL Server and MongoDB while PostgreSQL quietly disagreed with both.
    /// </summary>
    private static readonly string[] MustIncludeMissingValues =
    [
        "activityDefinition.activity-definition-by-display-name-v2",
        "secret.secret-filtered-list",
        "secret.secret-filtered-list-v2",
        "workflowDefinition.definition-by-name-v2",
        "workflowTriggerBinding.by-artifact-and-trigger-binding-id",
        "workflowTriggerBinding.by-publication-and-trigger-binding-id",
        "workflowTriggerBinding.by-stimulus-and-type",
        "workflowTriggerBinding.by-stimulus-type-and-active",
        // Added by #1296, each because Groundwork's scale-bearing guard proved the query could match
        // rows the index omitted. These were live defects on main, not consequences of this change.
        "bookmarkState.by-stimulus-and-type-and-bookmark-identity",
        "bookmarkState.by-stimulus-type-and-bookmark-identity",
        "workflowExecutionState.by-history-order"
    ];

    [Fact]
    public void The_indexes_that_must_keep_every_row_still_do()
    {
        // Parity is satisfiable by making both sides Excluded, which would silently undo those fixes. This
        // names them so a future sweep has to argue with the test rather than pass it.
        var inclusive = Indexes()
            .Where(entry => entry.Logical.MissingValueBehavior == MissingValueBehavior.IncludedAsNull)
            .Select(entry => $"{entry.Unit}.{entry.Logical.Identity}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(MustIncludeMissingValues.Order(StringComparer.Ordinal), inclusive);
    }

    private static IEnumerable<(string Unit, LogicalIndexDeclaration Logical, PhysicalIndexDefinition? Physical)> Indexes()
    {
        var manifest = new GroundworkAllFeaturesWithIdentityAndDiagnosticsDeploymentSchema().CreateManifest();
        foreach (var unit in manifest.StorageUnits)
        {
            if (unit.PhysicalStorage is not { } storage)
                continue;

            var definition = (storage.Policy as PhysicalStoragePolicy.ExplicitPolicy)?.Definition;
            foreach (var logical in storage.LogicalIndexes)
            {
                yield return (
                    unit.Identity.Value,
                    logical,
                    definition?.Indexes.FirstOrDefault(index =>
                        string.Equals(index.LogicalName, logical.Identity, StringComparison.Ordinal)));
            }
        }
    }
}
