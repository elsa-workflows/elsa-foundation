using Elsa.Workflows.Design.Persistence.Core.Constants;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Commands;
using Xunit;

namespace Elsa.Workflows.Design.Tests;

public sealed class WorkflowReconciliationOperationKeysTests
{
    [Fact]
    public void Materialization_keys_keep_the_format_existing_markers_were_written_under()
    {
        // A permanent delete finds these markers by recomputing their keys, so a format change would strand every
        // marker an existing database already holds.
        Assert.Equal("workflow-reconciliation:definition:5:wf-01", WorkflowReconciliationOperationKeys.Definition("wf-01").Value);
        Assert.Equal("workflow-reconciliation:version:5:wf-0110:sort-key-1", WorkflowReconciliationOperationKeys.Version("wf-01", "sort-key-1").Value);
    }

    [Fact]
    public void Each_metadata_write_gets_a_key_of_its_own_outside_the_per_version_scheme()
    {
        var first = WorkflowReconciliationOperationKeys.DefinitionMetadataWrite("wf-01");
        var second = WorkflowReconciliationOperationKeys.DefinitionMetadataWrite("wf-01");

        Assert.NotEqual(first, second);
        // Existing databases hold "workflow-reconciliation:definition-metadata:<id><sort key>" markers.
        Assert.StartsWith("workflow-reconciliation:definition-metadata-write:5:wf-0132:", first.Value);
    }

    [Fact]
    public void A_draft_clone_key_is_the_same_on_every_node_and_per_version()
    {
        var key = WorkflowReconciliationOperationKeys.DraftClone("wf-01", "sort-key-1");

        Assert.Equal("workflow-reconciliation:draft-clone:5:wf-0110:sort-key-1", key.Value);
        Assert.Equal(key, WorkflowReconciliationOperationKeys.DraftClone("wf-01", "sort-key-1"));
        Assert.NotEqual(key, WorkflowReconciliationOperationKeys.DraftClone("wf-01", "sort-key-2"));
    }

    [Fact]
    public void A_draft_clone_key_for_the_longest_definition_id_fits_the_operation_key_limit()
    {
        var key = WorkflowReconciliationOperationKeys.DraftClone(
            new string('x', WorkflowDefinitionLimits.IdentityMaximumLength),
            Elsa.Primitives.Versioning.SemVer.ToSortKey("999999999.999999999.999999999"));

        DesignOperationKey.Validate(key, EfCloneDraftFromVersionCommand.OperationKind);
    }

    [Fact]
    public void A_metadata_write_key_for_the_longest_definition_id_fits_the_operation_key_limit()
    {
        var key = WorkflowReconciliationOperationKeys.DefinitionMetadataWrite(new string('x', WorkflowDefinitionLimits.IdentityMaximumLength));

        DesignOperationKey.Validate(key, EfSaveWorkflowDefinitionCommand.OperationKind);
    }
}
