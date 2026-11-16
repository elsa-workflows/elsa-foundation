using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Triggers;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// Spec 238, the start-path rule. A node that produces start bindings is valid only when every composite from the
/// root down to it begins at the next node of that path, as its structure kind's start-entry strategy answers.
/// Assertions name facets and node ids only, never message text.
/// </summary>
public sealed class WorkflowTriggerPlacementTests
{
    private const string StartTriggerType = "Elsa.Event";
    private const string WaitTriggerType = "Elsa.Wait";

    [Fact]
    public void RootTrigger_IsValid()
    {
        var outcome = Extractor().Evaluate(Executable(Trigger("node-trigger")));

        Assert.Single(outcome.Bindings);
    }

    [Fact]
    public void TriggerAtFirstChild_OfFirstChildComposite_IsValid()
    {
        var root = Composite("root", StartEntryTestSupport.FirstChildKind, Trigger("node-trigger"), Action("node-after"));

        Assert.Single(Extractor().Evaluate(Executable(root)).Bindings);
    }

    [Fact]
    public void TriggerAtSecondChild_OfFirstChildComposite_IsRefusedNamingTheComposite()
    {
        var root = Composite("root", StartEntryTestSupport.FirstChildKind, Action("node-before"), Trigger("node-trigger"));

        var exception = Assert.Throws<WorkflowTriggerPreflightException>(() => Extractor().Evaluate(Executable(root)));

        Assert.Equal(WorkflowTriggerPreflightException.PlacementFacet, exception.Facet);
        Assert.Equal("node-trigger", exception.ExecutableNodeId);
        Assert.Equal("root", exception.CompositeNodeId);
        Assert.Equal(StartEntryTestSupport.FirstChildKind, exception.CompositeStructureKind);
    }

    [Fact]
    public void TriggerBelowCompositeWithoutStructure_IsRefused()
    {
        var root = Action("root", Trigger("node-trigger"));

        var exception = Assert.Throws<WorkflowTriggerPreflightException>(() => Extractor().Evaluate(Executable(root)));

        Assert.Equal(WorkflowTriggerPreflightException.PlacementFacet, exception.Facet);
        Assert.Equal("root", exception.CompositeNodeId);
        Assert.Null(exception.CompositeStructureKind);
    }

    [Fact]
    public void TriggerBelowUnregisteredStructureKind_IsRefused()
    {
        var root = Composite("root", "test.loop", Trigger("node-trigger"));

        var exception = Assert.Throws<WorkflowTriggerPreflightException>(() => Extractor().Evaluate(Executable(root)));

        Assert.Equal(WorkflowTriggerPreflightException.PlacementFacet, exception.Facet);
        Assert.Equal("test.loop", exception.CompositeStructureKind);
    }

    [Fact]
    public void TriggerTwoLevelsDeep_ValidAtEachLevel_IsAccepted()
    {
        var inner = Composite("inner", StartEntryTestSupport.AnyChildKind, Action("node-start"), Trigger("node-trigger"));
        var root = Composite("root", StartEntryTestSupport.FirstChildKind, inner, Action("node-after"));

        Assert.Single(Extractor().Evaluate(Executable(root)).Bindings);
    }

    [Fact]
    public void TriggerTwoLevelsDeep_InvalidAtInnerLevel_IsRefusedNamingTheInnerComposite()
    {
        var inner = Composite("inner", StartEntryTestSupport.FirstChildKind, Action("node-start"), Trigger("node-trigger"));
        var root = Composite("root", StartEntryTestSupport.FirstChildKind, inner);

        var exception = Assert.Throws<WorkflowTriggerPreflightException>(() => Extractor().Evaluate(Executable(root)));

        Assert.Equal("inner", exception.CompositeNodeId);
    }

    [Fact]
    public void TriggerTwoLevelsDeep_InvalidAtOuterLevel_IsRefusedNamingTheOuterComposite()
    {
        var inner = Composite("inner", StartEntryTestSupport.AnyChildKind, Trigger("node-trigger"));
        var root = Composite("root", StartEntryTestSupport.FirstChildKind, Action("node-before"), inner);

        var exception = Assert.Throws<WorkflowTriggerPreflightException>(() => Extractor().Evaluate(Executable(root)));

        Assert.Equal("root", exception.CompositeNodeId);
    }

    [Fact]
    public void IntentionallyNonStartingTrigger_IsNeverChecked()
    {
        var root = Action("root", Action("node-before"), Trigger("node-wait", WaitTriggerType));

        var outcome = Extractor().Evaluate(Executable(root));

        var nodeOutcome = Assert.Single(outcome.NodeOutcomes);
        Assert.Equal(WorkflowTriggerPreflightStatus.IntentionallyNonStarting, nodeOutcome.Status);
    }

