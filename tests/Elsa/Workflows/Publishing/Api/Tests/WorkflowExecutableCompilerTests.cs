using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Scheduling.Activities;
using Elsa.Activities.Sequence;
using Elsa.Activities.Sequence.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Persistence.Core;
using Elsa.Primitives.Entities;
using Elsa.Primitives.Models;
using Elsa.Primitives.Persistence;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Resolvers;
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
        Assert.Equal("alias", expression.ResultType?.Kind);
        Assert.Equal("String", expression.ResultType?.Id);
        Assert.DoesNotContain("typeName", binding.Metadata);
        Assert.Equal("Text", binding.Metadata["referenceKey"]);
    }

    [Fact]
    public async Task CompilesVariableReferenceInputIntoCanonicalVariableReadBinding()
    {
        // A structured Variable reference compiles directly to the closed variable-read role. Runtime
        // no longer needs to route a normal variable read through an expression-language handler.
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
        Assert.Null(binding.Expression);
        Assert.Equal("var-counter", binding.Variable!.VariableKey);
        Assert.Equal("node-sequence", binding.Variable.DeclaringScopeId);
    }

    [Fact]
    public async Task CompilesBareVariableReferenceKeyInputIntoWorkflowScopedVariableReadBinding()
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
        Assert.Equal("var-counter", binding.Variable!.VariableKey);
        Assert.Equal(VariableReference.WorkflowScopeId, binding.Variable.DeclaringScopeId);
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
        Assert.Equal("Text", binding.InputKey);
        Assert.Equal("Hello World!", binding.LiteralValue?.GetString());
        Assert.Equal("String", binding.TargetType.Alias);
        Assert.Equal(ValueProtectionPolicy.InstanceInline, binding.EffectivePolicy);
        Assert.Equal("Text", binding.Metadata["referenceKey"]);
        Assert.DoesNotContain("typeName", binding.Metadata);
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

        var materialized = await new RuntimeActivityInputMaterializer(
            new RuntimeInputBindingResolver(),
            TestWellKnownTypeRegistry.Create()).MaterializeInputsAsync(executable.RootActivity);

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

        var materialized = await new RuntimeActivityInputMaterializer(
            new RuntimeInputBindingResolver(),
            TestWellKnownTypeRegistry.Create()).MaterializeInputsAsync(executable.RootActivity);

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
                State: new WorkflowDefinitionState([], Node("write-one", Text("hello")), [], [], null, null),
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
    public async Task Compiles_typed_intrinsics_without_activity_catalog_rows()
    {
        var variable = new VariableReference("items", VariableReference.WorkflowScopeId);
        var stringList = new TypeReference("String", CollectionKind.List);
        var nodes = new[]
        {
            IntrinsicNode("set", AuthoredWorkflowIntrinsicKind.Set, stringList, variable, JsonSerializer.SerializeToElement(new[] { "set" })),
            IntrinsicNode("merge", AuthoredWorkflowIntrinsicKind.Merge, stringList, variable, JsonSerializer.SerializeToElement(new[] { "merge" })),
            IntrinsicNode("reduce", AuthoredWorkflowIntrinsicKind.Reduce, stringList, variable, JsonSerializer.SerializeToElement(new[] { "reduce" })),
            IntrinsicNode("return", AuthoredWorkflowIntrinsicKind.Return, new TypeReference("String"), null, "done"),
            IntrinsicNode("control", AuthoredWorkflowIntrinsicKind.Control, null, null, "Approved"),
            IntrinsicNode("correlate", AuthoredWorkflowIntrinsicKind.SetCorrelationId, new TypeReference("String"), null, "order-42"),
            IntrinsicNode("set-name", AuthoredWorkflowIntrinsicKind.SetInstanceName, new TypeReference("String"), null, "Order 42"),
            IntrinsicNode("set-output", AuthoredWorkflowIntrinsicKind.SetOutput, new TypeReference("String"), null, "accepted", "result"),
            IntrinsicNode("finish", AuthoredWorkflowIntrinsicKind.Finish, null, null, "Aborted")
        };
        var compiler = Compiler(WorkflowVersion(SequenceNode("sequence", nodes)));

        var executable = await compiler.CompileAsync(NewRequest(DateTimeOffset.UtcNow));

        var compiled = executable.NodesById;
        Assert.Equal(WorkflowIntrinsicKind.Set, compiled["set"].IntrinsicKind);
        Assert.Equal(WorkflowIntrinsicKind.Merge, compiled["merge"].IntrinsicKind);
        Assert.Equal(WorkflowIntrinsicKind.Reduce, compiled["reduce"].IntrinsicKind);
        Assert.Equal(WorkflowIntrinsicKind.Return, compiled["return"].IntrinsicKind);
        Assert.Equal(WorkflowIntrinsicKind.Control, compiled["control"].IntrinsicKind);
        Assert.Equal(WorkflowIntrinsicKind.SetCorrelationId, compiled["correlate"].IntrinsicKind);
        Assert.Equal(WorkflowIntrinsicKind.SetInstanceName, compiled["set-name"].IntrinsicKind);
        Assert.Equal(WorkflowIntrinsicKind.SetOutput, compiled["set-output"].IntrinsicKind);
        Assert.Equal(WorkflowIntrinsicKind.Finish, compiled["finish"].IntrinsicKind);
        Assert.All(nodes, authored => Assert.Null(compiled[authored.NodeId].ActivityContract));
        Assert.Equal(CollectionKind.List, compiled["merge"].InputBindings[WorkflowIntrinsicInputKeys.Value].TargetType.CollectionKind);
        Assert.Equal("items", compiled["reduce"].IntrinsicVariable!.VariableKey);
        Assert.Equal("Approved", compiled["control"].InputBindings[WorkflowIntrinsicInputKeys.Outcome].LiteralValue!.Value.GetString());
        Assert.Equal("result", compiled["set-output"].InputBindings[WorkflowIntrinsicInputKeys.Name].LiteralValue!.Value.GetString());
        Assert.Equal("accepted", compiled["set-output"].InputBindings[WorkflowIntrinsicInputKeys.Value].LiteralValue!.Value.GetString());
        Assert.Equal("Aborted", compiled["finish"].InputBindings[WorkflowIntrinsicInputKeys.Outcome].LiteralValue!.Value.GetString());
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

    private static WorkflowDefinitionVersion WorkflowVersion(ActivityNode? rootActivity) =>
        new("definition-1", "1.0.0")
        {
            Id = "version-1",
            Definition = new WorkflowDefinition { Id = "definition-1", Name = "Demo" },
            State = new WorkflowDefinitionState([], rootActivity, [], [], null, null)
        };

    private static ActivityNode Node(string nodeId, params WorkflowArgumentState[] inputs) =>
        new(nodeId, "activity-write-line", inputs, Outputs: []);

    private static ActivityNode WriteLinesNode(string nodeId, params WorkflowArgumentState[] inputs) =>
        new(nodeId, "activity-write-lines", inputs, Outputs: []);

    private static ActivityNode IntrinsicNode(
        string nodeId,
        AuthoredWorkflowIntrinsicKind kind,
        TypeReference? valueType,
        VariableReference? variable,
        object value,
        string? targetName = null)
    {
        var inputKey = kind is AuthoredWorkflowIntrinsicKind.Control or AuthoredWorkflowIntrinsicKind.Finish
            ? WorkflowIntrinsicInputKeys.Outcome
            : WorkflowIntrinsicInputKeys.Value;
        var inputs = new List<WorkflowArgumentState>();
        if (kind == AuthoredWorkflowIntrinsicKind.SetOutput)
            inputs.Add(new WorkflowArgumentState(WorkflowIntrinsicInputKeys.Name, new ArgumentValue(targetName, "Literal"), null, null, null, null));
        inputs.Add(new WorkflowArgumentState(inputKey, new ArgumentValue(value, "Literal"), null, null, null, null));
        return new ActivityNode(
            nodeId,
            $"elsa.intrinsic.{kind.ToString().ToLowerInvariant()}@1",
            inputs,
            Outputs: [])
        {
            Intrinsic = new AuthoredWorkflowIntrinsic(kind, valueType, variable)
        };
    }

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
            DescriptorType = typeof(ClrActivityDescriptor).FullName!,
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
            DescriptorType = typeof(ClrActivityDescriptor).FullName!,
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
}
