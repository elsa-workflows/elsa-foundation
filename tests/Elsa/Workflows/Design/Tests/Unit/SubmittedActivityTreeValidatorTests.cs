using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Services;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit;

/// <summary>
/// Direct coverage for the shared <see cref="SubmittedActivityTreeValidator"/> extracted from the
/// EF Core and Groundwork submit commands (issue #417 item 5). Exercises every throw branch,
/// including the nested-child empty-node-id case that the command-level tests do not reach, plus
/// the happy path through a multi-level tree.
/// </summary>
public sealed class SubmittedActivityTreeValidatorTests
{
    private static ActivityNode Node(string nodeId, string versionId) =>
        new(nodeId, versionId, [], []);

    [Fact]
    public void Throws_when_root_is_null()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            SubmittedActivityTreeValidator.Validate(null, new StubStructureService()));
        Assert.Contains("root activity", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Throws_when_root_node_id_is_empty()
    {
        var root = Node(string.Empty, "v1");
        var ex = Assert.Throws<ArgumentException>(() =>
            SubmittedActivityTreeValidator.Validate(root, new StubStructureService()));
        Assert.Contains("node id", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Throws_when_root_activity_version_id_is_empty()
    {
        var root = Node("root", string.Empty);
        var ex = Assert.Throws<ArgumentException>(() =>
            SubmittedActivityTreeValidator.Validate(root, new StubStructureService()));
        Assert.Contains("activity version id", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Throws_when_a_nested_child_node_id_is_empty()
    {
        var root = Node("root", "v1");
        var child = Node(string.Empty, "v2");
        var structure = new StubStructureService((root, [child]));

        var ex = Assert.Throws<ArgumentException>(() =>
            SubmittedActivityTreeValidator.Validate(root, structure));
        Assert.Contains("node id", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Passes_for_a_fully_populated_multi_level_tree()
    {
        var root = Node("root", "v1");
        var child = Node("child", "v2");
        var grandchild = Node("grandchild", "v3");
        var structure = new StubStructureService((root, [child]), (child, [grandchild]));

        SubmittedActivityTreeValidator.Validate(root, structure);
    }

    private sealed class StubStructureService(params (ActivityNode Parent, ActivityNode[] Children)[] edges)
        : IActivityStructureService
    {
        public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity)
        {
            foreach (var (parent, children) in edges)
                if (ReferenceEquals(parent, activity))
                    return [new ActivityChildProjection("Children", children)];
            return [];
        }

        public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections) =>
            throw new NotSupportedException();

        public ActivityNodeStructure? CompileExecutableStructure(ActivityNode activity) =>
            throw new NotSupportedException();

        public IReadOnlyCollection<Expressions.Core.Models.VariableDefinition> ProjectScopedVariables(ActivityNode activity) => [];

        public bool SupportsScopedVariables(ActivityNode activity) => false;
    }
}
