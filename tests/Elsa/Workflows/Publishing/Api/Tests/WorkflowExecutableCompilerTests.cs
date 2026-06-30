using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Sequence;
using Elsa.Activities.Sequence.Models;
using Elsa.Persistence.Core;
using Elsa.Primitives.Entities;
using Elsa.Primitives.Models;
using Elsa.Primitives.Persistence;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ArgumentValue = Elsa.Expressions.Core.Models.ArgumentValue;
using WorkflowArgumentState = Elsa.Workflows.Design.Core.Models.ArgumentState;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed class WorkflowExecutableCompilerTests
{
    private readonly ActivityDefinitionVersion _writeLineActivity = ActivityVersion("activity-write-line", "Text", new TypeReference("String"));
    private readonly ActivityDefinitionVersion _sequenceActivity = ActivityVersion("activity-sequence", typeof(SequenceActivity).FullName!);
    private readonly IActivityStructureService _activityStructureService = ActivityStructureService();

    [Fact]
    public async Task CompilesPublishedWorkflowExecutableWithPublishedScope()
    {
        var now = new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);
        var compiler = Compiler(WorkflowVersion(Node("write-one", Text("hello"))));

        var executable = await compiler.CompileAsync(new WorkflowExecutableCompileRequest(
            VersionId: "version-1",
            Scope: WorkflowExecutableScope.Published,
            CreatedAt: now,
            PublishedAt: now,
            ExpiresAt: null,
            ArtifactIdPrefix: "artifact-"));

        Assert.StartsWith("artifact-", executable.Identity.ArtifactId, StringComparison.Ordinal);
        Assert.Equal(WorkflowExecutableScope.Published, executable.Scope);
        Assert.Null(executable.ExpiresAt);
        Assert.Equal("write-one", executable.RootActivity.ExecutableNodeId);
    }

    [Fact]
    public async Task CompilesJavaScriptBoundInputIntoRuntimeExpressionBinding()
    {
        var now = new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);
        var compiler = Compiler(WorkflowVersion(Node("write-js", JavaScriptText("\"Hello \" + \"World\""))));

        var executable = await compiler.CompileAsync(new WorkflowExecutableCompileRequest(
            VersionId: "version-1",
            Scope: WorkflowExecutableScope.Published,
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
            Scope: WorkflowExecutableScope.Published,
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
            Scope: WorkflowExecutableScope.Published,
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
            Scope: WorkflowExecutableScope.Published,
            CreatedAt: now,
            PublishedAt: now,
            ExpiresAt: null,
            ArtifactIdPrefix: "artifact-")).AsTask());
    }

    [Fact]
    public async Task CompilingExpressionInputWithoutExpressionTextThrows()
    {
        var now = new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);
        var compiler = Compiler(WorkflowVersion(Node("write-js", JavaScriptText(""))));

        await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() => compiler.CompileAsync(new WorkflowExecutableCompileRequest(
            VersionId: "version-1",
            Scope: WorkflowExecutableScope.Published,
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
            Scope: WorkflowExecutableScope.Published,
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
            Scope: WorkflowExecutableScope.Published,
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
            Scope: WorkflowExecutableScope.TransientTestRun,
            CreatedAt: now,
            PublishedAt: null,
            ExpiresAt: expiresAt,
            ArtifactIdPrefix: "test-artifact-",
            CompatibilityMetadata: new Dictionary<string, string> { ["runtime.testRunId"] = "testrun-1" }));

        Assert.StartsWith("test-artifact-", executable.Identity.ArtifactId, StringComparison.Ordinal);
        Assert.Equal(WorkflowExecutableScope.TransientTestRun, executable.Scope);
        Assert.Equal(expiresAt, executable.ExpiresAt);
        Assert.Null(executable.PublishedAt);
        Assert.Equal("testrun-1", executable.CompatibilityMetadata["runtime.testRunId"]);
        Assert.Equal(3, executable.Nodes.Count);
    }

    [Fact]
    public async Task CompilesDraftSnapshotWithoutReadingDurableWorkflowVersion()
    {
        var now = new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);
        var compiler = new WorkflowExecutableCompiler(
            new ThrowingVersionStore(),
            new FakeActivityVersionStore([_writeLineActivity]),
            _activityStructureService,
            TestWellKnownTypeRegistry.Create());

        var executable = await compiler.CompileAsync(new WorkflowExecutableCompileRequest(
            VersionId: "draft:snapshot-1",
            Scope: WorkflowExecutableScope.TransientTestRun,
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
                SourceReference: new WorkflowExecutableSourceReference("WorkflowDraftSnapshot", "snapshot-1", "draft"))
        });

        Assert.StartsWith("test-artifact-", executable.Identity.ArtifactId, StringComparison.Ordinal);
        Assert.Equal("definition-1", executable.Identity.DefinitionId);
        Assert.Equal("draft:snapshot-1", executable.Identity.DefinitionVersionId);
        Assert.Equal("WorkflowDraftSnapshot", executable.Identity.Source?.SourceKind);
        Assert.Equal("snapshot-1", executable.Identity.Source?.SourceId);
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
            Scope: WorkflowExecutableScope.Published,
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

    private WorkflowExecutableCompiler Compiler(WorkflowDefinitionVersion workflowVersion) =>
        new(
            new FakeVersionStore(workflowVersion),
            new FakeActivityVersionStore([_writeLineActivity, _sequenceActivity]),
            _activityStructureService,
            TestWellKnownTypeRegistry.Create());

    private static WorkflowDefinitionVersion WorkflowVersion(ActivityNode? rootActivity) =>
        new("definition-1", "1.0.0")
        {
            Id = "version-1",
            Definition = new WorkflowDefinition { Id = "definition-1", Name = "Demo" },
            State = new WorkflowDefinitionState([], rootActivity, [], [], null, null)
        };

    private static ActivityNode Node(string nodeId, params WorkflowArgumentState[] inputs) =>
        new(nodeId, "activity-write-line", inputs, Outputs: []);

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
