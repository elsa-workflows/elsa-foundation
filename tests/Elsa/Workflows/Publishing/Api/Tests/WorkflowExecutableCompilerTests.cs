using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Scheduling.Activities;
using Elsa.Activities.Sequence;
using Elsa.Activities.Sequence.Models;
using Elsa.Persistence.Core;
using Elsa.Primitives.Entities;
using Elsa.Primitives.Models;
using Elsa.Primitives.Persistence;
using Elsa.Expressions.Core.Contracts;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ArgumentValue = Elsa.Expressions.Core.Models.ArgumentValue;
using WorkflowArgumentState = Elsa.Workflows.Design.Core.Models.ArgumentState;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;
using CronActivity = Elsa.Activities.Scheduling.Activities.Cron;
using EventActivity = Elsa.Activities.Primitives.Activities.Event;
using TimerActivity = Elsa.Activities.Scheduling.Activities.Timer;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed class WorkflowExecutableCompilerTests
{
    private readonly ActivityDefinitionVersion _writeLineActivity = ActivityVersion("activity-write-line", "Text", new TypeReference("String"));
    private readonly ActivityDefinitionVersion _writeLinesActivity = ActivityVersion("activity-write-lines", "Lines", new TypeReference("String", CollectionKind.List));
    private readonly ActivityDefinitionVersion _sequenceActivity = ActivityVersion("activity-sequence", typeof(SequenceActivity).FullName!);
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
    public async Task Resolves_exact_reusable_version_places_template_and_compiles_expression_default()
    {
        var contract = new ActivityContract("1", [new ActivityInputContract(
            "value", "Value", new TypeReference("Int32"), true,
            new("JavaScript", JsonSerializer.SerializeToElement("40 + 2")), "elsa.json")], [new ActivityOutputContract(
            "result", "Result", new TypeReference("Int32"), true, "elsa.json")], []);
        var root = new ExecutableNode(
            "local-root", "local-root", "test.boundary", "1",
            new("test.boundary", "1", JsonSerializer.SerializeToElement(new { plan = 1 })),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, RuntimeOutputCapture>(), new Dictionary<string, string>(),
            [new ExecutableChildSlot("Graph.Entry", [new ExecutableNode(
                "local-child", "local-child", "test.child", "1",
                new("test.child", "1", JsonSerializer.SerializeToElement(new { plan = 2 })),
                new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, RuntimeOutputCapture>(), new Dictionary<string, string>())])]);
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
                [new("caller-result", "CallerResult", new TypeReference("Int32"), null, null)])),
            new FakeActivityVersionStore([]),
            _activityStructureService,
            TestWellKnownTypeRegistry.Create(),
            new ReusablePublicationStore(publication),
            new ReusableTemplateReader(template),
            new ReusableSourceReader(sourceReference),
            sidecars);

        var executable = await compiler.CompileAsync(NewRequest(DateTimeOffset.UtcNow));

        Assert.Equal("activity.reusable", executable.RootActivity.ActivityType);
        Assert.Matches("^node-[0-9a-f]{64}$", executable.RootActivity.ExecutableNodeId);
        var binding = executable.RootActivity.InputBindings["Value"];
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
    public async Task Reusable_output_capture_preserves_a_custom_driver_and_non_serializable_value_through_runtime_completion()
    {
        var driver = new OpaqueOutputStorageDriver();
        var storageDrivers = new RuntimeDurableValueStorageDriverRegistry(
            [new JsonRuntimeDurableValueStorageDriver(), driver]);
        var contract = new ActivityContract(
            "1",
            [],
            [new("result", "Result", new TypeReference("Object"), true, driver.DriverKey)],
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
                new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, RuntimeOutputCapture>(), new Dictionary<string, string>())])]);
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
            new ReusablePublicationStore(publication),
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
        var opaque = new OpaqueOutput(() => { });

        var captured = await CompleteReusableBoundaryAsync(executable, storageDrivers, opaque);
        var projections = await RuntimeInputBindingStateProjection.ProjectAllAsync([captured], storageDrivers);

        Assert.Equal(driver.DriverKey, placed.Root.OutputCaptures["Result"].StorageDriverKey);
        Assert.Equal(DurableValueStorage.External, captured.Storage);
        Assert.Equal(driver.DriverKey, captured.Type.Id);
        Assert.Same(opaque, projections.WorkflowVariables["CallerResult"]);
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
        Assert.StartsWith("System.String", expression.ResultType?.Id, StringComparison.Ordinal);
        Assert.StartsWith("System.String", binding.Metadata["typeName"], StringComparison.Ordinal);
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
        Assert.Equal(RuntimeInputBindingSource.Expression, binding.Source);
        Assert.Null(binding.LiteralValue);
        var expression = binding.Expression;
        Assert.NotNull(expression);
        Assert.Equal("Variable", expression!.Language);

        var parsed = JsonSerializer.Deserialize<JsonElement>(expression.Expression);
        Assert.Equal("var-counter", parsed.GetProperty("referenceKey").GetString());
        Assert.Equal("node-sequence", parsed.GetProperty("declaringScopeId").GetString());
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
        Assert.Equal(RuntimeInputBindingSource.Expression, binding.Source);
        Assert.Equal("Variable", binding.Expression!.Language);
        var parsed = JsonSerializer.Deserialize<JsonElement>(binding.Expression.Expression);
        Assert.Equal("var-counter", parsed.GetProperty("referenceKey").GetString());
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
        Assert.StartsWith("System.String", binding.Metadata["typeName"], StringComparison.Ordinal);
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

        var materialized = await new RuntimeActivityInputMaterializer().MaterializeInputsAsync(executable.RootActivity);

        var textInput = Assert.Single(materialized);
        Assert.Equal("Text", textInput.Name);
        Assert.Equal("Hello World!", textInput.Value);
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

        var materialized = await new RuntimeActivityInputMaterializer().MaterializeInputsAsync(executable.RootActivity);

        var linesInput = Assert.Single(materialized);
        var lines = Assert.IsAssignableFrom<ICollection<string>>(linesInput.Value);
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
        var executableStructure = structure!.Payload.Deserialize<SequenceExecutableStructure>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(executableStructure);
        var materializedVariable = Assert.Single(executableStructure!.Variables);
        Assert.Equal("var-counter", materializedVariable.ReferenceKey);
        Assert.Equal("Counter", materializedVariable.Name);
    }

    [Fact]
    public async Task IndexesResumeTargetHandlerIntoExecutable()
    {
        var now = new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);
        var compiler = ResumeCompiler(WorkflowVersion(ResumeNode("delay-1")), _resumeProbeActivity);

        var executable = await compiler.CompileAsync(NewRequest(now));

        var resumeTarget = Assert.Contains(
            "resume-target:probe",
            (IReadOnlyDictionary<string, WorkflowExecutableResumeTarget>)executable.ResumeTargets);
        Assert.Equal("delay-1", resumeTarget.ExecutableNodeId);
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
    public async Task DuplicateResumeTargetIdAcrossNodesFailsCompilation()
    {
        var now = new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);
        var compiler = ResumeCompiler(
            WorkflowVersion(SequenceNode("seq", [ResumeNode("delay-1"), ResumeNode("delay-2")])),
            _resumeProbeActivity,
            _sequenceActivity);

        await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() => compiler.CompileAsync(NewRequest(now)).AsTask());
    }

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
        await activityStore.SaveAsync(RuntimeState(boundaryExecutionId, boundary, ActivityExecutionStatus.Running));
        await activityStore.SaveAsync(RuntimeState(childExecutionId, child, ActivityExecutionStatus.Completed, boundaryExecutionId));

        var services = new ServiceCollection();
        services.AddScoped<IActivityFactory>(_ => new FixedActivityFactory(new OutputtingCompositeActivity(outputValue)));
        services.AddSingleton<IExpressionEvaluator, ConstantExpressionEvaluator>();
        services.AddSingleton<IWorkflowExecutableStore>(executableStore);
        services.AddSingleton<IActivityExecutionStateStore>(activityStore);
        services.AddSingleton<IWorkflowSchedulerWorkQueue>(schedulerQueue);
        services.AddSingleton<IRuntimeActivityOutputRegister, InMemoryRuntimeActivityOutputRegister>();
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
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ActivityFaultIncidentRecorder>();
        await using var provider = services.BuildServiceProvider();
        var handler = new WorkflowParentActivityCompletionSchedulerWorkHandler(
            new RuntimeActivityInputMaterializer(),
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
        var stored = await durableStore.ListAsync(workflowExecutionId);
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

    private sealed class ConstantExpressionEvaluator : IExpressionEvaluator
    {
        public ValueTask<T?> EvaluateAsync<T>(
            IExpression expression,
            IExpressionExecutionContext context,
            IExpressionEvaluatorOptions? options = default) =>
            ValueTask.FromResult((T?)(object)42);

        public ValueTask<object?> EvaluateAsync(
            IExpression expression,
            Type returnType,
            IExpressionExecutionContext context,
            IExpressionEvaluatorOptions? options = default) =>
            ValueTask.FromResult<object?>(42);
    }

    private sealed class FixedActivityFactory(IActivity activity) : IActivityFactory
    {
        public ValueTask<IActivity> Create(
            RuntimeActivityDescriptor descriptor,
            IReadOnlyDictionary<string, InputArgument>? inputs,
            IReadOnlyDictionary<string, OutputArgument>? outputs,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(activity);
    }

    private sealed class OutputtingCompositeActivity(object? outputValue) : IActivity, IActivityChildCompletionHandler, IRuntimeActivityCheckpointParticipant
    {
        public string Id { get; set; } = string.Empty;
        public string NodeId { get; set; } = string.Empty;
        public string? Name { get; set; }
        public string Type { get; set; } = "test.boundary";
        public string Version { get; set; } = "1";
        public Dictionary<string, object> CustomProperties { get; set; } = new();
        public Dictionary<string, object> SyntheticProperties { get; set; } = new();
        public Dictionary<string, object> Metadata { get; set; } = new();
        public ValueTask<bool> CanExecuteAsync(IActivityExecutionContext context) => ValueTask.FromResult(true);
        public ValueTask ExecuteAsync(IActivityExecutionContext context) => ValueTask.CompletedTask;
        public ValueTask OnChildCompletedAsync(ActivityChildCompletedContext context) => ValueTask.CompletedTask;
        public ValueTask<IReadOnlyCollection<RuntimeStateChange<DurableValueState>>> PrepareEntryCheckpointAsync(
            IRuntimeActivityExecutionContext context,
            IReadOnlyDictionary<string, object?> effectiveInputs,
            DateTimeOffset capturedAt,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyCollection<RuntimeStateChange<DurableValueState>>>([]);

        public ValueTask<IReadOnlyCollection<RuntimeStateChange<DurableValueState>>> PrepareCompletionCheckpointAsync(
            IRuntimeActivityExecutionContext context,
            IReadOnlyCollection<DurableValueState> persistedValues,
            DateTimeOffset capturedAt,
            CancellationToken cancellationToken = default)
        {
            context.RecordActivityOutput("Result", outputValue);
            context.CompleteCompositeActivity([ActivityOutcomes.Done]);
            return ValueTask.FromResult<IReadOnlyCollection<RuntimeStateChange<DurableValueState>>>([]);
        }
    }

    private sealed class OpaqueOutput(Action callback)
    {
        public Action Callback { get; } = callback;
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

    // A minimal type carrying a [ResumeTarget] handler. The compiler reflects the attribute off the node's
    // resolved CLR type, so the type need not be a full activity for this indexing test.
    private sealed class ResumeProbeActivity
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

    private static WorkflowDefinitionVersion WorkflowVersion(
        ActivityNode? rootActivity,
        IReadOnlyCollection<Elsa.Expressions.Core.Models.VariableDefinition>? variables = null) =>
        new("definition-1", "1.0.0")
        {
            Id = "version-1",
            Definition = new WorkflowDefinition { Id = "definition-1", Name = "Demo" },
            State = new WorkflowDefinitionState(variables ?? [], rootActivity, [], [], null)
        };

    private static ActivityNode Node(string nodeId, params WorkflowArgumentState[] inputs) =>
        new(nodeId, "activity-write-line", inputs, Outputs: []);

    private static ActivityNode WriteLinesNode(string nodeId, params WorkflowArgumentState[] inputs) =>
        new(nodeId, "activity-write-lines", inputs, Outputs: []);

    private static ActivityNode SequenceNode(
        string nodeId,
        IReadOnlyCollection<ActivityNode> activities,
        IReadOnlyCollection<Elsa.Expressions.Core.Models.VariableDefinition>? variables = null) =>
        new(
            nodeId,
            "activity-sequence",
            Inputs: [],
            Outputs: [],
            Structure: new ActivityNodeStructure(
                SequenceActivity.StructureKind,
                SequenceActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new SequenceAuthoredStructure(activities, variables))));

    private static WorkflowArgumentState Text(string value) =>
        new("Text", new ArgumentValue(value, "Literal"), null, null, null, null);

    private static WorkflowArgumentState JavaScriptText(string expression) =>
        new("Text", new ArgumentValue(expression, "JavaScript"), null, null, null, null);

    private static WorkflowArgumentState VariableText(JsonElement reference) =>
        new("Text", new ArgumentValue(reference, "Variable"), null, null, null, null);

    private static WorkflowArgumentState ObjectLines(IReadOnlyCollection<string> lines) =>
        new("Lines", new ArgumentValue(JsonSerializer.SerializeToElement(lines), "Object"), null, null, null, null);

    private static ActivityDefinitionVersion ActivityVersion(string id, string inputName, TypeReference inputType) =>
        ActivityVersion(id, "Test.WriteLine", [new InputDefinition(inputName, inputName, inputType, null, inputName, null)]);

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
            DescriptorPayload = JsonSerializer.SerializeToElement(new ClrActivityDescriptor("Object")),
            Inputs = inputs ?? []
        };

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
    private sealed class LegacyTriggerActivity
    {
        public const string ActivityType = "Elsa.Test.LegacyTrigger";
    }

    private static IActivityStructureService ActivityStructureService()
    {
        var services = new ServiceCollection();
        services.AddScoped<IActivityStructureService, DefaultActivityStructureService>();
        new ActivitiesSequenceFeature().ConfigureServices(services);

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

    private sealed class ReusablePublicationStore(ActivityDefinitionVersionPublication publication) : IActivityDefinitionVersionPublicationStore
    {
        public Task<ActivityDefinitionVersionPublication?> FindAsync(string definitionVersionId, CancellationToken cancellationToken = default) => Task.FromResult(definitionVersionId == publication.DefinitionVersionId ? publication : null);
        public Task<IReadOnlyList<ActivityDefinitionVersionPublication>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ActivityDefinitionVersionPublication>>(definitionId == publication.DefinitionId ? [publication] : []);
    }

    private sealed class ReusableTemplateReader(ExecutableActivityTemplate template) : IExecutableActivityTemplateReader
    {
        public ValueTask<ExecutableActivityTemplate?> FindAsync(string templateId, CancellationToken cancellationToken = default) => ValueTask.FromResult(templateId == template.TemplateId ? template : null);
        public ValueTask<ExecutableActivityTemplate?> FindByHashAsync(string templateHash, CancellationToken cancellationToken = default) => ValueTask.FromResult(templateHash == template.TemplateHash ? template : null);
    }

    private sealed class ReusableSourceReader(WorkflowExecutableSourceReference reference) : IWorkflowExecutableSourceReferenceReader
    {
        public ValueTask<WorkflowExecutableSourceReference?> FindAsync(string sourceReferenceId, CancellationToken cancellationToken = default) => ValueTask.FromResult(sourceReferenceId == reference.SourceReferenceId ? reference : null);
        public ValueTask<IReadOnlyCollection<WorkflowExecutableSourceReference>> ListByArtifactAsync(string artifactId, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyCollection<WorkflowExecutableSourceReference>>(artifactId == reference.ArtifactId ? [reference] : []);
        public ValueTask<IReadOnlyCollection<WorkflowExecutableSourceReference>> ListAsync(WorkflowExecutableReferenceScope? scope = null, bool liveOnly = false, DateTimeOffset? now = null, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyCollection<WorkflowExecutableSourceReference>>([reference]);
        public ValueTask<IReadOnlyCollection<string>> ListUnreferencedArtifactIdsAsync(IEnumerable<string> artifactIds, DateTimeOffset now, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyCollection<string>>([]);
    }
}
