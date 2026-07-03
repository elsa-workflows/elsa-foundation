using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class WorkflowTriggerBindingExtractorTests
{
    private readonly WorkflowExecutableIdentity _identity = new("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:artifact");

    [Fact]
    public void Extract_ReturnsBinding_ForTriggerNodeRecognizedByProvider()
    {
        var extractor = new WorkflowTriggerBindingExtractor([new FakeProvider("Elsa.Event", "Event", "sha256:event:hello", "order-7")]);
        var executable = Executable(TriggerNode("node-event", "Elsa.Event"));

        var binding = Assert.Single(extractor.Extract(executable));

        Assert.Equal(WorkflowTriggerBinding.BuildId("artifact-1", "node-event"), binding.TriggerBindingId);
        Assert.Equal("artifact-1", binding.ArtifactId);
        Assert.Equal("node-event", binding.ExecutableNodeId);
        Assert.Equal("Event", binding.StimulusType);
        Assert.Equal("sha256:event:hello", binding.StimulusHash);
        Assert.Equal("order-7", binding.CorrelationScope);
    }

    [Fact]
    public void Extract_IgnoresNonTriggerNodes()
    {
        var extractor = new WorkflowTriggerBindingExtractor([new FakeProvider("Elsa.Event", "Event", "sha256:event:hello")]);
        var executable = Executable(ActionNode("node-action", "Elsa.WriteLine"));

        Assert.Empty(extractor.Extract(executable));
    }

    [Fact]
    public void Extract_WalksChildSlots()
    {
        var extractor = new WorkflowTriggerBindingExtractor([new FakeProvider("Elsa.Event", "Event", "sha256:event:hello")]);
        var root = ActionNode("root", "Elsa.Sequence", TriggerNode("child-event", "Elsa.Event"));
        var executable = Executable(root);

        var binding = Assert.Single(extractor.Extract(executable));
        Assert.Equal("child-event", binding.ExecutableNodeId);
    }

    [Fact]
    public void Extract_Throws_WhenTriggerNodeHasNoDescribingProvider()
    {
        // A node marked as a trigger that no provider recognizes must fail the publish, not be silently dropped.
        var extractor = new WorkflowTriggerBindingExtractor([new FakeProvider("Elsa.Event", "Event", "sha256:event:hello")]);
        var executable = Executable(TriggerNode("node-mystery", "Elsa.Unknown"));

        var exception = Assert.Throws<WorkflowTriggerExtractionException>(() => extractor.Extract(executable));
        Assert.Contains("node-mystery", exception.Message);
    }

    private WorkflowExecutable Executable(ExecutableNode root) =>
        new(
            identity: _identity,
            rootActivity: root,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: DateTimeOffset.UnixEpoch,
            publishedAt: DateTimeOffset.UnixEpoch,
            compatibilityMetadata: new Dictionary<string, string>());

    private static ExecutableNode TriggerNode(string nodeId, string activityType, params ExecutableNode[] children) =>
        Node(nodeId, activityType, TriggerNodeMetadata.TriggerExecutionType, children);

    private static ExecutableNode ActionNode(string nodeId, string activityType, params ExecutableNode[] children) =>
        Node(nodeId, activityType, "Action", children);

    private static ExecutableNode Node(string nodeId, string activityType, string executionType, ExecutableNode[] children)
    {
        using var document = JsonDocument.Parse("""{"type":"test"}""");
        var childSlots = children.Length == 0
            ? Array.Empty<ExecutableChildSlot>()
            : [new ExecutableChildSlot("Body", children)];

        return new ExecutableNode(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: activityType,
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: document.RootElement.Clone(),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string> { [TriggerNodeMetadata.ExecutionTypeKey] = executionType },
            childSlots: childSlots);
    }

    private sealed class FakeProvider(string activityType, string stimulusType, string stimulusHash, string? correlationScope = null)
        : IActivityTriggerStimulusProvider
    {
        public TriggerStimulusDescriptor? Describe(ExecutableNode node) =>
            StringComparer.Ordinal.Equals(node.ActivityType, activityType)
                ? new TriggerStimulusDescriptor(stimulusType, stimulusHash, correlationScope)
                : null;
    }
}
