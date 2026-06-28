using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Validators;
using Xunit;
using static Elsa.Workflows.Design.Tests.Unit.BaselineValidatorTests.ValidatorTestHelpers;

namespace Elsa.Workflows.Design.Tests.Unit.BaselineValidatorTests;

/// <summary>
/// Reference preservation and repair across structural edits (#211): move preserves a reference and
/// marks it invalid (no name-based retargeting); copy/import remaps internal declaring scope ids to
/// the copied node ids while preserving reference keys; external references are left for validation.
/// </summary>
public sealed class ScopedVariableRepairTests
{
    private static readonly IActivityStructureService Structure = StructureService();

    private static VariableReference ReferenceOf(ArgumentState argument)
    {
        Assert.True(VariableReference.TryParse(argument.Value.Value, out var reference));
        return reference!;
    }

    private static ActivityNode Child(string nodeId, string scopeId, string referenceKey = "var-c") =>
        Node(nodeId, inputs: [VariableInput("body", new VariableReference(referenceKey, scopeId))]);

    [Fact]
    public async Task Moving_an_activity_out_of_its_declaring_scope_preserves_the_reference_and_marks_it_invalid()
    {
        // 'moved' references container 'c1' but is now a sibling of 'c1' (moved out of it).
        var container = Node("c1", childActivities: [Node("kept")], containerVariables: [Variable("var-c", "Local")]);
        var moved = Child("moved", "c1");
        var state = State(activities: [container, moved], variables: []);

        var errors = await Validate(new VariableExpressionResolverValidator(Options(), Walker(), Resolver()), state);

        // Reference is reported as invalid (out of scope) — not silently retargeted...
        var error = Assert.Single(errors);
        Assert.Equal("moved/inputs/body", error.Path);
        Assert.Equal("Expressions/UnresolvedVariable", error.Type);
        Assert.Contains("not visible", error.Message, StringComparison.Ordinal);

        // ...and the original reference is preserved unchanged for deliberate repair.
        var reference = ReferenceOf(moved.Inputs.Single());
        Assert.Equal("c1", reference.DeclaringScopeId);
        Assert.Equal("var-c", reference.ReferenceKey);
    }

    [Fact]
    public void Copying_a_container_subtree_remaps_internal_references_to_the_copied_node_ids()
    {
        var child = Child("child", "c1");
        var container = Node("c1", childActivities: [child], containerVariables: [Variable("var-c", "Local")]);
        var remap = new Dictionary<string, string> { ["c1"] = "c1-copy", ["child"] = "child-copy" };

        var copied = Remapper().Remap(container, remap);

        Assert.Equal("c1-copy", copied.NodeId);
        var copiedChild = Structure.ProjectChildren(copied).SelectMany(slot => slot.Activities).Single();
        Assert.Equal("child-copy", copiedChild.NodeId);

        // Internal reference now points at the copied container, with its reference key preserved.
        var reference = ReferenceOf(copiedChild.Inputs.Single());
        Assert.Equal("c1-copy", reference.DeclaringScopeId);
        Assert.Equal("var-c", reference.ReferenceKey);
    }

    [Fact]
    public void Copying_leaves_external_references_unchanged()
    {
        // 'child' references an outer scope that is NOT part of the copied subtree.
        var child = Child("child", "outer-scope");
        var container = Node("c1", childActivities: [child], containerVariables: [Variable("var-c", "Local")]);
        var remap = new Dictionary<string, string> { ["c1"] = "c1-copy", ["child"] = "child-copy" };

        var copied = Remapper().Remap(container, remap);

        var copiedChild = Structure.ProjectChildren(copied).SelectMany(slot => slot.Activities).Single();
        var reference = ReferenceOf(copiedChild.Inputs.Single());
        Assert.Equal("outer-scope", reference.DeclaringScopeId); // external reference left for validation
    }
}
