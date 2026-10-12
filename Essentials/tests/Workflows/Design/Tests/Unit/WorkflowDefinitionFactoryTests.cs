using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit;

/// <summary>
/// Provider-neutral conversion (T070) of the soft-delete copy assertions that lived in the
/// EF-referencing <c>WorkflowDefinitionSoftDeleteTests</c>. <see cref="WorkflowDefinition.From"/> is a
/// pure domain-model factory: it never touches a persistence provider, so these assertions are the
/// EF-independent home for its copy semantics after the EF store is removed (T072).
/// </summary>
public sealed class WorkflowDefinitionFactoryTests
{
    [Fact]
    public void From_preserves_soft_delete_metadata_round_trip()
    {
        var deletedAt = DateTimeOffset.UtcNow;
        var source = new WorkflowDefinition
        {
            Id = "def-1",
            Name = "Deleted",
            Description = "desc",
            DeletedAt = deletedAt,
            DeletedReason = "gone",
        };

        var copy = WorkflowDefinition.From(source);

        Assert.Equal(source.Id, copy.Id);
        Assert.Equal(source.Name, copy.Name);
        Assert.Equal(source.Description, copy.Description);
        Assert.Equal(deletedAt, copy.DeletedAt);
        Assert.Equal("gone", copy.DeletedReason);
    }

    [Fact]
    public void From_leaves_soft_delete_metadata_unset_for_non_deleted_source()
    {
        var source = new WorkflowDefinition { Id = "def-2", Name = "Active" };

        var copy = WorkflowDefinition.From(source);

        Assert.Null(copy.DeletedAt);
        Assert.Null(copy.DeletedReason);
    }

    [Fact]
    public void From_leaves_soft_delete_metadata_unset_for_non_entity_source()
    {
        // Both real callers pass a read-model (WorkflowDefinitionModel), not a WorkflowDefinition entity,
        // so `source is WorkflowDefinition` is false and the soft-delete copy branch is skipped. This pins
        // the branch the callers actually hit — the entity-source tests above cover the copy branch.
        var source = new WorkflowDefinitionModel("def-3", "Model", "desc");

        var copy = WorkflowDefinition.From(source);

        Assert.Equal("def-3", copy.Id);
        Assert.Null(copy.DeletedAt);
        Assert.Null(copy.DeletedReason);
    }
}
