using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Flowchart;
using Elsa.Activities.Flowchart.Models;
using Elsa.Activities.Scheduling.Activities;
using Elsa.Activities.Sequence;
using Elsa.Activities.Sequence.Models;
using Elsa.Events.Core.Contracts;
using Elsa.Persistence.Core;
using Elsa.Primitives.Entities;
using Elsa.Primitives.Models;
using Elsa.Primitives.Persistence;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Publishing.Api.Handlers;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Events;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Resolvers;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ArgumentValue = Elsa.Expressions.Core.Models.ArgumentValue;
using DesignActivityContract = Elsa.Activities.Design.Core.Models.ActivityContract;
using DesignActivityInputContract = Elsa.Activities.Design.Core.Models.ActivityInputContract;
using WorkflowArgumentState = Elsa.Workflows.Design.Core.Models.ArgumentState;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;
using CronActivity = Elsa.Activities.Scheduling.Activities.Cron;
using EventActivity = Elsa.Activities.Primitives.Activities.Event;
using TimerActivity = Elsa.Activities.Scheduling.Activities.Timer;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed class WorkflowExecutableCompilerTests
{
    [Fact]
    public void Compiler_preserves_the_current_main_pre_metadata_enricher_constructor()
    {
        var constructor = typeof(WorkflowExecutableCompiler).GetConstructor(
        [
            typeof(IWorkflowDefinitionVersionStore),
            typeof(IActivityDefinitionVersionStore),
            typeof(IActivityDefinitionVersionPublicationStore),
            typeof(IExecutableActivityTemplateReader),
            typeof(IWorkflowExecutableSourceReferenceReader),
            typeof(ActivityTemplatePlacer),
            typeof(RuntimeInputBindingCompiler),
            typeof(RuntimeOutputCaptureCompiler),
            typeof(WorkflowExecutableHasher),
            typeof(ActivityTreeProjector),
            typeof(ExecutableNodeCompiler),
            typeof(WorkflowExecutablePlacementSidecarContext)
        ]);

        Assert.NotNull(constructor);
    }

    private readonly ActivityDefinitionVersion _writeLineActivity = ActivityVersion("activity-write-line", "Text", new TypeReference("String"));
    private readonly ActivityDefinitionVersion _writeLinesActivity = ActivityVersion("activity-write-lines", "Lines", new TypeReference("String", CollectionKind.List));
    private readonly ActivityDefinitionVersion _sequenceActivity = ActivityVersion("activity-sequence", typeof(SequenceActivity).FullName!);
    private readonly ActivityDefinitionVersion _flowchartActivity = ActivityVersion("activity-flowchart", typeof(FlowchartActivity).FullName!);
    private readonly ActivityDefinitionVersion _legacyTriggerActivity = LegacyTriggerActivityVersion();
    private readonly IActivityStructureService _activityStructureService = ActivityStructureService();

    [Fact]
    public async Task CompilesPublishedWorkflowExecutableWithPublishedScope()
    {
        var now = new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);
        var compiler = Compiler(WorkflowVersion(Node("write-one", Text("hello"))));

        var executable = await compiler.CompileAsync(new WorkflowExecutableCompileRequest(
            VersionId: "version-1",
            Scope: WorkflowExecutableReferenceScope.Published,
            CreatedAt: now,
            PublishedAt: now,
            ExpiresAt: null,
            ArtifactIdPrefix: "artifact-"));

        Assert.StartsWith("artifact-", executable.Identity.ArtifactId, StringComparison.Ordinal);
        Assert.Equal("write-one", executable.RootActivity.ExecutableNodeId);
        Assert.Equal(
            new RuntimeRequirement(WellKnownRuntimeActivityConsumers.ClrActivity, RuntimeActivityDescriptor.InitialSchemaVersion),
            Assert.Single(executable.RuntimeRequirements));
    }

    [Fact]
    public async Task Compiles_authored_checkpoint_cadence_onto_the_executable_and_into_the_behavioral_hash()
    {
        // ADR 0032 R5: the authored cadence flows design → publish → executable and is behavioral content — a cadence
        // change is a distinct artifact identity, never a silent alias of the same graph.
        var now = new DateTimeOffset(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);
        var node = Node("write-one", Text("hello"));
        var unauthored = await Compiler(WorkflowVersion(node)).CompileAsync(NewRequest(now));
        var coalesced = await Compiler(WorkflowVersionWithCadence(node, new WorkflowCheckpointCadenceOptions
        {
            Mode = "Coalesced",
            MaxSegmentCheckpoints = 8
        })).CompileAsync(NewRequest(now));
        var immediate = await Compiler(WorkflowVersionWithCadence(node, new WorkflowCheckpointCadenceOptions
        {
            Mode = "Immediate"
        })).CompileAsync(NewRequest(now));

        Assert.Null(unauthored.CheckpointCadence);
        Assert.Equal(new WorkflowExecutableCheckpointCadence("Coalesced", 8), coalesced.CheckpointCadence);
        Assert.Equal(new WorkflowExecutableCheckpointCadence("Immediate"), immediate.CheckpointCadence);

        // Distinct behavior ⇒ distinct content hash; unauthored stays byte-identical to the pre-cadence payload.
        Assert.NotEqual(unauthored.Identity.ArtifactHash, coalesced.Identity.ArtifactHash);
        Assert.NotEqual(unauthored.Identity.ArtifactHash, immediate.Identity.ArtifactHash);
        Assert.NotEqual(coalesced.Identity.ArtifactHash, immediate.Identity.ArtifactHash);
    }

    [Fact]
    public async Task Blank_authored_cadence_mode_compiles_as_unauthored()
    {
        var now = new DateTimeOffset(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);
        var node = Node("write-one", Text("hello"));
        var unauthored = await Compiler(WorkflowVersion(node)).CompileAsync(NewRequest(now));
        var blank = await Compiler(WorkflowVersionWithCadence(node, new WorkflowCheckpointCadenceOptions()))
            .CompileAsync(NewRequest(now));

        Assert.Null(blank.CheckpointCadence);
        Assert.Equal(unauthored.Identity.ArtifactHash, blank.Identity.ArtifactHash);
    }

    [Fact]
    public async Task Unrecognised_checkpoint_cadence_alias_fails_publication()
    {
        var compiler = Compiler(WorkflowVersionWithCadence(
            Node("write-one", Text("hello")),
            new WorkflowCheckpointCadenceOptions { Mode = "EveryOtherTuesday" }));

        var exception = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(
            () => compiler.CompileAsync(NewRequest(DateTimeOffset.UtcNow)).AsTask());

        Assert.Contains("EveryOtherTuesday", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Non_positive_authored_segment_cap_fails_publication()
    {
        var compiler = Compiler(WorkflowVersionWithCadence(
            Node("write-one", Text("hello")),
            new WorkflowCheckpointCadenceOptions { Mode = "Coalesced", MaxSegmentCheckpoints = 0 }));

        await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(
            () => compiler.CompileAsync(NewRequest(DateTimeOffset.UtcNow)).AsTask());
    }

    [Fact]
    public async Task Resolves_exact_reusable_version_places_template_and_compiles_expression_default()
    {
        var contract = new DesignActivityContract("1", [new DesignActivityInputContract(
            "value", "Value", new TypeReference("Int32"), true,
            false, new("JavaScript", JsonSerializer.SerializeToElement("40 + 2")), "elsa.json")], [new ActivityOutputContract(
            "result", "Result", new TypeReference("Int32"), true, false, "elsa.json")], []);
        var root = new ExecutableNode(
            "local-root", "local-root", "test.boundary", "1",
            new("test.boundary", "1", JsonSerializer.SerializeToElement(new { plan = 1 })),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, RuntimeOutputCapture>(), new Dictionary<string, string>(),
            [new ExecutableChildSlot("Graph.Entry", [new ExecutableNode(
                "local-child", "local-child", "test.child", "1",
                new("test.child", "1", JsonSerializer.SerializeToElement(new { plan = 2 })),
                new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, RuntimeOutputCapture>(), new Dictionary<string, string>())])],
            activityContract: BoundaryRuntimeContract(hasValueInput: true));
        var template = new ExecutableActivityTemplate(
            "template-reusable", "hash-reusable", root, new Dictionary<string, WorkflowExecutableResumeTarget>(),
            [], [], [], "fingerprint", new Dictionary<string, string>(), DateTimeOffset.UnixEpoch);
        var publication = new ActivityDefinitionVersionPublication
        {
            Id = "version-reusable",
            DefinitionVersionId = "version-reusable",
            DefinitionId = "definition-reusable",
            Version = "3.2.1",
            ActivityTypeKey = "activity.reusable",
            ResolutionKind = ActivityDefinitionVersionResolutionKind.ReusableTemplateBoundary,
            Contract = contract,
            Provider = new("test", "1", JsonSerializer.SerializeToElement(new { })),
            TemplateId = template.TemplateId,
            TemplateHash = template.TemplateHash,
            SourceReferenceId = "source-reusable",
            ProviderFingerprint = "fingerprint",
            DirectDependencyCount = 0,
            ClosedTemplateCount = 0,
            RuntimeRequirements = [],
            Lifecycle = ActivityDefinitionVersionLifecycle.Active
        };
        var sourceReference = new WorkflowExecutableSourceReference(
            "source-reusable", template.TemplateId, "ActivityDefinitionVersion", publication.DefinitionVersionId,
            publication.Version, publication.DefinitionId, publication.DefinitionVersionId, publication.Version,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, WorkflowExecutableReferenceScope.Published,
            LayoutSidecar: new ExecutableLayoutSidecar([new(
                "layout", ActivityInvocationOrigin.Empty, template.TemplateHash,
                [new("local-root", "local-root", "local-root", 1, 2)], [])]));
        var sidecars = new WorkflowExecutablePlacementSidecarContext();
        var outputTarget = new WorkflowArgumentState(
            "result",
            new ArgumentValue(JsonSerializer.SerializeToElement(new { referenceKey = "caller-result", declaringScopeId = "workflow" }), "Variable"),
            null, null, null, null);
        var compiler = TestCompiler.Create(
            new FakeVersionStore(WorkflowVersion(
                new ActivityNode("use-reusable", publication.DefinitionVersionId, [], [outputTarget]),
                variables: [new("caller-result", "CallerResult", new TypeReference("Int32"), null, null)])),
            new FakeActivityVersionStore([]),
            _activityStructureService,
            TestWellKnownTypeRegistry.Create(),
            new SinglePublicationStore(publication),
            new ReusableTemplateReader(template),
            new ReusableSourceReader(sourceReference),
            sidecars);

        var executable = await compiler.CompileAsync(NewRequest(DateTimeOffset.UtcNow));

        Assert.Equal("activity.reusable", executable.RootActivity.ActivityType);
        Assert.Matches("^node-[0-9a-f]{64}$", executable.RootActivity.ExecutableNodeId);
        var binding = executable.RootActivity.InputBindings["value"];
        Assert.Equal(RuntimeInputBindingSource.Expression, binding.Source);
        Assert.Equal("JavaScript", binding.Expression!.Language);
        Assert.Equal("40 + 2", binding.Expression.Expression);
        var capture = executable.RootActivity.OutputCaptures["Result"];
        Assert.Equal("variable:CallerResult", capture.ValueId);
        Assert.Equal("CallerResult", capture.Metadata[RuntimeMetadataKeys.VariableName]);
        Assert.True(capture.CaptureOnSuccessfulCompletion);
        Assert.Equal("version-reusable", executable.RootActivity.Descriptor.Payload.GetProperty("definitionVersionId").GetString());
        Assert.Single(sidecars.Get("version-1").BoundarySegments);

        var captured = await CompleteReusableBoundaryAsync(
            executable,
            new RuntimeDurableValueStorageDriverRegistry([new JsonRuntimeDurableValueStorageDriver()]),
            42);
        Assert.Equal(42, captured.InlineValue?.GetInt32());
        Assert.Equal("CallerResult", captured.Metadata[RuntimeMetadataKeys.VariableName]);
    }

    [Fact]
    public async Task Source_owned_flowchart_publication_compiles_authored_children_as_ordinary_structure()
    {
        var root = FlowchartNode("flowchart-0affb8fb", [Node("writeline-1a75fea9", Text("hello"))]);
        var publication = SourceOwnedPublication(
            _flowchartActivity,
            ActivityDefinitionVersionResolutionKind.AuthorableActivity);
        var compiler = CompilerWithPublication(root, publication, _writeLineActivity, _flowchartActivity);

        var executable = await compiler.CompileAsync(NewRequest(DateTimeOffset.UtcNow));

        Assert.Equal("flowchart-0affb8fb", executable.RootActivity.ExecutableNodeId);
        Assert.Equal(FlowchartActivity.StructureKind, executable.RootActivity.Structure?.Kind);
        Assert.Equal("writeline-1a75fea9", Assert.Single(executable.RootActivity.ChildSlots).Activities.Single().ExecutableNodeId);
    }

    // ADR 0032 R1 / spec 107: the [ActivitySideEffectProfile] attribute is folded into the pinned contract by
    // the publish-time compiler. Flowchart is declared ReplaySafe; WriteLine (unmarked) defaults to External.
    [Fact]
    public async Task Compiler_folds_the_declared_side_effect_profile_into_the_pinned_contract()
    {
        var root = FlowchartNode("flowchart-0affb8fb", [Node("writeline-1a75fea9", Text("hello"))]);
        var publication = SourceOwnedPublication(
            _flowchartActivity,
            ActivityDefinitionVersionResolutionKind.AuthorableActivity);
        var compiler = CompilerWithPublication(root, publication, _writeLineActivity, _flowchartActivity);

        var executable = await compiler.CompileAsync(NewRequest(DateTimeOffset.UtcNow));

        Assert.Equal(
            Elsa.Activities.Runtime.Core.Models.SideEffectProfile.ReplaySafe,
            executable.RootActivity.ActivityContract!.SideEffectProfile);
        var writeLine = Assert.Single(executable.RootActivity.ChildSlots).Activities.Single();
        Assert.Equal(
            Elsa.Activities.Runtime.Core.Models.SideEffectProfile.External,
            writeLine.ActivityContract!.SideEffectProfile);
    }

    [Fact]
    public async Task Legacy_source_owned_sequence_publication_compiles_authored_children_as_ordinary_structure()
    {
        var root = SequenceNode("sequence-source-owned", [Node("write-line", Text("hello"))]);
        var publication = SourceOwnedPublication(
            _sequenceActivity,
            ActivityDefinitionVersionResolutionKind.Unspecified);
        var compiler = CompilerWithPublication(root, publication, _writeLineActivity, _sequenceActivity);

        var executable = await compiler.CompileAsync(NewRequest(DateTimeOffset.UtcNow));

        Assert.Equal("sequence-source-owned", executable.RootActivity.ExecutableNodeId);
        Assert.Equal(SequenceActivity.StructureKind, executable.RootActivity.Structure?.Kind);
        Assert.Equal("write-line", Assert.Single(executable.RootActivity.ChildSlots).Activities.Single().ExecutableNodeId);
    }

    [Fact]
    public async Task Reusable_template_boundary_with_authored_children_remains_rejected()
    {
        var publication = new ActivityDefinitionVersionPublication
        {
            Id = "version-reusable-with-authored-children",
            DefinitionVersionId = "version-reusable-with-authored-children",
            DefinitionId = "definition-reusable-with-authored-children",
            Version = "1.0.0",
            ActivityTypeKey = "activity.reusable",
            SourceDraftId = "draft-reusable-with-authored-children",
            Contract = new DesignActivityContract("1", [], [], []),
            Provider = new("test", "1", JsonSerializer.SerializeToElement(new { })),
            TemplateId = "template-reusable-with-authored-children",
            TemplateHash = "hash-reusable-with-authored-children",
            SourceReferenceId = "source-reusable-with-authored-children",
            ProviderFingerprint = "fingerprint",
            DirectDependencyCount = 0,
            ClosedTemplateCount = 0,
            RuntimeRequirements = []
        };
        var root = SequenceNode(
            "use-reusable-with-authored-children",
            [Node("authored-child", Text("not allowed"))],
            activityVersionId: publication.DefinitionVersionId);
        var compiler = CompilerWithPublication(root, publication);

        var exception = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(
            () => compiler.CompileAsync(NewRequest(DateTimeOffset.UtcNow)).AsTask());

        Assert.Equal(
            "Reusable activity node 'use-reusable-with-authored-children' cannot contain authored child activities; its structure is supplied by the exact published activity template.",
            exception.Message);
    }

    [Fact]
    public async Task Reusable_output_capture_preserves_a_custom_driver_and_persistable_value_through_runtime_completion()
    {
        var driver = new OpaqueOutputStorageDriver();
        var storageDrivers = new RuntimeDurableValueStorageDriverRegistry(
            [new JsonRuntimeDurableValueStorageDriver(), driver]);
        var contract = new DesignActivityContract(
            "1",
            [],
            [new("result", "Result", new TypeReference("Object"), true, false, driver.DriverKey)],
            []);
        var outputTarget = new WorkflowArgumentState(
            "result",
            new ArgumentValue(JsonSerializer.SerializeToElement(new { referenceKey = "caller-result" }), "Variable"),
            null, null, null, null);
        var captures = new RuntimeOutputCaptureCompiler(storageDrivers).CompileBoundaryOutputs(
            "use-reusable",
            contract.Outputs,
            [outputTarget],
            [new("caller-result", "CallerResult", new TypeReference("Object"), null, null)]);
        var root = new ExecutableNode(
            "local-root", "local-root", "test.boundary", "1",
            new("test.boundary", "1", JsonSerializer.SerializeToElement(new { plan = 1 })),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, RuntimeOutputCapture>(), new Dictionary<string, string>(),
            [new ExecutableChildSlot("Graph.Entry", [new ExecutableNode(
                "local-child", "local-child", "test.child", "1",
                new("test.child", "1", JsonSerializer.SerializeToElement(new { plan = 2 })),
                new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, RuntimeOutputCapture>(), new Dictionary<string, string>())])],
            activityContract: BoundaryRuntimeContract(hasValueInput: false));
        var template = new ExecutableActivityTemplate(
            "template-custom", "hash-custom", root, new Dictionary<string, WorkflowExecutableResumeTarget>(),
            [], [], [], "fingerprint", new Dictionary<string, string>(), DateTimeOffset.UnixEpoch);
        var publication = new ActivityDefinitionVersionPublication
        {
            Id = "version-custom",
            DefinitionVersionId = "version-custom",
            DefinitionId = "definition-custom",
            Version = "1.0.0",
            ActivityTypeKey = "activity.custom-output",
            Contract = contract,
            Provider = new("test", "1", JsonSerializer.SerializeToElement(new { })),
            TemplateId = template.TemplateId,
            TemplateHash = template.TemplateHash,
            SourceReferenceId = "source-custom",
            ProviderFingerprint = "fingerprint",
            DirectDependencyCount = 0,
            ClosedTemplateCount = 0,
            RuntimeRequirements = []
        };
        var sourceReference = new WorkflowExecutableSourceReference(
            "source-custom", template.TemplateId, "ActivityDefinitionVersion", publication.DefinitionVersionId,
            publication.Version, publication.DefinitionId, publication.DefinitionVersionId, publication.Version,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, WorkflowExecutableReferenceScope.Published,
            LayoutSidecar: new ExecutableLayoutSidecar([new(
                "layout-custom", ActivityInvocationOrigin.Empty, template.TemplateHash,
                [new("local-root", "local-root", "local-root", 0, 0)], [])]));
        var placer = new ActivityTemplatePlacer(
            new SinglePublicationStore(publication),
            new ReusableTemplateReader(template),
            new ReusableSourceReader(sourceReference),
            new Sha256ActivityPlacementHasher());
        var placed = await placer.PlaceAsync(new(
            publication,
            template,
            sourceReference,
            new ActivityInvocationOrigin([new(ActivityInvocationOriginSegmentKind.AuthoredNode, "use-reusable")]),
            publication.ActivityTypeKey,
            new Dictionary<string, RuntimeInputBinding>(),
            captures));
        var executable = new WorkflowExecutable(
            new("artifact-custom", "workflow", "workflow-version", "1.0.0", "hash-workflow"),
            placed.Root,
            placed.ResumeTargets,
            DateTimeOffset.UnixEpoch,
            new Dictionary<string, string>());
        var opaque = new OpaqueOutput("opaque");

        var captured = await CompleteReusableBoundaryAsync(executable, storageDrivers, opaque);
        var projections = await RuntimeInputBindingStateProjection.ProjectAllAsync([captured], storageDrivers);

        Assert.Equal(driver.DriverKey, placed.Root.OutputCaptures["Result"].StorageDriverKey);
        Assert.Equal(DurableValueStorage.External, captured.Storage);
        Assert.Equal(driver.DriverKey, captured.Type.Id);
        Assert.Same(opaque, projections.WorkflowVariables["CallerResult"]);
    }

    [Fact]
    public async Task Compiler_applies_event_collected_node_metadata_before_executable_assembly()
    {
        var workflowVersion = WorkflowVersion(Node("write-one", Text("hello")));
        var compiler = TestCompiler.Create(
            new FakeVersionStore(workflowVersion),
            new FakeActivityVersionStore([_writeLineActivity]),
            _activityStructureService,
            TestWellKnownTypeRegistry.Create(),
            metadataEnricher: MetadataEnricher(new FixedMetadataSource("write-one", "runtime.pin", "pinned-value")));

        var executable = await compiler.CompileAsync(NewRequest(DateTimeOffset.UtcNow));

        Assert.Equal("pinned-value", executable.RootActivity.Metadata["runtime.pin"]);
    }

    [Fact]
    public async Task Metadata_enrichment_preserves_pinned_activity_contracts()
    {
        var workflowVersion = WorkflowVersion(Node("write-one", Text("hello")));
        var compiler = TestCompiler.Create(
            new FakeVersionStore(workflowVersion),
            new FakeActivityVersionStore([_writeLineActivity]),
            _activityStructureService,
            TestWellKnownTypeRegistry.Create(),
            metadataEnricher: MetadataEnricher(new FixedMetadataSource("write-one", "runtime.pin", "pinned-value")));

        var executable = await compiler.CompileAsync(NewRequest(DateTimeOffset.UtcNow));

        // The enricher rebuilds every node to merge metadata claims; the rebuild must carry the pinned
        // activity contract through, or the runtime start handler faults with VF-ACT-001 on dispatch.
        Assert.Equal("pinned-value", executable.RootActivity.Metadata["runtime.pin"]);
        Assert.NotNull(executable.RootActivity.ActivityContract);
    }

    [Fact]
    public async Task Clr_node_with_unresolvable_type_alias_fails_publication()
    {
        var ghost = GhostAliasActivityVersion();
        var compiler = TestCompiler.Create(
            new FakeVersionStore(WorkflowVersion(new ActivityNode("write-ghost", ghost.Id, [], []))),
            new FakeActivityVersionStore([ghost]),
            _activityStructureService,
            TestWellKnownTypeRegistry.Create());

        var exception = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(
            () => compiler.CompileAsync(NewRequest(DateTimeOffset.UtcNow)).AsTask());

        Assert.Contains("VF-ACT-001", exception.Message, StringComparison.Ordinal);
        Assert.Contains("write-ghost", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Test.UnregisteredActivity", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Clr_node_without_a_typed_result_fails_publication()
    {
        var registry = TestWellKnownTypeRegistry.Create();
        registry.RegisterType(typeof(ContractlessActivity), TypeAliasConvention.CanonicalAlias(typeof(ContractlessActivity)));
        var contractless = ContractlessActivityVersion();
        var compiler = TestCompiler.Create(
            new FakeVersionStore(WorkflowVersion(new ActivityNode("run-contractless", contractless.Id, [], []))),
            new FakeActivityVersionStore([contractless]),
            _activityStructureService,
            registry);

        var exception = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(
            () => compiler.CompileAsync(NewRequest(DateTimeOffset.UtcNow)).AsTask());

        Assert.Contains("VF-ACT-001", exception.Message, StringComparison.Ordinal);
        Assert.Contains("run-contractless", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Enrichment_that_drops_pinned_contracts_fails_publication()
    {
        // The VF-ACT-001 gate runs on the final tree, so a metadata enricher that rebuilds nodes without
        // their pinned contracts is caught at publish time instead of poisoning runtime dispatch.
        var workflowVersion = WorkflowVersion(Node("write-one", Text("hello")));
        var compiler = TestCompiler.Create(
            new FakeVersionStore(workflowVersion),
            new FakeActivityVersionStore([_writeLineActivity]),
            _activityStructureService,
            TestWellKnownTypeRegistry.Create(),
            metadataEnricher: new ContractStrippingEnricher());

        var exception = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(
            () => compiler.CompileAsync(NewRequest(DateTimeOffset.UtcNow)).AsTask());

        Assert.Contains("VF-ACT-001", exception.Message, StringComparison.Ordinal);
        Assert.Contains("write-one", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Metadata_enrichment_preserves_intrinsic_identity()
    {
        var value = new RuntimeInputBinding(
            WorkflowIntrinsicInputKeys.Value,
            new ValueTypeDescriptor("System.String"),
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(
                new ValueTypeDescriptor("System.String"),
                JsonSerializer.SerializeToElement("updated"),
                ValueProtectionPolicy.InstanceInline));
        var intrinsic = new ExecutableNode(
            "node-set", "set", "elsa.intrinsic.set", "1.0.0",
            new RuntimeActivityDescriptor("intrinsic", "1", JsonSerializer.SerializeToElement(new { })),
            new Dictionary<string, RuntimeInputBinding> { [value.InputName] = value },
            new Dictionary<string, string>(),
            intrinsicKind: WorkflowIntrinsicKind.Set,
            intrinsicVariable: new RuntimeVariableReference("target", "scope-root"));
        var root = new ExecutableNode(
            "root", "root", "test.container", "1.0.0",
            new RuntimeActivityDescriptor("test.container", "1", JsonSerializer.SerializeToElement(new { })),
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot("Body", [intrinsic])]);
        var source = new WorkflowExecutableCompileSource(
            "definition-1", "version-1", "1.0.0",
            new WorkflowDefinitionState([], null, [], [], null),
            "WorkflowDefinitionVersion", "version-1", null);

        var enrichment = await MetadataEnricher(new FixedMetadataSource("node-set", "runtime.pin", "pinned-value"))
            .EnrichCompilationAsync(NewRequest(DateTimeOffset.UtcNow), source, root);

        var enriched = Assert.Single(Assert.Single(enrichment.RootActivity.ChildSlots).Activities);
        Assert.Equal("pinned-value", enriched.Metadata["runtime.pin"]);
        Assert.Equal(WorkflowIntrinsicKind.Set, enriched.IntrinsicKind);
        Assert.Equal("target", enriched.IntrinsicVariable?.VariableKey);
    }

    [Fact]
    public async Task Compiler_projects_canonical_versioned_workflow_input_contract_into_behavioral_hash()
    {
        var first = WorkflowVersion(
            Node("write-one", Text("hello")),
            [
                WorkflowInput("zeta", new TypeReference("String"), isRequired: false),
                WorkflowInput(
                    "alpha",
                    new TypeReference("Object"),
                    isRequired: true,
                    JsonSerializer.SerializeToElement(new { b = 2, a = 1 }),
                    "Literal")
            ]);
        var reordered = WorkflowVersion(
            Node("write-one", Text("hello")),
            [
                WorkflowInput(
                    "alpha",
                    new TypeReference("Object"),
                    isRequired: true,
                    JsonSerializer.SerializeToElement(new { a = 1, b = 2 }),
                    "Literal"),
                WorkflowInput("zeta", new TypeReference("String"), isRequired: false)
            ]);
        var changed = WorkflowVersion(
            Node("write-one", Text("hello")),
            [WorkflowInput("zeta", new TypeReference("Int32"), isRequired: false)]);

        var firstExecutable = await Compiler(first).CompileAsync(NewRequest(DateTimeOffset.UtcNow));
        var reorderedExecutable = await Compiler(reordered).CompileAsync(NewRequest(DateTimeOffset.UtcNow));
        var changedExecutable = await Compiler(changed).CompileAsync(NewRequest(DateTimeOffset.UtcNow));

        Assert.Equal(WorkflowExecutableInputContract.CurrentVersion, firstExecutable.InputContract!.Version);
        Assert.Equal(["alpha", "zeta"], firstExecutable.InputContract.Inputs.Select(input => input.Name));
        Assert.Equal(firstExecutable.Identity.ArtifactHash, reorderedExecutable.Identity.ArtifactHash);
        Assert.NotEqual(firstExecutable.Identity.ArtifactHash, changedExecutable.Identity.ArtifactHash);
    }

    [Fact]
    public async Task Structured_behavioral_hash_distinguishes_values_that_contain_legacy_delimiters()
    {
        var executable = await Compiler(WorkflowVersion(Node("write-one", Text("hello"))))
            .CompileAsync(NewRequest(DateTimeOffset.UtcNow));
        var hasher = new WorkflowExecutableHasher();
        var singleEncodedInput = new WorkflowExecutableInputContract(
            WorkflowExecutableInputContract.CurrentVersion,
            [new WorkflowDeclaredInput("a:String:Single:False:<none>|b", new TypeReference("String"), false)]);
        var separateInputs = new WorkflowExecutableInputContract(
            WorkflowExecutableInputContract.CurrentVersion,
            [
                new WorkflowDeclaredInput("a", new TypeReference("String"), false),
                new WorkflowDeclaredInput("b", new TypeReference("String"), false)
            ]);
        var singleEncodedNode = new WorkflowExecutableDependency("child", "sha256:child", ["a,b"]);
        var separateNodes = new WorkflowExecutableDependency("child", "sha256:child", ["a", "b"]);

        var singleInputHash = hasher.ComputeHash(executable.RootActivity, singleEncodedInput, []);
        var separateInputHash = hasher.ComputeHash(executable.RootActivity, separateInputs, []);
        var singleNodeHash = hasher.ComputeHash(executable.RootActivity, executable.InputContract!, [singleEncodedNode]);
        var separateNodeHash = hasher.ComputeHash(executable.RootActivity, executable.InputContract!, [separateNodes]);

        Assert.NotEqual(singleInputHash, separateInputHash);
        Assert.NotEqual(singleNodeHash, separateNodeHash);
    }

    [Fact]
    public async Task Structured_behavioral_hash_distinguishes_absent_default_from_explicit_json_null()
    {
        var executable = await Compiler(WorkflowVersion(Node("write-one", Text("hello"))))
            .CompileAsync(NewRequest(DateTimeOffset.UtcNow));
        var hasher = new WorkflowExecutableHasher();
        var absentDefault = new WorkflowExecutableInputContract(
            WorkflowExecutableInputContract.CurrentVersion,
            [new WorkflowDeclaredInput("value", new TypeReference("Object"), false)]);
        var explicitNullDefault = new WorkflowExecutableInputContract(
            WorkflowExecutableInputContract.CurrentVersion,
            [new WorkflowDeclaredInput("value", new TypeReference("Object"), false, JsonSerializer.SerializeToElement<object?>(null))]);

        var absentHash = hasher.ComputeHash(executable.RootActivity, absentDefault, []);
        var explicitNullHash = hasher.ComputeHash(executable.RootActivity, explicitNullDefault, []);

        Assert.NotEqual(absentHash, explicitNullHash);
    }

    [Fact]
    public async Task Compiler_assembles_canonical_direct_dependencies_and_hashes_child_behavior()
    {
        var workflow = WorkflowVersion(SequenceNode("sequence", [Node("dispatch-b"), Node("dispatch-a")]));
        var first = TestCompiler.Create(
            new FakeVersionStore(workflow),
            new FakeActivityVersionStore([_writeLineActivity, _sequenceActivity]),
            _activityStructureService,
            TestWellKnownTypeRegistry.Create(),
            metadataEnricher: MetadataEnricher(new FixedDependencySource(
            [
                new ExecutableDependencyClaim("dispatch-b", "child", "sha256:one"),
                new ExecutableDependencyClaim("dispatch-a", "child", "sha256:one")
            ])));
        var reordered = TestCompiler.Create(
            new FakeVersionStore(workflow),
            new FakeActivityVersionStore([_writeLineActivity, _sequenceActivity]),
            _activityStructureService,
            TestWellKnownTypeRegistry.Create(),
            metadataEnricher: MetadataEnricher(new FixedDependencySource(
            [
                new ExecutableDependencyClaim("dispatch-a", "child", "sha256:one"),
                new ExecutableDependencyClaim("dispatch-b", "child", "sha256:one")
            ])));
        var changed = TestCompiler.Create(
            new FakeVersionStore(workflow),
            new FakeActivityVersionStore([_writeLineActivity, _sequenceActivity]),
            _activityStructureService,
            TestWellKnownTypeRegistry.Create(),
            metadataEnricher: MetadataEnricher(new FixedDependencySource(
                [new ExecutableDependencyClaim("dispatch-a", "child", "sha256:two")])));

        var firstExecutable = await first.CompileAsync(NewRequest(DateTimeOffset.UtcNow));
        var reorderedExecutable = await reordered.CompileAsync(NewRequest(DateTimeOffset.UtcNow));
        var changedExecutable = await changed.CompileAsync(NewRequest(DateTimeOffset.UtcNow));

        var dependency = Assert.Single(firstExecutable.Dependencies);
        Assert.Equal("child", dependency.ArtifactId);
        Assert.Equal("sha256:one", dependency.ArtifactHash);
        Assert.Equal(["dispatch-a", "dispatch-b"], dependency.DispatchNodeIds);
        Assert.Equal(firstExecutable.Identity.ArtifactHash, reorderedExecutable.Identity.ArtifactHash);
        Assert.NotEqual(firstExecutable.Identity.ArtifactHash, changedExecutable.Identity.ArtifactHash);
    }

    [Fact]
    public async Task Compiler_canonicalizes_duplicate_exact_node_dependency_claims()
    {
        var workflow = WorkflowVersion(SequenceNode("sequence", [Node("dispatch-b"), Node("dispatch-a")]));
        var unique = await CompileWithDependenciesAsync(
            workflow,
            [
                new ExecutableDependencyClaim("dispatch-a", "child", "sha256:child"),
                new ExecutableDependencyClaim("dispatch-b", "child", "sha256:child")
            ]);
        var duplicated = await CompileWithDependenciesAsync(
            workflow,
            [
                new ExecutableDependencyClaim("dispatch-b", "child", "sha256:child"),
                new ExecutableDependencyClaim("dispatch-a", "child", "sha256:child"),
                new ExecutableDependencyClaim("dispatch-a", "child", "sha256:child"),
                new ExecutableDependencyClaim("dispatch-b", "child", "sha256:child")
            ]);

        var dependency = Assert.Single(duplicated.Dependencies);
        Assert.Equal(["dispatch-a", "dispatch-b"], dependency.DispatchNodeIds);
        Assert.Equal(unique.Identity.ArtifactHash, duplicated.Identity.ArtifactHash);
    }

    [Fact]
    public async Task Compiler_produces_same_parent_hash_for_equivalent_shared_diamond_orders()
    {
        var shared = await Compiler(WorkflowVersion(Node("shared", Text("shared behavior"))))
            .CompileAsync(NewRequest(DateTimeOffset.UtcNow));
        var left = await CompileWithDependenciesAsync(
            WorkflowVersion(Node("left", Text("left behavior"))),
            [new ExecutableDependencyClaim("left", shared.Identity.ArtifactId, shared.Identity.ArtifactHash)]);
        var right = await CompileWithDependenciesAsync(
            WorkflowVersion(Node("right", Text("right behavior"))),
            [new ExecutableDependencyClaim("right", shared.Identity.ArtifactId, shared.Identity.ArtifactHash)]);
        var parentWorkflow = WorkflowVersion(SequenceNode(
            "parent",
            [Node("dispatch-left"), Node("dispatch-right")]));

        var leftFirst = await CompileWithDependenciesAsync(
            parentWorkflow,
            [
                new ExecutableDependencyClaim("dispatch-left", left.Identity.ArtifactId, left.Identity.ArtifactHash),
                new ExecutableDependencyClaim("dispatch-right", right.Identity.ArtifactId, right.Identity.ArtifactHash)
            ]);
        var rightFirst = await CompileWithDependenciesAsync(
            parentWorkflow,
            [
                new ExecutableDependencyClaim("dispatch-right", right.Identity.ArtifactId, right.Identity.ArtifactHash),
                new ExecutableDependencyClaim("dispatch-left", left.Identity.ArtifactId, left.Identity.ArtifactHash)
            ]);

        Assert.Equal(2, leftFirst.Dependencies.Count);
        Assert.Equal(leftFirst.Identity.ArtifactHash, rightFirst.Identity.ArtifactHash);
    }

    [Fact]
    public async Task Grandchild_behavior_change_propagates_through_child_hash_into_parent_hash()
    {
        var firstGrandchild = await Compiler(WorkflowVersion(Node("grandchild", Text("first behavior"))))
            .CompileAsync(NewRequest(DateTimeOffset.UtcNow));
        var changedGrandchild = await Compiler(WorkflowVersion(Node("grandchild", Text("changed behavior"))))
            .CompileAsync(NewRequest(DateTimeOffset.UtcNow));
        var childWorkflow = WorkflowVersion(Node("dispatch-grandchild"));
        var firstChild = await CompileWithDependenciesAsync(
            childWorkflow,
            [new ExecutableDependencyClaim(
                "dispatch-grandchild",
                firstGrandchild.Identity.ArtifactId,
                firstGrandchild.Identity.ArtifactHash)]);
        var changedChild = await CompileWithDependenciesAsync(
            childWorkflow,
            [new ExecutableDependencyClaim(
                "dispatch-grandchild",
                changedGrandchild.Identity.ArtifactId,
                changedGrandchild.Identity.ArtifactHash)]);
        var parentWorkflow = WorkflowVersion(Node("dispatch-child"));
        var firstParent = await CompileWithDependenciesAsync(
            parentWorkflow,
            [new ExecutableDependencyClaim(
                "dispatch-child",
                firstChild.Identity.ArtifactId,
                firstChild.Identity.ArtifactHash)]);
        var changedParent = await CompileWithDependenciesAsync(
            parentWorkflow,
            [new ExecutableDependencyClaim(
                "dispatch-child",
                changedChild.Identity.ArtifactId,
                changedChild.Identity.ArtifactHash)]);

        Assert.NotEqual(firstGrandchild.Identity.ArtifactHash, changedGrandchild.Identity.ArtifactHash);
        Assert.NotEqual(firstChild.Identity.ArtifactHash, changedChild.Identity.ArtifactHash);
        Assert.NotEqual(firstParent.Identity.ArtifactHash, changedParent.Identity.ArtifactHash);
    }

    [Fact]
    public async Task Compiler_rejects_a_malformed_stored_exact_artifact_cycle_with_a_deterministic_full_identity_path()
    {
        var store = new InMemoryWorkflowExecutableStore();
        var firstIdentity = StoredIdentity("artifact-a", "sha256:a");
        var secondIdentity = StoredIdentity("artifact-b", "sha256:b");
        await store.SaveAsync(StoredExecutable(
            firstIdentity,
            new WorkflowExecutableDependency(secondIdentity.ArtifactId, secondIdentity.ArtifactHash, ["node-root"])));
        await store.SaveAsync(StoredExecutable(
            secondIdentity,
            new WorkflowExecutableDependency(firstIdentity.ArtifactId, firstIdentity.ArtifactHash, ["node-root"])));
        var compiler = ValidatedDependencyCompiler(
            WorkflowVersion(Node("dispatch-child")),
            [new ExecutableDependencyClaim("dispatch-child", firstIdentity.ArtifactId, firstIdentity.ArtifactHash)],
            store);

        var exception = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() =>
            compiler.CompileAsync(NewRequest(DateTimeOffset.UtcNow)).AsTask());

        var graphFailure = Assert.IsType<WorkflowExecutableDependencyGraphException>(exception.InnerException);
        Assert.Equal(WorkflowExecutableDependencyGraphFailureKind.Cycle, graphFailure.Kind);
        Assert.Contains("artifact-a@sha256:a -> artifact-b@sha256:b -> artifact-a@sha256:a", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Compiler_rejects_candidate_full_identity_recurrence_in_a_stored_child_closure()
    {
        var childIdentity = StoredIdentity("artifact-child", "sha256:child");
        var workflow = WorkflowVersion(Node("dispatch-child"));
        var claims = new[] { new ExecutableDependencyClaim("dispatch-child", childIdentity.ArtifactId, childIdentity.ArtifactHash) };
        var candidate = await CompileWithDependenciesAsync(workflow, claims);
        var store = new InMemoryWorkflowExecutableStore();
        await store.SaveAsync(StoredExecutable(candidate.Identity));
        await store.SaveAsync(StoredExecutable(
            childIdentity,
            new WorkflowExecutableDependency(
                candidate.Identity.ArtifactId,
                candidate.Identity.ArtifactHash,
                ["node-root"])));

        var exception = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() =>
            ValidatedDependencyCompiler(workflow, claims, store)
                .CompileAsync(NewRequest(DateTimeOffset.UtcNow)).AsTask());

        Assert.Contains(candidate.Identity.ArtifactId, exception.Message, StringComparison.Ordinal);
        Assert.Contains(candidate.Identity.ArtifactHash, exception.Message, StringComparison.Ordinal);
        Assert.Contains("recurs", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            $"artifact-child@sha256:child -> {candidate.Identity.ArtifactId}@{candidate.Identity.ArtifactHash}",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Compiler_rejects_a_truncated_dependency_artifact_id_instead_of_prefix_matching()
    {
        var store = new InMemoryWorkflowExecutableStore();
        var childIdentity = StoredIdentity("artifact-abcdef", "sha256:child-full");
        await store.SaveAsync(StoredExecutable(childIdentity));
        var compiler = ValidatedDependencyCompiler(
            WorkflowVersion(Node("dispatch-child")),
            [new ExecutableDependencyClaim("dispatch-child", "artifact-abc", childIdentity.ArtifactHash)],
            store);

        var exception = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() =>
            compiler.CompileAsync(NewRequest(DateTimeOffset.UtcNow)).AsTask());

        var graphFailure = Assert.IsType<WorkflowExecutableDependencyGraphException>(exception.InnerException);
        Assert.Equal(WorkflowExecutableDependencyGraphFailureKind.MissingArtifact, graphFailure.Kind);
        Assert.Contains("artifact-abc@sha256:child-full", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Compiler_rejects_a_dependency_hash_mismatch_without_truncated_hash_matching()
    {
        var store = new InMemoryWorkflowExecutableStore();
        var childIdentity = StoredIdentity("artifact-child", "sha256:child-full-hash");
        await store.SaveAsync(StoredExecutable(childIdentity));
        var compiler = ValidatedDependencyCompiler(
            WorkflowVersion(Node("dispatch-child")),
            [new ExecutableDependencyClaim("dispatch-child", childIdentity.ArtifactId, "sha256:child")],
            store);

        var exception = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() =>
            compiler.CompileAsync(NewRequest(DateTimeOffset.UtcNow)).AsTask());

        var graphFailure = Assert.IsType<WorkflowExecutableDependencyGraphException>(exception.InnerException);
        Assert.Equal(WorkflowExecutableDependencyGraphFailureKind.HashMismatch, graphFailure.Kind);
        Assert.Contains("sha256:child-full-hash", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Compiler_allows_same_definition_dependency_when_the_full_artifact_identity_is_different()
    {
        var store = new InMemoryWorkflowExecutableStore();
        var olderIdentity = StoredIdentity(
            "artifact-older",
            "sha256:older",
            definitionId: "definition-1",
            definitionVersionId: "version-older");
        await store.SaveAsync(StoredExecutable(olderIdentity));
        var compiler = ValidatedDependencyCompiler(
            WorkflowVersion(Node("dispatch-older")),
            [new ExecutableDependencyClaim("dispatch-older", olderIdentity.ArtifactId, olderIdentity.ArtifactHash)],
            store);

        var executable = await compiler.CompileAsync(NewRequest(DateTimeOffset.UtcNow));

        Assert.Equal("definition-1", executable.Identity.DefinitionId);
        Assert.NotEqual(olderIdentity.ArtifactId, executable.Identity.ArtifactId);
        Assert.Equal(olderIdentity.ArtifactId, Assert.Single(executable.Dependencies).ArtifactId);
    }

    [Fact]
    public async Task Compiler_rejects_non_literal_workflow_input_defaults()
    {
        var workflow = WorkflowVersion(
            Node("write-one"),
            [WorkflowInput(
                "message",
                new TypeReference("String"),
                isRequired: false,
                JsonSerializer.SerializeToElement("expression"),
                "JavaScript")]);

        var exception = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() =>
            Compiler(workflow).CompileAsync(NewRequest(DateTimeOffset.UtcNow)).AsTask());

        Assert.Contains("unsupported default syntax", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LegacyClrTriggerCatalogRow_CompilesWithDeclaredIdentityAndTriggerExecutionType()
    {
        var compiler = Compiler(WorkflowVersion(new ActivityNode("legacy-trigger", "activity-legacy-trigger", [], [])));

        var executable = await compiler.CompileAsync(new WorkflowExecutableCompileRequest(
            VersionId: "version-1",
            Scope: WorkflowExecutableReferenceScope.Published,
            CreatedAt: DateTimeOffset.UtcNow,
            PublishedAt: DateTimeOffset.UtcNow,
            ExpiresAt: null,
            ArtifactIdPrefix: "artifact-"));

        Assert.Equal(LegacyTriggerActivity.ActivityType, executable.RootActivity.ActivityType);
        Assert.Equal("Trigger", executable.RootActivity.Metadata[TriggerNodeMetadata.ExecutionTypeKey]);
    }

    public static TheoryData<Type, string> FirstPartyClrTriggers => new()
    {
        { typeof(EventActivity), EventActivity.ActivityType },
        { typeof(TimerActivity), TimerActivity.ActivityType },
        { typeof(CronActivity), CronActivity.ActivityType },
        { typeof(HttpEndpoint), HttpEndpoint.ActivityType }
    };

    [Theory]
    [MemberData(nameof(FirstPartyClrTriggers))]
    public async Task LegacyFirstPartyClrTriggerCatalogRow_CompilesWithTriggerProjection(Type activityType, string declaredActivityType)
    {
        var activityVersion = LegacyTriggerActivityVersion(activityType);
        var workflowVersion = WorkflowVersion(new ActivityNode("legacy-trigger", activityVersion.Id, [], []));
        var registry = TestWellKnownTypeRegistry.Create();
        registry.RegisterType(activityType, TypeAliasConvention.CanonicalAlias(activityType));
        var compiler = TestCompiler.Create(
            new FakeVersionStore(workflowVersion),
            new FakeActivityVersionStore([activityVersion]),
            _activityStructureService,
            registry);

        var executable = await compiler.CompileAsync(NewRequest(DateTimeOffset.UtcNow));

        Assert.Equal(declaredActivityType, executable.RootActivity.ActivityType);
        Assert.Equal("Trigger", executable.RootActivity.Metadata[TriggerNodeMetadata.ExecutionTypeKey]);
    }

    [Fact]
    public async Task CompilesJavaScriptBoundInputIntoRuntimeExpressionBinding()
    {
        var now = new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);
        var compiler = Compiler(WorkflowVersion(Node("write-js", JavaScriptText("\"Hello \" + \"World\""))));

        var executable = await compiler.CompileAsync(new WorkflowExecutableCompileRequest(
            VersionId: "version-1",
            Scope: WorkflowExecutableReferenceScope.Published,
            CreatedAt: now,
            PublishedAt: now,
            ExpiresAt: null,
            ArtifactIdPrefix: "artifact-"));

        var binding = Assert.Contains("Text", (IReadOnlyDictionary<string, RuntimeInputBinding>)executable.RootActivity.InputBindings);
        Assert.Equal(RuntimeInputBindingSource.Expression, binding.Source);
        Assert.Null(binding.LiteralValue);
        var expression = binding.Expression;
        Assert.NotNull(expression);
        Assert.Equal("JavaScript", expression!.Language);
        Assert.Equal("\"Hello \" + \"World\"", expression.Expression);
        Assert.Equal("String", expression.ResultType?.Id);
        Assert.Equal("String", binding.TargetType.Alias);
        Assert.Equal("Text", binding.Metadata["referenceKey"]);
    }

    [Fact]
    public async Task CompilesVariableReferenceInputIntoRuntimeExpressionBinding()
    {
        // #206: a structured Variable reference input must compile into a runtime expression binding
        // whose language is "Variable" and whose expression text round-trips the reference (reference
        // key + declaring scope), so the runtime VariableExpressionHandler resolves it at execution time.
        var now = new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);
        var reference = JsonSerializer.SerializeToElement(new { referenceKey = "var-counter", declaringScopeId = "node-sequence" });
        var compiler = Compiler(WorkflowVersion(Node("write-var", VariableText(reference))));

        var executable = await compiler.CompileAsync(new WorkflowExecutableCompileRequest(
            VersionId: "version-1",
            Scope: WorkflowExecutableReferenceScope.Published,
            CreatedAt: now,
            PublishedAt: now,
            ExpiresAt: null,
            ArtifactIdPrefix: "artifact-"));

        var binding = Assert.Contains("Text", (IReadOnlyDictionary<string, RuntimeInputBinding>)executable.RootActivity.InputBindings);
        Assert.Equal(RuntimeInputBindingSource.VariableRead, binding.Source);
        Assert.Null(binding.LiteralValue);
        Assert.Equal("var-counter", binding.Variable?.VariableKey);
        Assert.Equal("node-sequence", binding.Variable?.DeclaringScopeId);
    }

    [Fact]
    public async Task CompilesBareVariableReferenceKeyInputIntoRuntimeExpressionBinding()
    {
        // A Variable input may carry just a bare reference key string (workflow-scope reference).
        var now = new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);
        var compiler = Compiler(WorkflowVersion(Node("write-var", VariableText(JsonSerializer.SerializeToElement("var-counter")))));

        var executable = await compiler.CompileAsync(new WorkflowExecutableCompileRequest(
            VersionId: "version-1",
            Scope: WorkflowExecutableReferenceScope.Published,
            CreatedAt: now,
            PublishedAt: now,
            ExpiresAt: null,
            ArtifactIdPrefix: "artifact-"));

        var binding = Assert.Contains("Text", (IReadOnlyDictionary<string, RuntimeInputBinding>)executable.RootActivity.InputBindings);
        Assert.Equal(RuntimeInputBindingSource.VariableRead, binding.Source);
        Assert.Equal("var-counter", binding.Variable?.VariableKey);
        Assert.Equal(Elsa.Expressions.Core.Models.VariableReference.WorkflowScopeId, binding.Variable?.DeclaringScopeId);
    }

    [Fact]
    public async Task CompilingVariableInputWithoutReferenceKeyThrows()
    {
        var now = new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);
        var compiler = Compiler(WorkflowVersion(Node("write-var", VariableText(JsonSerializer.SerializeToElement(new { declaringScopeId = "node-sequence" })))));

        await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() => compiler.CompileAsync(new WorkflowExecutableCompileRequest(
            VersionId: "version-1",
            Scope: WorkflowExecutableReferenceScope.Published,
            CreatedAt: now,
            PublishedAt: now,
            ExpiresAt: null,
            ArtifactIdPrefix: "artifact-")).AsTask());
    }

    [Fact]
    public async Task CompilingUnknownVersionIdThrowsTypedCompilationException()
    {
        // #397: version-source resolution used to run before the try block, so a store lookup failure for an
        // unknown VersionId escaped as a raw ArgumentException that the publish path could not distinguish from
        // a real compilation error. Resolution now runs inside the guarded region, so the failure surfaces as a
        // typed WorkflowExecutableCompilationException (DefinitionId/VersionId unknown because resolution failed).
        var now = new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);
        var compiler = Compiler(WorkflowVersion(Node("write-one", Text("hello"))));

        var exception = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() => compiler.CompileAsync(new WorkflowExecutableCompileRequest(
            VersionId: "missing-version",
            Scope: WorkflowExecutableReferenceScope.Published,
            CreatedAt: now,
            PublishedAt: now,
            ExpiresAt: null,
            ArtifactIdPrefix: "artifact-")).AsTask());

        Assert.Null(exception.DefinitionId);
        Assert.Null(exception.DefinitionVersionId);
        Assert.Contains("missing-version", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompilingExpressionInputWithoutExpressionTextThrows()
    {
        var now = new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);
        var compiler = Compiler(WorkflowVersion(Node("write-js", JavaScriptText(""))));

        await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() => compiler.CompileAsync(new WorkflowExecutableCompileRequest(
            VersionId: "version-1",
            Scope: WorkflowExecutableReferenceScope.Published,
            CreatedAt: now,
            PublishedAt: now,
            ExpiresAt: null,
            ArtifactIdPrefix: "artifact-")).AsTask());
    }

    [Fact]
    public async Task CompilesBoundTextInputIntoRuntimeLiteralBinding()
    {
        var now = new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);
        var compiler = Compiler(WorkflowVersion(Node("write-hello", Text("Hello World!"))));

        var executable = await compiler.CompileAsync(new WorkflowExecutableCompileRequest(
            VersionId: "version-1",
            Scope: WorkflowExecutableReferenceScope.Published,
            CreatedAt: now,
            PublishedAt: now,
            ExpiresAt: null,
            ArtifactIdPrefix: "artifact-"));

        var binding = Assert.Contains("Text", (IReadOnlyDictionary<string, RuntimeInputBinding>)executable.RootActivity.InputBindings);
        Assert.Equal(RuntimeInputBindingSource.Literal, binding.Source);
        Assert.Equal("Text", binding.InputName);
        Assert.Equal("Hello World!", binding.LiteralValue?.GetString());
        Assert.Equal("Text", binding.Metadata["referenceKey"]);
        Assert.Equal("String", binding.TargetType.Alias);
    }

    [Fact]
    public async Task CompiledBoundTextInputMaterializesAuthoredValueForTheRuntime()
    {
        // End-to-end proof for the WriteLine "blank line" bug: the authored text must survive
        // compilation as a runtime input binding and materialize back into the value the runtime
        // feeds to WriteLine.Text. A regression here is exactly what prints a blank line.
        var now = new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);
        var compiler = Compiler(WorkflowVersion(Node("write-hello", Text("Hello World!"))));

        var executable = await compiler.CompileAsync(new WorkflowExecutableCompileRequest(
            VersionId: "version-1",
            Scope: WorkflowExecutableReferenceScope.Published,
            CreatedAt: now,
            PublishedAt: now,
            ExpiresAt: null,
            ArtifactIdPrefix: "artifact-"));

        var materialized = await MaterializeInputsAsync(executable, "write-hello", now);

        var textInput = Assert.Single(materialized.Values);
        Assert.Equal("Text", textInput.Key);
        Assert.Equal("Hello World!", textInput.Value.InlineValue?.Deserialize<string>());
    }

    [Fact]
    public async Task CompiledObjectCollectionInputMaterializesAuthoredArrayForTheRuntime()
    {
        var now = new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);
        var compiler = Compiler(WorkflowVersion(WriteLinesNode("write-lines", ObjectLines(["Hello", "World"]))));

        var executable = await compiler.CompileAsync(new WorkflowExecutableCompileRequest(
            VersionId: "version-1",
            Scope: WorkflowExecutableReferenceScope.Published,
            CreatedAt: now,
            PublishedAt: now,
            ExpiresAt: null,
            ArtifactIdPrefix: "artifact-"));

        var binding = Assert.Contains("Lines", (IReadOnlyDictionary<string, RuntimeInputBinding>)executable.RootActivity.InputBindings);
        Assert.Equal(RuntimeInputBindingSource.Literal, binding.Source);
        Assert.Equal(JsonValueKind.Array, binding.LiteralValue?.ValueKind);

        var materialized = await MaterializeInputsAsync(executable, "write-lines", now);

        var linesInput = Assert.Single(materialized.Values);
        var lines = Assert.IsAssignableFrom<ICollection<string>>(linesInput.Value.InlineValue?.Deserialize<string[]>());
        Assert.Equal(["Hello", "World"], lines);
    }

    [Fact]
    public async Task CompilesTransientWorkflowExecutableWithExpirationAndMetadata()
    {
        var now = new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);
        var expiresAt = now.AddMinutes(30);
        var compiler = Compiler(WorkflowVersion(SequenceNode(
            "sequence",
            [
                Node("write-one", Text("one")),
                Node("write-two", Text("two"))
            ])));

        var executable = await compiler.CompileAsync(new WorkflowExecutableCompileRequest(
            VersionId: "version-1",
            Scope: WorkflowExecutableReferenceScope.TestRun,
            CreatedAt: now,
            PublishedAt: null,
            ExpiresAt: expiresAt,
            ArtifactIdPrefix: "test-artifact-",
            CompatibilityMetadata: new Dictionary<string, string> { ["runtime.testRunId"] = "testrun-1" }));

        // Scope/expiry are reference facts now (ADR 0040); the compiled artifact is pure behavior. The compile
        // request still carries them (the publish/test-run handlers stamp them onto the reference), but the
        // executable itself only exposes behavior + compatibility metadata.
        Assert.StartsWith("test-artifact-", executable.Identity.ArtifactId, StringComparison.Ordinal);
        Assert.Equal("testrun-1", executable.CompatibilityMetadata["runtime.testRunId"]);
        Assert.Equal(3, executable.Nodes.Count);
    }

    [Fact]
    public async Task CompilesDraftSnapshotWithoutReadingDurableWorkflowVersion()
    {
        var now = new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);
        var compiler = TestCompiler.Create(
            new ThrowingVersionStore(),
            new FakeActivityVersionStore([_writeLineActivity]),
            _activityStructureService,
            TestWellKnownTypeRegistry.Create());

        var executable = await compiler.CompileAsync(new WorkflowExecutableCompileRequest(
            VersionId: "draft:snapshot-1",
            Scope: WorkflowExecutableReferenceScope.TestRun,
            CreatedAt: now,
            PublishedAt: null,
            ExpiresAt: now.AddMinutes(30),
            ArtifactIdPrefix: "test-artifact-")
        {
            Source = new WorkflowExecutableCompileSource(
                DefinitionId: "definition-1",
                DefinitionVersionId: "draft:snapshot-1",
                ArtifactVersion: "draft",
                State: new WorkflowDefinitionState([], Node("write-one", Text("hello")), [], [], null),
                SourceKind: "WorkflowDraftSnapshot",
                SourceId: "snapshot-1",
                SourceVersion: "draft")
        });

        Assert.StartsWith("test-artifact-", executable.Identity.ArtifactId, StringComparison.Ordinal);
        Assert.Equal("definition-1", executable.Identity.DefinitionId);
        Assert.Equal("draft:snapshot-1", executable.Identity.DefinitionVersionId);
    }

    [Fact]
    public async Task CompilesContainerScopedVariablesIntoExecutableStructure()
    {
        // Publishing/runtime materialization (#207): container-scoped variable declarations authored
        // on a Sequence must survive compilation into the executable structure so the runtime can
        // read them without re-reading the design document.
        var now = new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);
        var counter = new Elsa.Expressions.Core.Models.VariableDefinition(
            ReferenceKey: "var-counter",
            Name: "Counter",
            Type: new TypeReference("String"),
            StorageDriverType: null,
            Default: new ArgumentValue("0", "Literal"));
        var compiler = Compiler(WorkflowVersion(SequenceNode(
            "sequence",
            [Node("write-one", Text("one"))],
            [counter])));

        var executable = await compiler.CompileAsync(new WorkflowExecutableCompileRequest(
            VersionId: "version-1",
            Scope: WorkflowExecutableReferenceScope.Published,
            CreatedAt: now,
            PublishedAt: now,
            ExpiresAt: null,
            ArtifactIdPrefix: "artifact-"));

        var structure = executable.RootActivity.Structure;
        Assert.NotNull(structure);
        var executableStructure = structure!.Payload.Deserialize<RuntimeVariableStructureProjection>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(executableStructure);
        var materializedVariable = Assert.Single(executableStructure!.Variables);
        Assert.Equal("var-counter", materializedVariable.VariableKey);
        Assert.Equal("Counter", materializedVariable.Name);
    }

    [Fact]
    public async Task IndexesResumeTargetHandlerIntoExecutable()
    {
        var now = new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);
        var compiler = ResumeCompiler(WorkflowVersion(ResumeNode("delay-1")), _resumeProbeActivity);

        var executable = await compiler.CompileAsync(NewRequest(now));

        var resumeTarget = Assert.Contains(
            "delay-1:resume-target:probe",
            (IReadOnlyDictionary<string, WorkflowExecutableResumeTarget>)executable.ResumeTargets);
        Assert.Equal("delay-1", resumeTarget.ExecutableNodeId);
        Assert.Equal("resume-target:probe", resumeTarget.LocalResumeTargetId);
        Assert.Equal(nameof(ResumeProbeActivity.OnResumeAsync), resumeTarget.HandlerKey);
    }

    [Fact]
    public async Task ActivitiesWithoutResumeTargetsProduceEmptyResumeTargetMap()
    {
        var now = new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);
        var compiler = Compiler(WorkflowVersion(Node("write-one", Text("hello"))));

        var executable = await compiler.CompileAsync(NewRequest(now));

        Assert.Empty(executable.ResumeTargets);
    }

    [Fact]
    public async Task MultipleInstancesOfAResumeTargetActivityCompileToNodeScopedTargets()
    {
        var now = new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);
        var compiler = ResumeCompiler(
            WorkflowVersion(SequenceNode("seq", [ResumeNode("delay-1"), ResumeNode("delay-2")])),
            _resumeProbeActivity,
            _sequenceActivity);

        var executable = await compiler.CompileAsync(NewRequest(now));

        Assert.Equal(2, executable.ResumeTargets.Count);
        foreach (var nodeId in new[] { "delay-1", "delay-2" })
        {
            var target = Assert.Contains(
                $"{nodeId}:resume-target:probe",
                (IReadOnlyDictionary<string, WorkflowExecutableResumeTarget>)executable.ResumeTargets);
            Assert.Equal(nodeId, target.ExecutableNodeId);
            Assert.Equal("resume-target:probe", target.LocalResumeTargetId);
        }
    }

    private static ValueTask<ActivityInputSnapshot> MaterializeInputsAsync(
        WorkflowExecutable executable,
        string invocationId,
        DateTimeOffset materializedAt) =>
        new RuntimeActivityInputMaterializer(new RuntimeInputBindingResolver(), TestWellKnownTypeRegistry.Create())
            .MaterializeSnapshotAsync(
                executable.RootActivity,
                invocationId,
                new RuntimeInputBindingResolutionContext(
                    "workflow-execution-test",
                    invocationId,
                    executable: executable),
                materializedAt);

    private static async Task<DurableValueState> CompleteReusableBoundaryAsync(
        WorkflowExecutable executable,
        IRuntimeDurableValueStorageDriverRegistry storageDrivers,
        object? outputValue)
    {
        const string workflowExecutionId = "wfexec-reusable";
        const string boundaryExecutionId = "actexec-reusable";
        const string childExecutionId = "actexec-reusable-child";
        var boundary = executable.RootActivity;
        var child = Assert.Single(boundary.ChildSlots.SelectMany(x => x.Activities));
        var executableStore = new InMemoryWorkflowExecutableStore();
        var activityStore = new InMemoryActivityExecutionStateStore();
        var schedulerQueue = new InMemoryWorkflowSchedulerWorkQueue();
        var durableStore = new InMemoryDurableValueStateStore();
        var inspectionStore = new InMemoryActivityExecutionInspectionStore();
        var incidentStore = new InMemoryIncidentStateStore();
        var checkpointStore = new InMemoryRuntimeCheckpointCommitStore(
            workflowExecutionStateStore: null,
            activityExecutionStateStore: activityStore,
            bookmarkStateStore: null,
            durableValueStateStore: durableStore,
            incidentStateStore: incidentStore,
            operationalStateStore: null,
            schedulerStateStore: null,
            activityExecutionInspectionWriter: inspectionStore);

        await executableStore.SaveAsync(executable);
        var boundaryContract = boundary.ActivityContract
            ?? throw new InvalidOperationException("The reusable boundary must carry its pinned runtime activity contract.");
        var boundaryState = RuntimeState(boundaryExecutionId, boundary, ActivityExecutionStatus.Running) with
        {
            ContractIdentity = new ActivityInvocationContractIdentity(
                boundaryContract.ActivityTypeKey,
                boundaryContract.ContractVersion,
                boundaryContract.SchemaFingerprint),
            InputSnapshot = new ActivityInputSnapshot(
                boundaryExecutionId,
                boundaryContract.SchemaFingerprint,
                "sha256:reusable-boundary-bindings",
                new Dictionary<string, ValueEnvelope>(),
                DateTimeOffset.UtcNow.AddMinutes(-2)),
            Attempts =
            [
                new ActivityAttempt(
                    $"{boundaryExecutionId}:attempt:1",
                    boundaryExecutionId,
                    1,
                    ActivityAttemptReason.Initial,
                    DateTimeOffset.UtcNow.AddMinutes(-2),
                    DateTimeOffset.UtcNow.AddMinutes(-1),
                    transitionKind: Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind.Suspend)
            ]
        };
        await activityStore.SaveAsync(boundaryState);
        await activityStore.SaveAsync(RuntimeState(childExecutionId, child, ActivityExecutionStatus.Completed, boundaryExecutionId));

        var services = new ServiceCollection();
        services.AddScoped<IActivityActivator>(_ => new FixedActivityActivator(new OutputtingCompositeActivity(outputValue)));
        services.AddSingleton<IWorkflowExecutableStore>(executableStore);
        services.AddSingleton<IActivityExecutionStateStore>(activityStore);
        services.AddSingleton<IWorkflowExecutionStateStore, InMemoryWorkflowExecutionStateStore>();
        services.AddSingleton<IWorkflowSchedulerWorkQueue>(schedulerQueue);
        services.AddSingleton(storageDrivers);
        services.AddSingleton<IDurableValueStateStore>(durableStore);
        services.AddSingleton<IIncidentStateStore>(incidentStore);
        services.AddSingleton<IRuntimeExecutionIdGenerator, ShortRuntimeExecutionIdGenerator>();
        services.AddSingleton<IActivityExecutionInspectionStore>(inspectionStore);
        services.AddSingleton<IRuntimeActivityExecutionInspectionAccumulator, RuntimeActivityExecutionInspectionAccumulator>();
        services.AddSingleton<IRuntimeCheckpointCommitStore>(checkpointStore);
        services.AddSingleton<IRuntimeCheckpointPersistencePolicy, ImmediateRuntimeCheckpointPersistencePolicy>();
        services.AddSingleton<IRuntimePostCommitIntentDispatcher, RuntimeSchedulerPostCommitIntentDispatcher>();
        services.AddSingleton<RuntimeCheckpointCommitter>();
        services.AddSingleton<ActivityCompletionProjector>();
        services.AddSingleton<RuntimeOutputCaptureProjector>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ActivityFaultIncidentRecorder>();
        await using var provider = services.BuildServiceProvider();
        var handler = new WorkflowParentActivityCompletionSchedulerWorkHandler(
            provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System);
        var now = DateTimeOffset.UtcNow;
        var payload = new RuntimeCompleteActivityCommandPayload(
            executable.Identity,
            boundary.ExecutableNodeId,
            boundaryExecutionId,
            parentActivityExecutionId: null,
            branchId: null,
            outcomeNames: [ActivityOutcomes.Done],
            reason: RuntimeCompleteActivityCommandPayload.ParentCompletionEvaluationReason,
            completionKind: SchedulerCompletionKind.ParentCompletionEvaluation,
            completedChildActivityExecutionId: childExecutionId);
        var workItem = new RuntimeSchedulerWorkItem(
            "work-reusable-complete",
            workflowExecutionId,
            "command-reusable-complete",
            WorkflowExecutionCommandKind.CompleteActivity,
            "envelope-reusable",
            "reusable-complete",
            now,
            now,
            1,
            JsonSerializer.SerializeToElement(payload),
            new Dictionary<string, string>(),
            new Dictionary<string, string>());

        await handler.HandleAsync(workItem);

        var captured = await durableStore.FindAsync(workflowExecutionId, "durable-variable:CallerResult");
        if (captured is not null)
            return captured;
        var stored = await durableStore.ListAllDurableValueStatesAsync(workflowExecutionId);
        var incidents = await incidentStore.ListAsync(workflowExecutionId);
        throw new Xunit.Sdk.XunitException(
            $"Normal Runtime boundary completion did not write the compiled caller output target. " +
            $"Stored=[{string.Join(',', stored.Select(x => x.DurableValueId))}], " +
            $"Incidents=[{string.Join(" | ", incidents.Select(x => $"{x.FailureType}:{x.Message}"))}].");
    }

    private static ActivityExecutionState RuntimeState(
        string activityExecutionId,
        ExecutableNode node,
        ActivityExecutionStatus status,
        string? parentActivityExecutionId = null) => new(
        Execution: new ActivityExecution(
            activityExecutionId,
            "wfexec-reusable",
            node.ExecutableNodeId,
            node.AuthoredActivityId,
            node.ActivityType,
            node.ActivityTypeVersion),
        Status: status,
        SubStatus: null,
        ScheduledAt: DateTimeOffset.UtcNow.AddMinutes(-2),
        StartedAt: DateTimeOffset.UtcNow.AddMinutes(-1),
        CompletedAt: status == ActivityExecutionStatus.Completed ? DateTimeOffset.UtcNow : null,
        SchedulingActivityExecutionId: null,
        ParentActivityExecutionId: parentActivityExecutionId,
        BranchId: null,
        IterationId: null,
        CallStackDepth: null,
        BookmarkIds: [],
        IncidentIds: [],
        FaultCount: 0,
        AggregateFaultCount: 0,
        Metadata: new Dictionary<string, string>());

    private sealed class FixedActivityActivator(IActivity activity) : IActivityActivator
    {
        public ValueTask<ActivityActivationLease> ActivateAsync(
            ActivityActivationRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ActivityActivationLease(activity));
    }

    private sealed class OutputtingCompositeActivity(object? outputValue) :
        StructuralActivity,
        IRuntimeActivityChildCompletionHandler,
        IRuntimeActivityCheckpointParticipant
    {
        public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context) =>
            ValueTask.FromResult(RuntimeStructuralContinuation.Complete());

        public ValueTask<IReadOnlyCollection<RuntimeStateChange<DurableValueState>>> PrepareEntryCheckpointAsync(
            IRuntimeActivityExecutionContext context,
            IReadOnlyDictionary<string, object?> effectiveInputs,
            DateTimeOffset capturedAt,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyCollection<RuntimeStateChange<DurableValueState>>>([]);

        public ValueTask<RuntimeActivityCompletionCheckpointPreparation> PrepareCompletionCheckpointAsync(
            IRuntimeActivityExecutionContext context,
            IReadOnlyCollection<DurableValueState> persistedValues,
            DateTimeOffset capturedAt,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new RuntimeActivityCompletionCheckpointPreparation(
                [],
                ActivityTransition.Complete(outputValue, ActivityOutcomes.Done)));
    }

    private sealed class OpaqueOutput(string value)
    {
        public string Value { get; } = value;
    }

    private sealed class OpaqueOutputStorageDriver : IRuntimeDurableValueStorageDriver
    {
        private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);
        private int _next;
        public string DriverKey => "test.opaque.external";

        public ValueTask<RuntimeDurableValueEncoding> EncodeAsync(
            object? value,
            RuntimeValueTypeDescriptor type,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var locator = $"opaque-{Interlocked.Increment(ref _next)}";
            _values.Add(locator, value);
            return ValueTask.FromResult(new RuntimeDurableValueEncoding(
                DurableValueStorage.External,
                null,
                new DurableValueExternalReference(DriverKey, locator, new Dictionary<string, string>())));
        }

        public ValueTask<object?> DecodeAsync(DurableValueState state, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!StringComparer.Ordinal.Equals(state.Type.Id, DriverKey) ||
                state.ExternalReference is not { } reference ||
                !_values.TryGetValue(reference.Locator, out var value))
            {
                throw new InvalidOperationException($"Durable value '{state.DurableValueId}' is not encoded by '{DriverKey}'.");
            }
            return ValueTask.FromResult(value);
        }
    }

    private static WorkflowExecutableCompileRequest NewRequest(DateTimeOffset now) =>
        new(
            VersionId: "version-1",
            Scope: WorkflowExecutableReferenceScope.Published,
            CreatedAt: now,
            PublishedAt: now,
            ExpiresAt: null,
            ArtifactIdPrefix: "artifact-");

    private async Task<WorkflowExecutable> CompileWithDependenciesAsync(
        WorkflowDefinitionVersion workflow,
        IReadOnlyCollection<ExecutableDependencyClaim> dependencies)
    {
        var compiler = TestCompiler.Create(
            new FakeVersionStore(workflow),
            new FakeActivityVersionStore([_writeLineActivity, _writeLinesActivity, _sequenceActivity, _legacyTriggerActivity]),
            _activityStructureService,
            TestWellKnownTypeRegistry.Create(),
            metadataEnricher: MetadataEnricher(new FixedDependencySource(dependencies)));

        return await compiler.CompileAsync(NewRequest(DateTimeOffset.UtcNow));
    }

    private WorkflowExecutableCompiler ValidatedDependencyCompiler(
        WorkflowDefinitionVersion workflow,
        IReadOnlyCollection<ExecutableDependencyClaim> dependencies,
        IWorkflowExecutableStore executableStore)
    {
        var publications = new EmptyActivityPublicationStore();
        var templates = new EmptyActivityTemplateReader();
        var references = new EmptySourceReferenceReader();
        var registry = TestWellKnownTypeRegistry.Create();
        var inputCompiler = new RuntimeInputBindingCompiler(registry);
        return new WorkflowExecutableCompiler(
            new FakeVersionStore(workflow),
            new FakeActivityVersionStore([_writeLineActivity, _writeLinesActivity, _sequenceActivity, _legacyTriggerActivity]),
            publications,
            templates,
            references,
            new ActivityTemplatePlacer(publications, templates, references, new Sha256ActivityPlacementHasher()),
            inputCompiler,
            new RuntimeOutputCaptureCompiler(new RuntimeDurableValueStorageDriverRegistry(
                [new JsonRuntimeDurableValueStorageDriver()])),
            new WorkflowExecutableHasher(),
            new ActivityTreeProjector(_activityStructureService),
            new ExecutableNodeCompiler(
                _activityStructureService,
                registry,
                inputCompiler),
            placementSidecars: null,
            metadataEnricher: MetadataEnricher(new FixedDependencySource(dependencies)),
            executableStore: executableStore);
    }

    private WorkflowExecutable StoredExecutable(
        WorkflowExecutableIdentity identity,
        params WorkflowExecutableDependency[] dependencies) =>
        TestExecutable.Create(identity, dependencies);

    private static WorkflowExecutableIdentity StoredIdentity(
        string artifactId,
        string artifactHash,
        string definitionId = "definition-child",
        string definitionVersionId = "version-child") =>
        TestExecutable.Identity(artifactId, artifactHash, definitionId, definitionVersionId);

    private WorkflowExecutableCompiler ResumeCompiler(WorkflowDefinitionVersion workflowVersion, params ActivityDefinitionVersion[] activities)
    {
        var registry = TestWellKnownTypeRegistry.Create();
        registry.RegisterType(typeof(ResumeProbeActivity), typeof(ResumeProbeActivity).FullName!);
        registry.RegisterType(typeof(SequenceActivity), typeof(SequenceActivity).FullName!);

        return TestCompiler.Create(
            new FakeVersionStore(workflowVersion),
            new FakeActivityVersionStore([.. activities]),
            _activityStructureService,
            registry);
    }

    private static ActivityNode ResumeNode(string nodeId) => new(nodeId, "activity-probe", Inputs: [], Outputs: []);

    private readonly ActivityDefinitionVersion _resumeProbeActivity = ActivityVersion("activity-probe", typeof(ResumeProbeActivity).FullName!);

    // A minimal type carrying a [ResumeTarget] handler plus the typed-result marker the publish-time
    // contract gate requires of every resolvable CLR activity.
    private sealed class ResumeProbeActivity : IActivityResult<ActivityUnit>
    {
        [ResumeTarget("resume-target:probe")]
        public ValueTask OnResumeAsync() => ValueTask.CompletedTask;
    }

    private WorkflowExecutableCompiler Compiler(WorkflowDefinitionVersion workflowVersion)
    {
        var registry = TestWellKnownTypeRegistry.Create();
        registry.RegisterType(typeof(LegacyTriggerActivity), TypeAliasConvention.CanonicalAlias(typeof(LegacyTriggerActivity)));
        return TestCompiler.Create(
            new FakeVersionStore(workflowVersion),
            new FakeActivityVersionStore([_writeLineActivity, _writeLinesActivity, _sequenceActivity, _legacyTriggerActivity]),
            _activityStructureService,
            registry);
    }

    private WorkflowExecutableCompiler CompilerWithPublication(
        ActivityNode root,
        ActivityDefinitionVersionPublication publication,
        params ActivityDefinitionVersion[] activityVersions) =>
        TestCompiler.Create(
            new FakeVersionStore(WorkflowVersion(root)),
            new FakeActivityVersionStore([.. activityVersions]),
            _activityStructureService,
            TestWellKnownTypeRegistry.Create(),
            new SinglePublicationStore(publication));

    private static WorkflowDefinitionVersion WorkflowVersion(
        ActivityNode? rootActivity,
        IReadOnlyCollection<InputDefinition>? inputs = null,
        IReadOnlyCollection<Elsa.Expressions.Core.Models.VariableDefinition>? variables = null) =>
        new("definition-1", "1.0.0")
        {
            Id = "version-1",
            Definition = new WorkflowDefinition { Id = "definition-1", Name = "Demo" },
            State = new WorkflowDefinitionState(variables ?? [], rootActivity, inputs ?? [], [], null)
        };

    private static WorkflowDefinitionVersion WorkflowVersionWithCadence(
        ActivityNode? rootActivity,
        WorkflowCheckpointCadenceOptions checkpointCadence) =>
        new("definition-1", "1.0.0")
        {
            Id = "version-1",
            Definition = new WorkflowDefinition { Id = "definition-1", Name = "Demo" },
            State = new WorkflowDefinitionState(
                [],
                rootActivity,
                [],
                [],
                new WorkflowStrategyOptions { CheckpointCadence = checkpointCadence })
        };

    private static InputDefinition WorkflowInput(
        string name,
        TypeReference type,
        bool isRequired,
        JsonElement? defaultValue = null,
        string? defaultSyntax = null) =>
        new(
            ReferenceKey: $"input-{name}",
            Name: name,
            Type: type,
            StorageDriverType: null,
            DisplayName: name,
            Category: null,
            IsNullable: !isRequired,
            IsRequired: isRequired,
            DefaultValue: defaultValue,
            DefaultSyntax: defaultSyntax);

    private static ActivityNode Node(string nodeId, params WorkflowArgumentState[] inputs) =>
        new(nodeId, "activity-write-line", inputs, Outputs: []);

    private static ActivityNode WriteLinesNode(string nodeId, params WorkflowArgumentState[] inputs) =>
        new(nodeId, "activity-write-lines", inputs, Outputs: []);

    private static ActivityNode SequenceNode(
        string nodeId,
        IReadOnlyCollection<ActivityNode> activities,
        IReadOnlyCollection<Elsa.Expressions.Core.Models.VariableDefinition>? variables = null,
        string activityVersionId = "activity-sequence") =>
        new(
            nodeId,
            activityVersionId,
            Inputs: [],
            Outputs: [],
            Structure: new ActivityNodeStructure(
                SequenceActivity.StructureKind,
                SequenceActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new SequenceAuthoredStructure(activities, variables))));

    private static ActivityNode FlowchartNode(string nodeId, IReadOnlyCollection<ActivityNode> activities) =>
        new(
            nodeId,
            "activity-flowchart",
            Inputs: [],
            Outputs: [],
            Structure: new ActivityNodeStructure(
                FlowchartActivity.StructureKind,
                FlowchartActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new FlowchartAuthoredStructure(activities))));

    private static ActivityDefinitionVersionPublication SourceOwnedPublication(
        ActivityDefinitionVersion version,
        ActivityDefinitionVersionResolutionKind resolutionKind) => new()
    {
        Id = version.Id,
        DefinitionVersionId = version.Id,
        DefinitionId = version.DefinitionId,
        Version = version.Version,
        ActivityTypeKey = version.Definition!.ActivityTypeKey,
        ResolutionKind = resolutionKind,
        Contract = new DesignActivityContract("1", [], [], []),
        Provider = new(version.ProviderKey, version.ProviderSchemaVersion, version.DescriptorPayload),
        TemplateId = "source-owned-template",
        TemplateHash = "source-owned-hash",
        SourceReferenceId = "source-owned-reference",
        ProviderFingerprint = "source-owned-fingerprint",
        DirectDependencyCount = 0,
        ClosedTemplateCount = 0,
        RuntimeRequirements = []
    };

    private static Elsa.Activities.Runtime.Core.Models.ActivityContract BoundaryRuntimeContract(bool hasValueInput)
    {
        var descriptor = JsonSerializer.SerializeToElement(new { plan = 1 });
        var valueType = new ValueTypeDescriptor("Object");
        var inputs = hasValueInput
            ? new[]
            {
                new Elsa.Activities.Runtime.Core.Models.ActivityInputContract(
                    "value",
                    "Value",
                    new ValueTypeDescriptor("Int32"),
                    isRequired: true,
                    isNullable: false,
                    hasDefault: false,
                    defaultValue: null,
                    policy: ActivityValuePolicy.Default)
            }
            : [];
        return new Elsa.Activities.Runtime.Core.Models.ActivityContract(
            "test.boundary",
            "1",
            "test.boundary",
            descriptor,
            inputs,
            new ActivityResultContract(
                valueType,
                isRequired: true,
                policy: ActivityValuePolicy.Default with { Lifecycle = ActivityValueLifecycle.Result },
                projections: [new ActivityResultProjectionContract(
                    "Result",
                    "$",
                    valueType,
                    isRequired: true,
                    policy: ActivityValuePolicy.Default with { Lifecycle = ActivityValueLifecycle.Result })]),
            [ActivityOutcomes.Done],
            new ActivityActivationRequirement("test.boundary", "test"));
    }

    private static WorkflowArgumentState Text(string value) =>
        new("Text", new ArgumentValue(value, "Literal"), null, null, null, null);

    private static WorkflowArgumentState JavaScriptText(string expression) =>
        new("Text", new ArgumentValue(expression, "JavaScript"), null, null, null, null);

    private static WorkflowArgumentState VariableText(JsonElement reference) =>
        new("Text", new ArgumentValue(reference, "Variable"), null, null, null, null);

    private static WorkflowArgumentState ObjectLines(IReadOnlyCollection<string> lines) =>
        new("Lines", new ArgumentValue(JsonSerializer.SerializeToElement(lines), "Object"), null, null, null, null);

    private static ActivityDefinitionVersion ActivityVersion(string id, string inputName, TypeReference inputType) =>
        ActivityVersion(
            id,
            "Test.WriteLine",
            [new InputDefinition(inputName, inputName, inputType, null, inputName, null, IsNullable: true)]);

    private static ActivityDefinitionVersion ActivityVersion(string id, string activityTypeKey, IReadOnlyCollection<InputDefinition>? inputs = null) =>
        new("1.0.0", "activity-definition-1")
        {
            Id = id,
            Definition = new ActivityDefinition
            {
                Id = "activity-definition-1",
                ActivityTypeKey = activityTypeKey,
                Category = "Test"
            },
            ProviderKey = WellKnownRuntimeActivityConsumers.ClrActivity,
            ProviderSchemaVersion = RuntimeActivityDescriptor.InitialSchemaVersion,
            ConsumerKey = WellKnownRuntimeActivityConsumers.ClrActivity,
            ConsumerSchemaVersion = RuntimeActivityDescriptor.InitialSchemaVersion,
            DescriptorPayload = JsonSerializer.SerializeToElement(new ClrActivityDescriptor(
                id switch
                {
                    "activity-probe" => TypeAliasConvention.CanonicalAlias(typeof(ResumeProbeActivity)),
                    _ => TestActivityAliases.ForActivityVersionId(id)
                })),
            Inputs = inputs ?? []
        };

    private static ActivityDefinitionVersion GhostAliasActivityVersion() =>
        ClrActivityVersion("activity-ghost", "Test.Ghost", "Test.UnregisteredActivity");

    private static ActivityDefinitionVersion ContractlessActivityVersion() =>
        ClrActivityVersion("activity-contractless", "Test.Contractless", TypeAliasConvention.CanonicalAlias(typeof(ContractlessActivity)));

    private static ActivityDefinitionVersion ClrActivityVersion(string id, string activityTypeKey, string typeAlias) =>
        new("1.0.0", $"{id}-definition")
        {
            Id = id,
            Definition = new ActivityDefinition
            {
                Id = $"{id}-definition",
                ActivityTypeKey = activityTypeKey,
                Category = "Test"
            },
            ProviderKey = WellKnownRuntimeActivityConsumers.ClrActivity,
            ProviderSchemaVersion = RuntimeActivityDescriptor.InitialSchemaVersion,
            ConsumerKey = WellKnownRuntimeActivityConsumers.ClrActivity,
            ConsumerSchemaVersion = RuntimeActivityDescriptor.InitialSchemaVersion,
            DescriptorPayload = JsonSerializer.SerializeToElement(new ClrActivityDescriptor(typeAlias)),
            Inputs = []
        };

    // Resolvable CLR type that declares no IActivityResult<T>: contract projection yields null, which the
    // publish-time VF-ACT-001 gate must refuse.
    private sealed class ContractlessActivity;

    private sealed class ContractStrippingEnricher : IExecutableNodeMetadataEnricher
    {
        public async ValueTask<ExecutableNode> EnrichAsync(
            WorkflowExecutableCompileRequest request,
            WorkflowExecutableCompileSource source,
            ExecutableNode rootActivity,
            CancellationToken cancellationToken = default) =>
            (await EnrichCompilationAsync(request, source, rootActivity, cancellationToken)).RootActivity;

        public ValueTask<ExecutableCompilationEnrichment> EnrichCompilationAsync(
            WorkflowExecutableCompileRequest request,
            WorkflowExecutableCompileSource source,
            ExecutableNode rootActivity,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ExecutableCompilationEnrichment(Strip(rootActivity), []));

        private static ExecutableNode Strip(ExecutableNode node) => new(
            node.ExecutableNodeId,
            node.AuthoredActivityId,
            node.ActivityType,
            node.ActivityTypeVersion,
            node.Descriptor,
            node.InputBindings,
            node.OutputCaptures,
            node.Metadata,
            node.ChildSlots.Select(slot => new ExecutableChildSlot(
                slot.Name,
                slot.Activities.Select(Strip).ToArray())).ToArray(),
            node.Structure);
    }

    private sealed class RuntimeVariableStructureProjection
    {
        public IReadOnlyCollection<RuntimeVariableDeclaration> Variables { get; init; } = [];
    }

    private static ActivityDefinitionVersion LegacyTriggerActivityVersion() =>
        LegacyTriggerActivityVersion(typeof(LegacyTriggerActivity));

    private static ActivityDefinitionVersion LegacyTriggerActivityVersion(Type activityType) =>
        new("1.0.0", "legacy-trigger-definition")
        {
            Id = "activity-legacy-trigger",
            Definition = new ActivityDefinition
            {
                Id = "legacy-trigger-definition",
                ActivityTypeKey = activityType.FullName!,
                Category = "Test"
            },
            ProviderKey = WellKnownRuntimeActivityConsumers.ClrActivity,
            ProviderSchemaVersion = RuntimeActivityDescriptor.InitialSchemaVersion,
            ConsumerKey = WellKnownRuntimeActivityConsumers.ClrActivity,
            ConsumerSchemaVersion = RuntimeActivityDescriptor.InitialSchemaVersion,
            DescriptorPayload = JsonSerializer.SerializeToElement(
                new ClrActivityDescriptor(TypeAliasConvention.CanonicalAlias(activityType)),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            ExecutionType = ActivityExecutionType.Action,
            Inputs = []
        };

    [TriggerActivity]
    private sealed class LegacyTriggerActivity : IActivityResult<ActivityUnit>
    {
        public const string ActivityType = "Elsa.Test.LegacyTrigger";
    }

    private static IActivityStructureService ActivityStructureService()
    {
        var services = new ServiceCollection();
        services.AddScoped<IActivityStructureService, DefaultActivityStructureService>();
        new ActivitiesSequenceFeature().ConfigureServices(services);
        new ActivitiesFlowchartFeature().ConfigureServices(services);

        return services.BuildServiceProvider().GetRequiredService<IActivityStructureService>();
    }

    private sealed class FakeVersionStore(WorkflowDefinitionVersion version) : IWorkflowDefinitionVersionStore
    {
        public Task<WorkflowDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) =>
            version.Id == versionId
                ? Task.FromResult(version)
                : throw new ArgumentException($"Workflow definition version with id '{versionId}' does not exist");

        public Task<WorkflowDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkflowDefinitionVersion?> FindByIdAsync(string versionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkflowDefinitionVersion?> FindLatestVersionAsync(string definitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<WorkflowDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ThrowingVersionStore : IWorkflowDefinitionVersionStore
    {
        public Task<WorkflowDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Draft snapshot compilation must not read durable workflow definition versions.");

        public Task<WorkflowDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkflowDefinitionVersion?> FindByIdAsync(string versionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkflowDefinitionVersion?> FindLatestVersionAsync(string definitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<WorkflowDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class EmptyActivityPublicationStore : IActivityDefinitionVersionPublicationStore
    {
        public Task<ActivityDefinitionVersionPublication?> FindAsync(string definitionVersionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ActivityDefinitionVersionPublication?>(null);

        public Task<IReadOnlyList<ActivityDefinitionVersionPublication>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ActivityDefinitionVersionPublication>>([]);
    }

    private sealed class EmptyActivityTemplateReader : IExecutableActivityTemplateReader
    {
        public ValueTask<ExecutableActivityTemplate?> FindAsync(string templateId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ExecutableActivityTemplate?>(null);

        public ValueTask<ExecutableActivityTemplate?> FindByHashAsync(string templateHash, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ExecutableActivityTemplate?>(null);

        public ValueTask<RuntimeStorePage<ExecutableActivityTemplate>> ListPageAsync(RuntimeStorePageRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(TestRuntimeStorePages.Page(request, Array.Empty<ExecutableActivityTemplate>(), template => template.TemplateId, "activity-template"));
    }

    private sealed class EmptySourceReferenceReader : IWorkflowExecutableSourceReferenceReader
    {
        public ValueTask<WorkflowExecutableSourceReference?> FindAsync(string sourceReferenceId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<WorkflowExecutableSourceReference?>(null);

        public ValueTask<RuntimeStorePage<WorkflowExecutableSourceReference>> ListByArtifactPageAsync(WorkflowExecutableSourceReferenceArtifactPageQuery query, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(TestRuntimeStorePages.Page(query, Array.Empty<WorkflowExecutableSourceReference>(), reference => reference.SourceReferenceId, $"source-reference:artifact:{query.ArtifactId}"));

        public ValueTask<RuntimeStorePage<WorkflowExecutableSourceReference>> ListPageAsync(WorkflowExecutableSourceReferencePageQuery query, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(TestRuntimeStorePages.Page(query, Array.Empty<WorkflowExecutableSourceReference>(), reference => reference.SourceReferenceId, TestRuntimeStorePages.SourceReferenceQueryBinding(query)));

        public ValueTask<IReadOnlyCollection<string>> ListUnreferencedArtifactIdsAsync(WorkflowExecutableArtifactCandidateBatch candidates, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(TestRuntimeStorePages.UnreferencedArtifactIds(candidates, Array.Empty<WorkflowExecutableSourceReference>(), now));
    }

    private sealed class SinglePublicationStore(ActivityDefinitionVersionPublication publication) : IActivityDefinitionVersionPublicationStore
    {
        public Task<ActivityDefinitionVersionPublication?> FindAsync(string definitionVersionId, CancellationToken cancellationToken = default) => Task.FromResult(definitionVersionId == publication.DefinitionVersionId ? publication : null);
        public Task<IReadOnlyList<ActivityDefinitionVersionPublication>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ActivityDefinitionVersionPublication>>(definitionId == publication.DefinitionId ? [publication] : []);
    }

    private sealed class ReusableTemplateReader(ExecutableActivityTemplate template) : IExecutableActivityTemplateReader
    {
        public ValueTask<ExecutableActivityTemplate?> FindAsync(string templateId, CancellationToken cancellationToken = default) => ValueTask.FromResult(templateId == template.TemplateId ? template : null);
        public ValueTask<ExecutableActivityTemplate?> FindByHashAsync(string templateHash, CancellationToken cancellationToken = default) => ValueTask.FromResult(templateHash == template.TemplateHash ? template : null);
        public ValueTask<RuntimeStorePage<ExecutableActivityTemplate>> ListPageAsync(RuntimeStorePageRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(TestRuntimeStorePages.Page(request, new[] { template }, candidate => candidate.TemplateId, "activity-template"));
    }

    private sealed class ReusableSourceReader(WorkflowExecutableSourceReference reference) : IWorkflowExecutableSourceReferenceReader
    {
        public ValueTask<WorkflowExecutableSourceReference?> FindAsync(string sourceReferenceId, CancellationToken cancellationToken = default) => ValueTask.FromResult(sourceReferenceId == reference.SourceReferenceId ? reference : null);
        public ValueTask<RuntimeStorePage<WorkflowExecutableSourceReference>> ListByArtifactPageAsync(WorkflowExecutableSourceReferenceArtifactPageQuery query, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(TestRuntimeStorePages.Page(query, query.ArtifactId == reference.ArtifactId ? new[] { reference } : Array.Empty<WorkflowExecutableSourceReference>(), candidate => candidate.SourceReferenceId, $"source-reference:artifact:{query.ArtifactId}"));
        public ValueTask<RuntimeStorePage<WorkflowExecutableSourceReference>> ListPageAsync(WorkflowExecutableSourceReferencePageQuery query, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(TestRuntimeStorePages.Page(query, (query.Scope is null || reference.Scope == query.Scope) && (!query.LiveOnly || reference.IsLive(query.Now!.Value)) ? new[] { reference } : Array.Empty<WorkflowExecutableSourceReference>(), candidate => candidate.SourceReferenceId, TestRuntimeStorePages.SourceReferenceQueryBinding(query)));
        public ValueTask<IReadOnlyCollection<string>> ListUnreferencedArtifactIdsAsync(WorkflowExecutableArtifactCandidateBatch candidates, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(TestRuntimeStorePages.UnreferencedArtifactIds(candidates, new[] { reference }, now));
    }

    private static ExecutableNodeMetadataEnricher MetadataEnricher(params IExecutableCompilationSource[] sources) =>
        new(new CollectingInlineEventPublisher(sources));

    private sealed class FixedMetadataSource(string nodeId, string key, string value) : IExecutableCompilationSource
    {
        public ValueTask<ExecutableCompilationContribution> GetContributionAsync(
            ExecutableCompilationContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ExecutableCompilationContribution(
                nodeMetadata: [new ExecutableNodeMetadataClaim(nodeId, key, value)]));
    }

    private sealed class FixedDependencySource(IReadOnlyCollection<ExecutableDependencyClaim> claims) : IExecutableCompilationSource
    {
        public ValueTask<ExecutableCompilationContribution> GetContributionAsync(
            ExecutableCompilationContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ExecutableCompilationContribution(dependencies: claims));
    }

    private sealed class CollectingInlineEventPublisher(IEnumerable<IExecutableCompilationSource> sources) : IInlineEventPublisher
    {
        private readonly CollectExecutableCompilation _handler = new(sources);

        public Task Publish(IEvent @event, CancellationToken cancellationToken = default) =>
            _handler.Handle(Assert.IsType<OnExecutableCompilationCollecting>(@event), cancellationToken);
    }
}
