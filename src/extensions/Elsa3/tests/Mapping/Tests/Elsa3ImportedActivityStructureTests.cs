using Elsa.Workflows.Design.Core.Models;
using Elsa3.Activities.Design.Import.Models;
using Xunit;

namespace Elsa3.Mapping.Tests;

/// <summary>
/// The Elsa 3 import's one definition of its nested structure: what <see cref="Elsa3ImportedActivityStructure.Create"/>
/// writes, <see cref="Elsa3ImportedActivityStructure.Nodes"/> reads back, every node exactly once and in document order,
/// siblings and mixed depths included.
/// </summary>
public sealed class Elsa3ImportedActivityStructureTests
{
    [Fact]
    public void Nodes_reads_back_every_node_create_wrote_once_in_document_order()
    {
        // root -> [a, b -> [b1, b2], c]
        var b = Node("b", Node("b1"), Node("b2"));
        var root = Node("root", Node("a"), b, Node("c"));

        Assert.Equal(["root", "a", "b", "b1", "b2", "c"], Elsa3ImportedActivityStructure.Nodes(root).Select(node => node.NodeId));
    }

    [Fact]
    public void A_node_without_children_has_no_structure()
    {
        var leaf = Node("leaf");

        Assert.Null(leaf.Structure);
        Assert.Equal(["leaf"], Elsa3ImportedActivityStructure.Nodes(leaf).Select(node => node.NodeId));
    }

    [Fact]
    public void A_null_root_is_refused_when_called_not_when_enumerated() =>
        Assert.Throws<ArgumentNullException>(() => Elsa3ImportedActivityStructure.Nodes(null!));

    private static ActivityNode Node(string nodeId, params ActivityNode[] children) =>
        new(nodeId, "activity-version", [], [], Elsa3ImportedActivityStructure.Create(children));
}
