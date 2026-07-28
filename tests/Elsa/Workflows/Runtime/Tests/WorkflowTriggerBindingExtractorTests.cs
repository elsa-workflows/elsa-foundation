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
    public void ProviderId_DefaultsToStablePublicClrIdentity()
    {
        IActivityTriggerStimulusProvider provider = new FakeProvider("Elsa.Event", "Event", "sha256:event:hello");

        Assert.Equal(typeof(FakeProvider).FullName, provider.ProviderId);
    }

    [Fact]
    public void NodeOutcome_RejectsRegisteredStatusWithoutBindings()
    {
        var exception = Assert.Throws<ArgumentException>(() => new WorkflowTriggerNodePreflightOutcome(
            "node-event",
            "Elsa.Event",
            "provider.event",
            WorkflowTriggerPreflightStatus.Registered,
            []));

        Assert.Contains("Registered", exception.Message);
    }

    [Fact]
    public void NodeOutcome_RejectsIntentionallyNonStartingStatusWithBindings()
    {
        var exception = Assert.Throws<ArgumentException>(() => new WorkflowTriggerNodePreflightOutcome(
            "node-event",
            "Elsa.Event",
            "provider.event",
            WorkflowTriggerPreflightStatus.IntentionallyNonStarting,
            [Binding("node-event", "sha256:event:hello")]));

        Assert.Contains("IntentionallyNonStarting", exception.Message);
    }

    [Fact]
    public void PreflightOutcome_RejectsDuplicateNodeOutcomes()
    {
        var nodeOutcome = new WorkflowTriggerNodePreflightOutcome(
            "node-event",
            "Elsa.Event",
            "provider.event",
            WorkflowTriggerPreflightStatus.Registered,
            [Binding("node-event", "sha256:event:hello")]);

        var exception = Assert.Throws<ArgumentException>(() => new WorkflowTriggerPreflightOutcome(_identity, [nodeOutcome, nodeOutcome]));

        Assert.Contains("node-event", exception.Message);
    }

    [Fact]
    public void PreflightException_PreservesArtifactNodeProviderFacetAndInnerException()
    {
        var innerException = new FormatException("invalid expression");
        var exception = new WorkflowTriggerPreflightException(
            "artifact-1",
            "node-cron",
            "Elsa.Cron",
            ["provider.cron"],
            "RecurringSchedule",
            "Cron schedule could not be materialized.",
            innerException);

        Assert.Equal("artifact-1", exception.ArtifactId);
        Assert.Equal("node-cron", exception.ExecutableNodeId);
        Assert.Equal("Elsa.Cron", exception.ActivityType);
        Assert.Equal(["provider.cron"], exception.ProviderIds);
        Assert.Equal("RecurringSchedule", exception.Facet);
        Assert.Same(innerException, exception.InnerException);
    }

    [Fact]
    public void Evaluate_RejectsTriggerNodeClaimedByNoProvider_WithArtifactAndNodeContext()
    {
        IWorkflowTriggerBindingExtractor extractor = new WorkflowTriggerBindingExtractor([new FakeProvider("Elsa.Event", "Event", "sha256:event:hello", providerId: "provider.event")]);
        var executable = Executable(TriggerNode("node-mystery", "Elsa.Unknown"));

        var exception = Assert.Throws<WorkflowTriggerPreflightException>(() => extractor.Evaluate(executable));

        Assert.Equal("artifact-1", exception.ArtifactId);
        Assert.Equal("node-mystery", exception.ExecutableNodeId);
        Assert.Equal("Elsa.Unknown", exception.ActivityType);
        Assert.Empty(exception.ProviderIds);
        Assert.Equal("ProviderRecognition", exception.Facet);
    }

    [Fact]
    public void Evaluate_RejectsTriggerNodeClaimedBySeveralProviders_WithSortedProviderIds()
    {
        IWorkflowTriggerBindingExtractor extractor = new WorkflowTriggerBindingExtractor([
            new FakeProvider("Elsa.Event", "Event", "sha256:event:one", providerId: "provider.zulu"),
            new FakeProvider("Elsa.Event", "Event", "sha256:event:two", providerId: "provider.alpha")
        ]);
        var executable = Executable(TriggerNode("node-event", "Elsa.Event"));

        var exception = Assert.Throws<WorkflowTriggerPreflightException>(() => extractor.Evaluate(executable));

        Assert.Equal("node-event", exception.ExecutableNodeId);
        Assert.Equal(["provider.alpha", "provider.zulu"], exception.ProviderIds);
        Assert.Equal("ProviderRecognition", exception.Facet);
    }

    [Fact]
    public void Evaluate_RejectsRecognizingProviderWithBlankProviderId()
    {
        IWorkflowTriggerBindingExtractor extractor = new WorkflowTriggerBindingExtractor([
            new FakeProvider("Elsa.Event", "Event", "sha256:event:hello", providerId: " ")
        ]);
        var executable = Executable(TriggerNode("node-event", "Elsa.Event"));

        var exception = Assert.Throws<WorkflowTriggerPreflightException>(() => extractor.Evaluate(executable));

        Assert.Equal("artifact-1", exception.ArtifactId);
        Assert.Equal("node-event", exception.ExecutableNodeId);
        Assert.Equal("Elsa.Event", exception.ActivityType);
        Assert.Equal("ProviderIdentity", exception.Facet);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Evaluate_RejectsThrowingProviderWithBlankProviderId_AsProviderIdentityFailure(string? providerId)
    {
        var innerException = new FormatException("invalid authored trigger input");
        IWorkflowTriggerBindingExtractor extractor = new WorkflowTriggerBindingExtractor([
            new ThrowingProvider(providerId, "Elsa.Event", innerException)
        ]);
        var executable = Executable(TriggerNode("node-event", "Elsa.Event"));

        var exception = Assert.Throws<WorkflowTriggerPreflightException>(() => extractor.Evaluate(executable));

        Assert.Equal("artifact-1", exception.ArtifactId);
        Assert.Equal("node-event", exception.ExecutableNodeId);
        Assert.Equal("Elsa.Event", exception.ActivityType);
        Assert.Empty(exception.ProviderIds);
        Assert.Equal("ProviderIdentity", exception.Facet);
        Assert.Same(innerException, exception.InnerException);
    }

    [Fact]
    public void Evaluate_RejectsDescriptorsThatProduceTheSameBindingId()
    {
        IWorkflowTriggerBindingExtractor extractor = new WorkflowTriggerBindingExtractor([
            new MultiDescriptorProvider(
                "provider.http",
                "Elsa.HttpEndpoint",
                new TriggerStimulusDescriptor("HttpEndpoint", "sha256:get"),
                new TriggerStimulusDescriptor("HttpEndpoint", "sha256:get"))
        ]);
        var executable = Executable(TriggerNode("node-http", "Elsa.HttpEndpoint"));

        var exception = Assert.Throws<WorkflowTriggerPreflightException>(() => extractor.Evaluate(executable));

        Assert.Equal("node-http", exception.ExecutableNodeId);
        Assert.Equal(["provider.http"], exception.ProviderIds);
        Assert.Equal("BindingIdentity", exception.Facet);
        Assert.Contains(WorkflowTriggerBinding.BuildId("artifact-1", "node-http", "sha256:get"), exception.Message);
    }

    [Fact]
    public void Evaluate_RejectsWholePreflightWhenLaterTriggerNodeIsInvalid()
    {
        IWorkflowTriggerBindingExtractor extractor = new WorkflowTriggerBindingExtractor([
            new FakeProvider("Elsa.Event", "Event", "sha256:event:hello", providerId: "provider.event")
        ]);
        var executable = Executable(ActionNode(
            "root",
            "Elsa.Sequence",
            TriggerNode("node-invalid", "Elsa.Unknown"),
            TriggerNode("node-valid", "Elsa.Event")));

        var exception = Assert.Throws<WorkflowTriggerPreflightException>(() => extractor.Evaluate(executable));

        Assert.Equal("node-invalid", exception.ExecutableNodeId);
        Assert.Empty(exception.ProviderIds);
    }

    [Fact]
    public void Evaluate_CallsEveryProviderExactlyOncePerTriggerNode()
    {
        var first = new CountingProvider("provider.event", "Elsa.Event", recognized: true, "sha256:event:hello");
        var second = new CountingProvider("provider.other", "Elsa.Other", recognized: true, "sha256:other");
        IWorkflowTriggerBindingExtractor extractor = new WorkflowTriggerBindingExtractor([first, second]);
        var executable = Executable(ActionNode(
            "root",
            "Elsa.Sequence",
            TriggerNode("node-event", "Elsa.Event"),
            TriggerNode("node-other", "Elsa.Other")));

        var outcome = extractor.Evaluate(executable);

        Assert.Equal(2, outcome.NodeOutcomes.Count);
        Assert.Equal(2, first.CallCount);
        Assert.Equal(2, second.CallCount);
    }

    [Fact]
    public void Evaluate_AcceptsProviderFanOutWhenResultingBindingIdsAreDistinct()
    {
        IWorkflowTriggerBindingExtractor extractor = new WorkflowTriggerBindingExtractor([
            new MultiDescriptorProvider(
                "provider.http",
                "Elsa.HttpEndpoint",
                new TriggerStimulusDescriptor("HttpEndpoint", "sha256:get"),
                new TriggerStimulusDescriptor("HttpEndpoint", "sha256:post"))
        ]);
        var executable = Executable(TriggerNode("node-http", "Elsa.HttpEndpoint"));

        var outcome = extractor.Evaluate(executable);

        var nodeOutcome = Assert.Single(outcome.NodeOutcomes);
        Assert.Equal("provider.http", nodeOutcome.ProviderId);
        Assert.Equal(WorkflowTriggerPreflightStatus.Registered, nodeOutcome.Status);
        Assert.Equal(2, nodeOutcome.Bindings.Count);
        Assert.Equal(2, nodeOutcome.Bindings.Select(x => x.TriggerBindingId).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Evaluate_LegacyExtractorReportsExecutableNodeActivityType()
    {
        IWorkflowTriggerBindingExtractor extractor = new LegacyExtractor([Binding("node-event", "sha256:event:hello")]);
        var executable = Executable(TriggerNode("node-event", "Elsa.Event"));

        var outcome = extractor.Evaluate(executable);

        var nodeOutcome = Assert.Single(outcome.NodeOutcomes);
        Assert.Equal("Event", Assert.Single(nodeOutcome.Bindings).StimulusType);
        Assert.Equal("Elsa.Event", nodeOutcome.ActivityType);
    }

    [Fact]
    public void Evaluate_LegacyExtractorRejectsBindingForMissingExecutableNode()
    {
        IWorkflowTriggerBindingExtractor extractor = new LegacyExtractor([Binding("node-missing", "sha256:event:hello")]);
        var executable = Executable(TriggerNode("node-event", "Elsa.Event"));

        var exception = Assert.Throws<WorkflowTriggerPreflightException>(() => extractor.Evaluate(executable));

        Assert.Equal("artifact-1", exception.ArtifactId);
        Assert.Equal("node-missing", exception.ExecutableNodeId);
        Assert.Equal("ExecutableIdentity", exception.Facet);
    }

    [Fact]
    public void Extract_ReturnsBinding_ForTriggerNodeRecognizedByProvider()
    {
        var extractor = new WorkflowTriggerBindingExtractor([new FakeProvider("Elsa.Event", "Event", "sha256:event:hello", "order-7")]);
        var executable = Executable(TriggerNode("node-event", "Elsa.Event"));

        var binding = Assert.Single(extractor.Extract(executable));

        Assert.Equal(WorkflowTriggerBinding.BuildId("artifact-1", "node-event", "sha256:event:hello"), binding.TriggerBindingId);
        Assert.Equal("artifact-1", binding.ArtifactId);
        Assert.Equal("node-event", binding.ExecutableNodeId);
        Assert.Equal("Event", binding.StimulusType);
        Assert.Equal("sha256:event:hello", binding.StimulusHash);
        Assert.Equal("order-7", binding.CorrelationScope);
        Assert.Equal(TriggerCardinality.FanOut, binding.Cardinality);
    }

    [Fact]
    public void Extract_CopiesExclusiveProviderCardinalityToTheDurableBinding()
    {
        var extractor = new WorkflowTriggerBindingExtractor(
            [new FakeProvider("Elsa.HttpEndpoint", "HttpEndpoint", "sha256:http:get-orders", cardinality: TriggerCardinality.Exclusive)]);

        var binding = Assert.Single(extractor.Extract(Executable(TriggerNode("node-http", "Elsa.HttpEndpoint"))));

        Assert.Equal(TriggerCardinality.Exclusive, binding.Cardinality);
    }

    [Fact]
    public void Extract_EmitsOneBindingPerDescriptor_WithDistinctIds_AndCopiesMetadataVerbatim()
    {
        // A single trigger node whose provider yields several descriptors (e.g. one HTTP method each) must
        // produce one binding per descriptor, each with a distinct id, and carry each descriptor's metadata through.
        var provider = new MultiDescriptorProvider(
            "provider.http",
            "Elsa.HttpEndpoint",
            new TriggerStimulusDescriptor("HttpEndpoint", "sha256:get", metadata: new Dictionary<string, string> { ["http:template"] = "orders/{id}", ["http:method"] = "get" }),
            new TriggerStimulusDescriptor("HttpEndpoint", "sha256:delete", metadata: new Dictionary<string, string> { ["http:template"] = "orders/{id}", ["http:method"] = "delete" }));
        var extractor = new WorkflowTriggerBindingExtractor([provider]);
        var executable = Executable(TriggerNode("node-http", "Elsa.HttpEndpoint"));

        var bindings = extractor.Extract(executable).ToList();

        Assert.Equal(2, bindings.Count);
        Assert.Equal(2, bindings.Select(b => b.TriggerBindingId).Distinct().Count());

        var getBinding = Assert.Single(bindings, b => b.StimulusHash == "sha256:get");
        Assert.Equal(WorkflowTriggerBinding.BuildId("artifact-1", "node-http", "sha256:get"), getBinding.TriggerBindingId);
        Assert.Equal("orders/{id}", getBinding.Metadata["http:template"]);
        Assert.Equal("get", getBinding.Metadata["http:method"]);

        var deleteBinding = Assert.Single(bindings, b => b.StimulusHash == "sha256:delete");
        Assert.Equal("delete", deleteBinding.Metadata["http:method"]);
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

    [Fact]
    public void Extract_ProducesNoBindings_ForRecognizedNonStartNode_WithoutThrowing()
    {
        // Spec 089 D: a recognized node that declares itself a non-start (CanStartWorkflow = false → zero
        // descriptors) must yield no trigger bindings and must NOT trip the zero-descriptor publish guard, which
        // only fires for a node NO provider recognizes.
        var extractor = new WorkflowTriggerBindingExtractor([new NonStartProvider("Elsa.HttpEndpoint")]);
        var executable = Executable(TriggerNode("node-midflow", "Elsa.HttpEndpoint"));

        Assert.Empty(extractor.Extract(executable));
    }

    [Fact]
    public void Evaluate_MapsRecognizedEmptyResultToProviderOwnedIntentionallyNonStartingOutcome()
    {
        const string providerId = "Elsa.HttpEndpoint";
        IWorkflowTriggerBindingExtractor extractor = new WorkflowTriggerBindingExtractor([
            new NonStartProvider("Elsa.HttpEndpoint", providerId)
        ]);
        var executable = Executable(TriggerNode("node-midflow", "Elsa.HttpEndpoint"));

        var outcome = extractor.Evaluate(executable);

        Assert.Empty(outcome.Bindings);
        var nodeOutcome = Assert.Single(outcome.NodeOutcomes);
        Assert.Equal("node-midflow", nodeOutcome.ExecutableNodeId);
        Assert.Equal(providerId, nodeOutcome.ProviderId);
        Assert.Equal(WorkflowTriggerPreflightStatus.IntentionallyNonStarting, nodeOutcome.Status);
        Assert.Empty(nodeOutcome.Bindings);
    }

    private WorkflowExecutable Executable(ExecutableNode root) =>
        new(
            identity: _identity,
            rootActivity: root,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: DateTimeOffset.UnixEpoch,
            compatibilityMetadata: new Dictionary<string, string>(),
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference);

    private WorkflowTriggerBinding Binding(string nodeId, string stimulusHash) =>
        new(
            TriggerBindingId: WorkflowTriggerBinding.BuildId(_identity.ArtifactId, nodeId, stimulusHash),
            ArtifactId: _identity.ArtifactId,
            DefinitionId: _identity.DefinitionId,
            ArtifactVersion: _identity.ArtifactVersion,
            ArtifactHash: _identity.ArtifactHash,
            ExecutableNodeId: nodeId,
            StimulusType: "Event",
            StimulusHash: stimulusHash,
            CorrelationScope: null,
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UnixEpoch);

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
            descriptor: new RuntimeActivityDescriptor("test", RuntimeActivityDescriptor.InitialSchemaVersion, document.RootElement.Clone()),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string> { [TriggerNodeMetadata.ExecutionTypeKey] = executionType },
            childSlots: childSlots);
    }

    private sealed class FakeProvider(
        string activityType,
        string stimulusType,
        string stimulusHash,
        string? correlationScope = null,
        string? providerId = null,
        TriggerCardinality cardinality = TriggerCardinality.FanOut)
        : IActivityTriggerStimulusProvider
    {
        public string ProviderId => providerId ?? GetType().FullName!;
        public TriggerCardinality Cardinality => cardinality;

        public ActivityTriggerStimulusResult Describe(ExecutableNode node) =>
            StringComparer.Ordinal.Equals(node.ActivityType, activityType)
                ? ActivityTriggerStimulusResult.Recognized([new TriggerStimulusDescriptor(stimulusType, stimulusHash, correlationScope)])
                : ActivityTriggerStimulusResult.NotRecognized;
    }

    private sealed class MultiDescriptorProvider(string providerId, string activityType, params TriggerStimulusDescriptor[] descriptors)
        : IActivityTriggerStimulusProvider
    {
        public string ProviderId => providerId;

        public ActivityTriggerStimulusResult Describe(ExecutableNode node) =>
            StringComparer.Ordinal.Equals(node.ActivityType, activityType)
                ? ActivityTriggerStimulusResult.Recognized(descriptors)
                : ActivityTriggerStimulusResult.NotRecognized;
    }

    private sealed class CountingProvider(string providerId, string activityType, bool recognized, string stimulusHash)
        : IActivityTriggerStimulusProvider
    {
        public string ProviderId => providerId;
        public int CallCount { get; private set; }

        public ActivityTriggerStimulusResult Describe(ExecutableNode node)
        {
            CallCount++;
            return recognized && StringComparer.Ordinal.Equals(node.ActivityType, activityType)
                ? ActivityTriggerStimulusResult.Recognized([new TriggerStimulusDescriptor(activityType, stimulusHash)])
                : ActivityTriggerStimulusResult.NotRecognized;
        }
    }

    private sealed class ThrowingProvider(string? providerId, string activityType, Exception exception)
        : IActivityTriggerStimulusProvider
    {
        public string ProviderId => providerId!;

        public ActivityTriggerStimulusResult Describe(ExecutableNode node) =>
            StringComparer.Ordinal.Equals(node.ActivityType, activityType)
                ? throw exception
                : ActivityTriggerStimulusResult.NotRecognized;
    }

    private sealed class LegacyExtractor(IReadOnlyCollection<WorkflowTriggerBinding> bindings) : IWorkflowTriggerBindingExtractor
    {
        public IReadOnlyCollection<WorkflowTriggerBinding> Extract(WorkflowExecutable executable) => bindings;
    }

    /// <summary>A provider that recognizes its type but declares a non-start (zero descriptors, no publish failure).</summary>
    private sealed class NonStartProvider(string activityType, string? providerId = null) : IActivityTriggerStimulusProvider
    {
        public string ProviderId => providerId ?? GetType().FullName!;

        public ActivityTriggerStimulusResult Describe(ExecutableNode node) =>
            StringComparer.Ordinal.Equals(node.ActivityType, activityType)
                ? ActivityTriggerStimulusResult.Recognized([])
                : ActivityTriggerStimulusResult.NotRecognized;
    }
}