    [Fact]
    public void DuplicateStartEntryProvidersForOneKind_FailAsConfiguration_NotPlacement()
    {
        var extractor = new WorkflowTriggerBindingExtractor(
            [new StartProvider(), new NonStartProvider()],
            [StartEntryTestSupport.FirstChild, new DuplicateFirstChildProvider()]);
        var root = Composite("root", StartEntryTestSupport.FirstChildKind, Trigger("node-trigger"));

        var exception = Assert.Throws<WorkflowTriggerPreflightException>(() => extractor.Evaluate(Executable(root)));

        Assert.NotEqual(WorkflowTriggerPreflightException.PlacementFacet, exception.Facet);
    }

    [Fact]
    public void ProviderThatCannotReadItsComposite_FailsAsRecognition_NotPlacement()
    {
        var extractor = new WorkflowTriggerBindingExtractor([new StartProvider(), new NonStartProvider()], [new ThrowingProvider()]);
        var root = Composite("root", "test.throwing", Trigger("node-trigger"));

        var exception = Assert.Throws<WorkflowTriggerPreflightException>(() => extractor.Evaluate(Executable(root)));

        Assert.NotEqual(WorkflowTriggerPreflightException.PlacementFacet, exception.Facet);
        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    private static WorkflowTriggerBindingExtractor Extractor() =>
        new([new StartProvider(), new NonStartProvider()], [StartEntryTestSupport.AnyChild, StartEntryTestSupport.FirstChild]);

    private static ExecutableNode Trigger(string nodeId, string activityType = StartTriggerType) =>
        Node(nodeId, activityType, TriggerNodeMetadata.TriggerExecutionType, []);

    private static ExecutableNode Action(string nodeId, params ExecutableNode[] children) =>
        Node(nodeId, "Elsa.Action", "Action", children);

    private static ExecutableNode Composite(string nodeId, string kind, params ExecutableNode[] children) =>
        StartEntryTestSupport.WithStructure(Action(nodeId, children), kind);

    private static ExecutableNode Node(string nodeId, string activityType, string executionType, ExecutableNode[] children)
    {
        using var document = JsonDocument.Parse("""{"type":"test"}""");
        return new ExecutableNode(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: activityType,
            activityTypeVersion: "1.0.0",
            descriptor: new RuntimeActivityDescriptor("test", RuntimeActivityDescriptor.InitialSchemaVersion, document.RootElement.Clone()),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string> { [TriggerNodeMetadata.ExecutionTypeKey] = executionType },
            childSlots: children.Length == 0 ? [] : [new ExecutableChildSlot("Body", children)]);
    }

    private static WorkflowExecutable Executable(ExecutableNode root) =>
        new(
            identity: new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:artifact-1"),
            rootActivity: root,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: DateTimeOffset.UnixEpoch,
            compatibilityMetadata: new Dictionary<string, string>(),
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference);

    /// <summary>Recognizes <see cref="StartTriggerType"/> and gives it one start binding.</summary>
    private sealed class StartProvider : IActivityTriggerStimulusProvider
    {
        public string ProviderId => "test.start";
        public TriggerCardinality Cardinality => TriggerCardinality.FanOut;

        public ActivityTriggerStimulusResult Describe(ExecutableNode node) =>
            StringComparer.Ordinal.Equals(node.ActivityType, StartTriggerType)
                ? ActivityTriggerStimulusResult.Recognized([new TriggerStimulusDescriptor("Event", $"sha256:{node.ExecutableNodeId}")])
                : ActivityTriggerStimulusResult.NotRecognized;
    }

    /// <summary>Recognizes <see cref="WaitTriggerType"/> as a mid-flow wait: recognized, no start bindings.</summary>
    private sealed class NonStartProvider : IActivityTriggerStimulusProvider
    {
        public string ProviderId => "test.wait";
        public TriggerCardinality Cardinality => TriggerCardinality.FanOut;

        public ActivityTriggerStimulusResult Describe(ExecutableNode node) =>
            StringComparer.Ordinal.Equals(node.ActivityType, WaitTriggerType)
                ? ActivityTriggerStimulusResult.Recognized([])
                : ActivityTriggerStimulusResult.NotRecognized;
    }

    private sealed class DuplicateFirstChildProvider : IStructuralStartEntryProvider
    {
        public string StructureKind => StartEntryTestSupport.FirstChildKind;
        public ExecutableNode? SelectStartChild(ExecutableNode composite, string triggerNodeId) => null;
    }

    private sealed class ThrowingProvider : IStructuralStartEntryProvider
    {
        public string StructureKind => "test.throwing";
        public ExecutableNode? SelectStartChild(ExecutableNode composite, string triggerNodeId) =>
            throw new InvalidOperationException("malformed");
    }
}
