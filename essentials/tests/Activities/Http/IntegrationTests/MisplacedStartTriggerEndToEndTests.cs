using Elsa.Workflows.Runtime.Core.Exceptions;
using Xunit;

namespace Elsa.Activities.Http.IntegrationTests;

/// <summary>
/// Spec 238, User Story 4, against the real host: the real trigger extractor, with the Flowchart and Sequence
/// start-entry strategies their features register, refuses a start trigger that no run could start from. It accepts
/// every placement on the start path.
/// </summary>
public sealed class MisplacedStartTriggerEndToEndTests : IAsyncLifetime
{
    private const string Method = "POST";

    private HttpEndpointHostFixture _fixture = null!;

    public async Task InitializeAsync() => _fixture = await HttpEndpointHostFixture.StartAsync();

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    [Fact]
    public async Task SequenceChildZero_StartTrigger_IsAccepted()
    {
        const string artifactId = "artifact-sequence-first";
        var root = _fixture.NewSequenceNode("node-sequence",
        [
            _fixture.NewStartEndpointNode(artifactId, "placement/sequence-first", Method, "node-start"),
            _fixture.NewWaitEndpointNode(artifactId, "placement/sequence-first/wait", Method, "node-wait")
        ]);

        await _fixture.PublishRootAsync(artifactId, root);

        Assert.Single(await _fixture.ListTriggerBindingsAsync(artifactId));
    }

    [Fact]
    public async Task SequenceChildOne_StartTrigger_IsRefusedNamingTheSequence()
    {
        const string artifactId = "artifact-sequence-second";
        var root = _fixture.NewSequenceNode("node-sequence",
        [
            _fixture.NewWaitEndpointNode(artifactId, "placement/sequence-second/wait", Method, "node-wait"),
            _fixture.NewStartEndpointNode(artifactId, "placement/sequence-second", Method, "node-start")
        ]);

        var exception = await Assert.ThrowsAsync<WorkflowTriggerPreflightException>(() => _fixture.PublishRootAsync(artifactId, root));

        AssertPlacement(exception, "node-start", "node-sequence");
        Assert.Empty(await _fixture.ListTriggerBindingsAsync(artifactId));
    }

    [Fact]
    public async Task FlowchartStartTrigger_WithInboundConnection_ThatIsNotTheStartNode_IsRefused()
    {
        const string artifactId = "artifact-flowchart-inbound";
        var root = _fixture.NewFlowchartNode("node-flowchart",
            [
                _fixture.NewWaitEndpointNode(artifactId, "placement/inbound/wait", Method, "node-wait"),
                _fixture.NewStartEndpointNode(artifactId, "placement/inbound", Method, "node-start")
            ],
            [("node-wait", "node-start")],
            startNodeId: "node-wait");

        var exception = await Assert.ThrowsAsync<WorkflowTriggerPreflightException>(() => _fixture.PublishRootAsync(artifactId, root));

        AssertPlacement(exception, "node-start", "node-flowchart");
    }

    [Fact]
    public async Task NestedFlowchart_AsTheOuterStartNode_WithAnEntryTrigger_IsAccepted()
    {
        const string artifactId = "artifact-nested-start";
        var inner = _fixture.NewFlowchartNode("node-inner",
            [
                _fixture.NewWaitEndpointNode(artifactId, "placement/nested-start/wait", Method, "node-inner-wait"),
                _fixture.NewStartEndpointNode(artifactId, "placement/nested-start", Method, "node-inner-start")
            ],
            [],
            startNodeId: "node-inner-wait");
        var root = _fixture.NewFlowchartNode("node-outer",
            [inner, _fixture.NewWaitEndpointNode(artifactId, "placement/nested-start/after", Method, "node-after")],
            [("node-inner", "node-after")],
            startNodeId: "node-inner");

        await _fixture.PublishRootAsync(artifactId, root);

        Assert.Single(await _fixture.ListTriggerBindingsAsync(artifactId));
    }

    [Fact]
    public async Task NestedFlowchart_ReachedThroughANonStartChild_IsRefusedNamingTheOuterFlowchart()
    {
        const string artifactId = "artifact-nested-off-path";
        var inner = _fixture.NewFlowchartNode("node-inner",
            [_fixture.NewStartEndpointNode(artifactId, "placement/nested-off-path", Method, "node-inner-start")],
            [],
            startNodeId: "node-inner-start");
        var root = _fixture.NewFlowchartNode("node-outer",
            [_fixture.NewWaitEndpointNode(artifactId, "placement/nested-off-path/wait", Method, "node-before"), inner],
            [("node-before", "node-inner")],
            startNodeId: "node-before");

        var exception = await Assert.ThrowsAsync<WorkflowTriggerPreflightException>(() => _fixture.PublishRootAsync(artifactId, root));

        AssertPlacement(exception, "node-inner-start", "node-outer");
    }

    private static void AssertPlacement(WorkflowTriggerPreflightException exception, string triggerNodeId, string compositeNodeId)
    {
        Assert.Equal(WorkflowTriggerPreflightException.PlacementFacet, exception.Facet);
        Assert.Equal(triggerNodeId, exception.ExecutableNodeId);
        Assert.Equal(compositeNodeId, exception.CompositeNodeId);
    }
}
