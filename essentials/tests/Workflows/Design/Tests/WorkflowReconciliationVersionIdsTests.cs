using Elsa.Workflows.Design.Persistence.Core.Constants;
using Elsa.Workflows.Design.Reconciliation.Services;
using Xunit;

namespace Elsa.Workflows.Design.Tests;

public sealed class WorkflowReconciliationVersionIdsTests
{
    [Fact]
    public void The_id_keeps_the_format_every_node_derives()
    {
        // Nodes on different releases must derive the same id, or their materialization requests conflict again (#2189).
        Assert.Equal(
            "wfver-bead209bca4b82fc4283efc60632f73134578970450a63124a65f5b35910f1e6",
            WorkflowReconciliationVersionIds.For("wf-01", "sort-key-1"));
    }

    [Fact]
    public void Each_definition_and_sort_key_gets_an_id_of_its_own()
    {
        string[] ids =
        [
            WorkflowReconciliationVersionIds.For("wf-01", "sort-key-1"),
            WorkflowReconciliationVersionIds.For("wf-01", "sort-key-2"),
            WorkflowReconciliationVersionIds.For("wf-02", "sort-key-1"),
            // The parts are framed by their length, so moving the boundary between them changes the id.
            WorkflowReconciliationVersionIds.For("wf-01s", "ort-key-1")
        ];

        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void The_id_of_the_longest_definition_id_fits_the_identity_limit()
    {
        var id = WorkflowReconciliationVersionIds.For(new string('x', WorkflowDefinitionLimits.IdentityMaximumLength), new string('0', 128));

        Assert.True(id.Length <= WorkflowDefinitionLimits.IdentityMaximumLength);
    }
}
