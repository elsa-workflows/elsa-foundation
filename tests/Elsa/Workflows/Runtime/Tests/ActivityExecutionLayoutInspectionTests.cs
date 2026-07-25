using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Api.Services;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public class ActivityExecutionLayoutInspectionTests
{
    [Fact]
    public async Task Layout_Uses_Executed_Source_Reference_And_Exact_Executable_Node_Join()
    {
        var workflowStates = new InMemoryWorkflowExecutionStateStore();
        var executables = new InMemoryWorkflowExecutableStore();
        var references = new InMemoryWorkflowExecutableSourceReferenceStore();
        var hierarchy = HierarchyStore();
        var origin = Origin();
        await workflowStates.SaveAsync(WorkflowState("source-executed"));
        await executables.SaveAsync(Executable());
        await hierarchy.SaveAsync(ActivityExecutionHierarchyProjector.FromInspection(
            ActivityExecutionInspectionProjectionTests.Projection("outer", "outer", null, 1, ActivityExecutionStatus.Completed, boundary: true)));
        await references.SaveAsync(Reference("source-executed", origin, x: 100));
        await references.SaveAsync(Reference("source-current", origin, x: 999));

        var reader = new ActivityExecutionLayoutReader(workflowStates, hierarchy, executables, references, new Authorization(structure: true, values: false));
        var view = await reader.ReadAsync("wf", "outer", default);

        Assert.NotNull(view);
        Assert.Equal("ExecutedReference", view.Selection);
        Assert.Equal("source-executed", view.SourceReferenceId);
        Assert.Equal(100, Assert.Single(view.Nodes).X);
    }

    [Fact]
    public async Task Pinned_Historical_Layout_Projects_Flowchart_Connections_To_Exact_Placed_Executable_Node_Ids()
    {
        var workflowStates = new InMemoryWorkflowExecutionStateStore();
        var executables = new InMemoryWorkflowExecutableStore();
        var references = new InMemoryWorkflowExecutableSourceReferenceStore();
        var hierarchy = HierarchyStore();
        var origin = Origin();
        await workflowStates.SaveAsync(WorkflowState("source-executed"));
        await executables.SaveAsync(FlowchartExecutable());
        await hierarchy.SaveAsync(ActivityExecutionHierarchyProjector.FromInspection(
            ActivityExecutionInspectionProjectionTests.Projection("outer", "placed-flowchart", null, 1, ActivityExecutionStatus.Completed, boundary: true)));
        await references.SaveAsync(FlowchartReference("source-executed", origin, "placed-source", "placed-target", 100));
        await references.SaveAsync(FlowchartReference("source-current", origin, "decoy-source", "decoy-target", 999));

        var reader = new ActivityExecutionLayoutReader(workflowStates, hierarchy, executables, references, new Authorization(true, false));
        var view = await reader.ReadAsync("wf", "outer", default);

        Assert.NotNull(view);
        Assert.Equal("source-executed", view!.SourceReferenceId);
        Assert.Equal(100, view.Nodes.Single(x => x.ExecutableNodeId == "placed-source").X);
        var connection = Assert.Single(view.Connections);
        Assert.Equal("placed-source", connection.Source.ExecutableNodeId);
        Assert.Equal("Approved", connection.Source.Port);
        Assert.Equal("placed-target", connection.Target.ExecutableNodeId);
        Assert.Equal("Input", connection.Target.Port);
        Assert.Collection(
            connection.Vertices!,
            vertex => Assert.Equal(new ActivityExecutionLayoutVertex(120.5, 64.25), vertex),
            vertex => Assert.Equal(new ActivityExecutionLayoutVertex(240, 96), vertex));
    }

    [Fact]
    public async Task Layout_Omits_Malformed_And_Unknown_Structure_Payloads()
    {
        var workflowStates = new InMemoryWorkflowExecutionStateStore();
        var executables = new InMemoryWorkflowExecutableStore();
        var references = new InMemoryWorkflowExecutableSourceReferenceStore();
        var hierarchy = HierarchyStore();
        var origin = Origin();
        await workflowStates.SaveAsync(WorkflowState("source-executed"));
        await executables.SaveAsync(UnsafeStructureExecutable());
        await hierarchy.SaveAsync(ActivityExecutionHierarchyProjector.FromInspection(
            ActivityExecutionInspectionProjectionTests.Projection("outer", "unknown-container", null, 1, ActivityExecutionStatus.Completed, boundary: true)));
        await references.SaveAsync(UnsafeStructureReference("source-executed", origin));

        var reader = new ActivityExecutionLayoutReader(workflowStates, hierarchy, executables, references, new Authorization(true, false));
        var view = await reader.ReadAsync("wf", "outer", default);

        Assert.NotNull(view);
        Assert.Empty(view!.Connections);
        var wire = JsonSerializer.Serialize(view, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("structure-secret-must-not-leak", wire, StringComparison.Ordinal);
        Assert.DoesNotContain("unknown-source", wire, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_Historical_Sidecar_Returns_Explicit_Automatic_Layout()
    {
        var workflowStates = new InMemoryWorkflowExecutionStateStore();
        var executables = new InMemoryWorkflowExecutableStore();
        var references = new InMemoryWorkflowExecutableSourceReferenceStore();
        var hierarchy = HierarchyStore();
        await workflowStates.SaveAsync(WorkflowState("legacy-reference"));
        await executables.SaveAsync(Executable());
        await hierarchy.SaveAsync(ActivityExecutionHierarchyProjector.FromInspection(
            ActivityExecutionInspectionProjectionTests.Projection("outer", "outer", null, 1, ActivityExecutionStatus.Completed, boundary: true)));
        await references.SaveAsync(Reference("legacy-reference", ActivityInvocationOrigin.Empty, 50));

        var reader = new ActivityExecutionLayoutReader(workflowStates, hierarchy, executables, references, new Authorization(true, false));
        var view = await reader.ReadAsync("wf", "outer", default);

        Assert.Equal("Automatic", view!.Selection);
        Assert.Empty(view.Nodes);
    }

    [Fact]
    public async Task Pinned_structure_includes_authored_nodes_that_never_executed_or_received_geometry()
    {
        var workflowStates = new InMemoryWorkflowExecutionStateStore();
        var executables = new InMemoryWorkflowExecutableStore();
        var references = new InMemoryWorkflowExecutableSourceReferenceStore();
        var hierarchy = HierarchyStore();
        var origin = Origin();
        var originMetadata = new Dictionary<string, string>
        {
            ["activity.invocationOrigin"] = JsonSerializer.Serialize(origin.Segments)
        };
        var neverExecuted = Node("node-never-executed", "authored-never-executed", metadata: originMetadata);
        var root = Node(
            "node-outer",
            "authored-outer",
            structure: new(
                "elsa.flowchart.structure",
                "1.0.0",
                JsonSerializer.SerializeToElement(new
                {
                    connections = new[]
                    {
                        new
                        {
                            source = new { nodeId = "authored-outer" },
                            target = new { nodeId = "authored-never-executed" }
                        }
                    }
                })),
            children: [neverExecuted],
            metadata: originMetadata);
        await workflowStates.SaveAsync(WorkflowState("source-executed"));
        await executables.SaveAsync(WorkflowExecutable(root));
        await hierarchy.SaveAsync(ActivityExecutionHierarchyProjector.FromInspection(
            ActivityExecutionInspectionProjectionTests.Projection("outer", "outer", null, 1, ActivityExecutionStatus.Completed, boundary: true)));
        await references.SaveAsync(Reference("source-executed", origin, x: 100));

        var reader = new ActivityExecutionLayoutReader(workflowStates, hierarchy, executables, references, new Authorization(true, false));
        var view = await reader.ReadAsync("wf", "outer", default);

        Assert.Equal(2, view!.Nodes.Count);
        var unexecuted = view.Nodes.Single(x => x.ExecutableNodeId == "node-never-executed");
        Assert.False(unexecuted.HasPinnedGeometry);
        Assert.Equal("elsa.test", unexecuted.ActivityType);
        Assert.Equal("node-never-executed", Assert.Single(view.Connections).Target.ExecutableNodeId);
    }

    [Fact]
    public async Task Pinned_structure_remains_available_when_the_optional_layout_sidecar_is_missing()
    {
        var workflowStates = new InMemoryWorkflowExecutionStateStore();
        var executables = new InMemoryWorkflowExecutableStore();
        var references = new InMemoryWorkflowExecutableSourceReferenceStore();
        var hierarchy = HierarchyStore();
        var origin = Origin();
        var metadata = new Dictionary<string, string>
        {
            ["activity.invocationOrigin"] = JsonSerializer.Serialize(origin.Segments)
        };
        var child = Node("node-child", "authored-child", metadata: metadata);
        await workflowStates.SaveAsync(WorkflowState("missing-reference"));
        await executables.SaveAsync(WorkflowExecutable(Node("node-outer", "authored-outer", children: [child], metadata: metadata)));
        await hierarchy.SaveAsync(ActivityExecutionHierarchyProjector.FromInspection(
            ActivityExecutionInspectionProjectionTests.Projection("outer", "outer", null, 1, ActivityExecutionStatus.Completed, boundary: true)));

        var reader = new ActivityExecutionLayoutReader(workflowStates, hierarchy, executables, references, new Authorization(true, false));
        var view = await reader.ReadAsync("wf", "outer", default);

        Assert.Equal("Automatic", view!.Selection);
        Assert.Equal(2, view.Nodes.Count);
        Assert.All(view.Nodes, node => Assert.False(node.HasPinnedGeometry));
    }

    [Fact]
    public async Task Legacy_Execution_Without_Exact_Source_Reference_Evidence_Does_Not_Guess_From_A_Single_Current_Reference()
    {
        var workflowStates = new InMemoryWorkflowExecutionStateStore();
        var executables = new InMemoryWorkflowExecutableStore();
        var references = new InMemoryWorkflowExecutableSourceReferenceStore();
        var hierarchy = HierarchyStore();
        var legacy = WorkflowState("unused") with { SystemMetadata = new Dictionary<string, string>() };
        await workflowStates.SaveAsync(legacy);
        await executables.SaveAsync(Executable());
        await hierarchy.SaveAsync(ActivityExecutionHierarchyProjector.FromInspection(
            ActivityExecutionInspectionProjectionTests.Projection("outer", "outer", null, 1, ActivityExecutionStatus.Completed, boundary: true)));
        await references.SaveAsync(Reference("only-current-reference", Origin(), 999));

        var reader = new ActivityExecutionLayoutReader(workflowStates, hierarchy, executables, references, new Authorization(true, false));
        var view = await reader.ReadAsync("wf", "outer", default);

        Assert.NotNull(view);
        Assert.Equal("Automatic", view.Selection);
        Assert.Equal(string.Empty, view.SourceReferenceId);
        Assert.Empty(view.Nodes);
    }

    [Fact]
    public async Task Structure_Authorization_Is_Required_Without_Consulting_Value_Authorization()
    {
        var workflowStates = new InMemoryWorkflowExecutionStateStore();
        await workflowStates.SaveAsync(WorkflowState("source-executed"));
        var reader = new ActivityExecutionLayoutReader(
            workflowStates,
            HierarchyStore(),
            new InMemoryWorkflowExecutableStore(),
            new InMemoryWorkflowExecutableSourceReferenceStore(),
            new Authorization(structure: false, values: true));

        Assert.Null(await reader.ReadAsync("wf", "outer", default));
    }

    private static RuntimeInMemoryActivityExecutionHierarchyStore HierarchyStore() =>
        new(new HmacActivityExecutionHierarchyCursorCodec(Options.Create(new ActivityExecutionHierarchyCursorOptions
        {
            SigningKey = "layout-signing-key-that-is-at-least-thirty-two-bytes"
        })));

    private static WorkflowExecutionState WorkflowState(string sourceReferenceId) => new(
        "wf",
        new("artifact", "workflow-def", "workflow-ver", "1", "hash"),
        WorkflowExecutionStatus.Completed,
        null,
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch,
        null,
        null,
        "tenant-a",
        new Dictionary<string, string> { [RuntimeMetadataKeys.SourceReferenceId] = sourceReferenceId });

    private static WorkflowExecutable Executable()
    {
        var node = new ExecutableNode(
            "node-outer",
            "authored-outer",
            "elsa.graph-activity",
            "1",
            new RuntimeActivityDescriptor("elsa.graph-activity", "1", JsonSerializer.SerializeToElement(new { })),
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, RuntimeOutputCapture>(),
            new Dictionary<string, string>());
        return new(
            new("artifact", "workflow-def", "workflow-ver", "1", "hash"),
            node,
            new Dictionary<string, WorkflowExecutableResumeTarget>(),
            DateTimeOffset.UnixEpoch,
            new Dictionary<string, string>(),
            IncidentStrategyBuiltIns.FaultReference);
    }

    private static WorkflowExecutable FlowchartExecutable()
    {
        var source = Node("placed-source", "authored-source");
        var target = Node("placed-target", "authored-target");
        var decoySource = Node("decoy-source", "decoy-authored-source");
        var decoyTarget = Node("decoy-target", "decoy-authored-target");
        var flowchart = Node(
            "placed-flowchart",
            "authored-flowchart",
            new ExecutableActivityStructure(
                "elsa.flowchart.structure",
                "1.0.0",
                JsonSerializer.SerializeToElement(new
                {
                    connections = new object[]
                    {
                        new
                        {
                            source = new { nodeId = "template-source", port = " Approved " },
                            target = new { nodeId = "authored-target", port = "Input" },
                            vertices = new object[]
                            {
                                new { x = 120.5, y = 64.25 },
                                new { x = 240d, y = 96d },
                                new { x = "not-a-number", y = 1d }
                            }
                        },
                        new
                        {
                            source = new { nodeId = "not-in-executed-segment", port = "Done" },
                            target = new { nodeId = "authored-target", port = "Input" }
                        }
                    },
                    secret = "structure-secret-must-not-leak"
                })),
            [source, target, decoySource, decoyTarget]);
        return WorkflowExecutable(flowchart);
    }

    private static WorkflowExecutable UnsafeStructureExecutable()
    {
        var unknown = Node(
            "unknown-container",
            "unknown-authored",
            new ExecutableActivityStructure(
                "elsa.flowchart.structure.near-match",
                "1.0.0",
                JsonSerializer.SerializeToElement(new
                {
                    connections = new[]
                    {
                        new
                        {
                            source = new { nodeId = "unknown-source" },
                            target = new { nodeId = "unknown-target" }
                        }
                    },
                    secret = "structure-secret-must-not-leak"
                })));
        var malformed = Node(
            "malformed-container",
            "malformed-authored",
            new ExecutableActivityStructure(
                "elsa.flowchart.structure",
                "1.0.0",
                JsonSerializer.SerializeToElement(new { connections = "not-an-array", secret = "structure-secret-must-not-leak" })));
        var unsupportedSchema = Node(
            "unsupported-schema-container",
            "unsupported-schema-authored",
            new ExecutableActivityStructure(
                "elsa.flowchart.structure",
                "2.0.0",
                JsonSerializer.SerializeToElement(new
                {
                    connections = new[]
                    {
                        new
                        {
                            source = new { nodeId = "unknown-source" },
                            target = new { nodeId = "unknown-target" }
                        }
                    }
                })));
        return WorkflowExecutable(Node("root", "root", children: [unknown, malformed, unsupportedSchema]));
    }

    private static WorkflowExecutable WorkflowExecutable(ExecutableNode root) => new(
        new("artifact", "workflow-def", "workflow-ver", "1", "hash"),
        root,
        new Dictionary<string, WorkflowExecutableResumeTarget>(),
        DateTimeOffset.UnixEpoch,
        new Dictionary<string, string>(),
        IncidentStrategyBuiltIns.FaultReference);

    private static ExecutableNode Node(
        string executableNodeId,
        string authoredActivityId,
        ExecutableActivityStructure? structure = null,
        IReadOnlyCollection<ExecutableNode>? children = null,
        IReadOnlyDictionary<string, string>? metadata = null) => new(
        executableNodeId,
        authoredActivityId,
        "elsa.test",
        "1",
        new RuntimeActivityDescriptor("elsa.test", "1", JsonSerializer.SerializeToElement(new { })),
        new Dictionary<string, RuntimeInputBinding>(),
        new Dictionary<string, RuntimeOutputCapture>(),
        metadata ?? new Dictionary<string, string>(),
        children is null ? [] : [new ExecutableChildSlot("Activities", children)],
        structure);

    private static WorkflowExecutableSourceReference Reference(string id, ActivityInvocationOrigin origin, double x) => new(
        id,
        "artifact",
        "WorkflowDefinitionVersion",
        "workflow-ver",
        "1",
        "workflow-def",
        "workflow-ver",
        "1",
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch,
        WorkflowExecutableReferenceScope.Published,
        LayoutSidecar: new(new[]
        {
            new ExecutableLayoutBoundarySegment(
                "layout-boundary",
                origin,
                "sha256-template",
                new[] { new ExecutableActivityLayoutRecord("template-node", "authored-outer", "node-outer", x, 80) },
                [])
        }));

    private static WorkflowExecutableSourceReference FlowchartReference(
        string id,
        ActivityInvocationOrigin origin,
        string sourceExecutableNodeId,
        string targetExecutableNodeId,
        double x) => Reference(id, origin,
        [
            new("template-flowchart", "authored-flowchart", "placed-flowchart", 20, 40),
            new("template-source", "authored-source", sourceExecutableNodeId, x, 80),
            new("template-target", "authored-target", targetExecutableNodeId, x + 200, 80)
        ]);

    private static WorkflowExecutableSourceReference UnsafeStructureReference(string id, ActivityInvocationOrigin origin) => Reference(id, origin,
    [
        new("unknown-container", "unknown-authored", "unknown-container", 0, 0),
        new("malformed-container", "malformed-authored", "malformed-container", 0, 0),
        new("unsupported-schema-container", "unsupported-schema-authored", "unsupported-schema-container", 0, 0)
    ]);

    private static WorkflowExecutableSourceReference Reference(
        string id,
        ActivityInvocationOrigin origin,
        IReadOnlyList<ExecutableActivityLayoutRecord> records) => new(
        id,
        "artifact",
        "WorkflowDefinitionVersion",
        "workflow-ver",
        "1",
        "workflow-def",
        "workflow-ver",
        "1",
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch,
        WorkflowExecutableReferenceScope.Published,
        LayoutSidecar: new(new[]
        {
            new ExecutableLayoutBoundarySegment("layout-boundary", origin, "sha256-template", records, [])
        }));

    private static ActivityInvocationOrigin Origin() => new(new[]
    {
        new ActivityInvocationOriginSegment(ActivityInvocationOriginSegmentKind.WorkflowRoot, "workflow-ver"),
        new ActivityInvocationOriginSegment(ActivityInvocationOriginSegmentKind.AuthoredNode, "node-boundary")
    });

    private sealed class Authorization(bool structure, bool values) : IActivityExecutionInspectionAuthorizationContext
    {
        public string TenantScope => "tenant:a";
        public string AuthorizationProfile => $"structure:{structure};values:{values}";
        public string AuditSubject => "test";
        public string RequestCorrelationId => "test-request";
        public bool CanInspectStructure(WorkflowExecutionState workflowExecution) => structure;
        public bool CanInspectSensitiveValues(WorkflowExecutionState workflowExecution) => values;
        public bool CanResolveSensitiveValuePayloads(WorkflowExecutionState workflowExecution) => false;
    }
}
