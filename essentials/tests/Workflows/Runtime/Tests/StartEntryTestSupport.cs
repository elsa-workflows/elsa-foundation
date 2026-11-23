using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// Test start-entry strategies for the trigger binding preflight's start-path rule (spec 238). The runtime knows no
/// activity structure, so tests that place triggers below a composite give that composite one of these kinds.
/// </summary>
internal static class StartEntryTestSupport
{
    /// <summary>A composite kind under which every direct child is an entry, like a Flowchart whose children have no inbound connection.</summary>
    public const string AnyChildKind = "test.any-child";

    /// <summary>A composite kind that always begins at its first child, like a Sequence.</summary>
    public const string FirstChildKind = "test.first-child";

    public static IStructuralStartEntryProvider AnyChild { get; } = new AnyChildStartEntryProvider();
    public static IStructuralStartEntryProvider FirstChild { get; } = new FirstChildStartEntryProvider();

    /// <summary>A copy of <paramref name="node"/> that carries a structure of <paramref name="kind"/>.</summary>
    public static ExecutableNode WithStructure(ExecutableNode node, string kind) =>
        new(
            executableNodeId: node.ExecutableNodeId,
            authoredActivityId: node.AuthoredActivityId,
            activityType: node.ActivityType,
            activityTypeVersion: node.ActivityTypeVersion,
            descriptor: node.Descriptor,
            inputBindings: node.InputBindings,
            metadata: node.Metadata,
            childSlots: node.ChildSlots,
            structure: new ExecutableActivityStructure(kind, "1", JsonSerializer.SerializeToElement(new { })));

    private static IEnumerable<ExecutableNode> Children(ExecutableNode composite) =>
        composite.ChildSlots.SelectMany(slot => slot.Activities);

    private static bool Contains(ExecutableNode node, string nodeId) =>
        StringComparer.Ordinal.Equals(node.ExecutableNodeId, nodeId) || Children(node).Any(child => Contains(child, nodeId));

    private sealed class AnyChildStartEntryProvider : IStructuralStartEntryProvider
    {
        public string StructureKind => AnyChildKind;

        public ExecutableNode? SelectStartChild(ExecutableNode composite, string triggerNodeId) =>
            Children(composite).FirstOrDefault(child => Contains(child, triggerNodeId));
    }

    private sealed class FirstChildStartEntryProvider : IStructuralStartEntryProvider
    {
        public string StructureKind => FirstChildKind;

        public ExecutableNode? SelectStartChild(ExecutableNode composite, string triggerNodeId) =>
            Children(composite).FirstOrDefault();
    }
}
