using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Runtime.Core.Attributes;
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
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ArgumentValue = Elsa.Expressions.Core.Models.ArgumentValue;
using WorkflowArgumentState = Elsa.Workflows.Design.Core.Models.ArgumentState;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// Same-input/same-output characterization harness for <see cref="WorkflowExecutableCompiler"/> (W30b, #418).
/// Compiles a corpus spanning the input-type x structure x resume-target surface and pins, per definition,
/// BOTH the full canonically-serialized <see cref="WorkflowExecutable"/> AND its content-addressable
/// <c>ArtifactHash</c>. The decomposition of the compiler is pure code motion: every golden here must stay
/// byte-identical across the refactor. The hash is the sharpest guard (any drift in the canonical hashing
/// payload changes it), so it is asserted explicitly in addition to being embedded in the serialized golden.
/// </summary>
public sealed class WorkflowExecutableCompilerGoldenTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);
    private readonly IActivityStructureService _activityStructureService = ActivityStructureService();

    public static TheoryData<string> CorpusNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in Corpus.Keys)
            data.Add(name);
        return data;
    }

    [Theory]
    [MemberData(nameof(CorpusNames))]
    public async Task CompiledExecutableMatchesGolden(string corpusName)
    {
        var definition = Corpus[corpusName];
        var compiler = definition.BuildCompiler(_activityStructureService);

        var executable = await compiler.CompileAsync(definition.Request);

        var actualJson = CanonicalJson(executable);
        var goldenPath = GoldenPath(corpusName);

        if (Environment.GetEnvironmentVariable("PUBLISHING_COMPILER_GOLDEN_REGEN") == "1")
        {
            File.WriteAllText(goldenPath, actualJson);
            return;
        }

        if (!File.Exists(goldenPath))
        {
            // Capture mode: write the golden into the source tree so it lands in the commit for review.
            Directory.CreateDirectory(Path.GetDirectoryName(goldenPath)!);
            File.WriteAllText(goldenPath, actualJson);
            Assert.Fail($"Golden '{corpusName}' did not exist; captured it at '{goldenPath}'. Re-run to verify.");
        }

        var expectedJson = File.ReadAllText(goldenPath);
        Assert.Equal(Normalize(expectedJson), Normalize(actualJson));

        // Explicit, independent hash pin: the sharpest byte-identity guard.
        var expectedHash = JsonNode.Parse(expectedJson)!["identity"]!["artifactHash"]!.GetValue<string>();
        Assert.Equal(expectedHash, executable.Identity.ArtifactHash);
    }

    [Fact]
    public async Task CompileProjectsEachActivityNodesChildrenExactlyOnce()
    {
        // W30b (#418) evidence for the traverse-once fix and the single INTENTIONAL observable change of the
        // refactor: the compiler used to call IActivityStructureService.ProjectChildren twice per node (once in
        // FlattenActivities, once in CompileNode). After the ActivityTreeProjector extraction it must call it
        // exactly once per node. Output identity across this change is proven by the golden corpus above; this
        // fact proves the call-count reduction actually landed.
        var counting = new CountingActivityStructureService(_activityStructureService);
        var definition = Corpus["nested-child-slots"]; // sequence root + two leaf nodes = 3 nodes.
        var compiler = definition.BuildCompiler(counting);

        await compiler.CompileAsync(definition.Request);

        Assert.Equal(3, counting.ProjectChildrenCallsByNode.Count);
        Assert.All(counting.ProjectChildrenCallsByNode, entry =>
            Assert.True(entry.Value == 1, $"Node '{entry.Key}' had ProjectChildren called {entry.Value} times; expected exactly 1."));
    }

    [Fact]
    public async Task ArtifactHashIsBehavioralOnly_SameTreeUnderDifferentSourcesYieldsSameHash()
    {
        // ADR 0038: the ArtifactHash covers Execution Material only. The same authored tree published under
        // two different source identities (definition version / artifact version / source reference version)
        // must resolve to the SAME hash — that is what makes executables content-addressed.
        var definition = Corpus["literal-input"];
        var compiler = definition.BuildCompiler(_activityStructureService);

        var firstSource = definition.SourceWith(definitionVersionId: "version-A", artifactVersion: "1.0.0", sourceVersion: "1.0.0");
        var secondSource = definition.SourceWith(definitionVersionId: "version-B", artifactVersion: "7.4.2", sourceVersion: "7.4.2");

        var first = await compiler.CompileAsync(definition.RequestWith(firstSource));
        var second = await compiler.CompileAsync(definition.RequestWith(secondSource));

        Assert.Equal(first.Identity.ArtifactHash, second.Identity.ArtifactHash);
        Assert.Equal(first.Identity.ArtifactId, second.Identity.ArtifactId);
        // Source identity still travels on the artifact even though it no longer feeds the hash.
        Assert.NotEqual(first.Identity.DefinitionVersionId, second.Identity.DefinitionVersionId);
        Assert.NotEqual(first.Identity.ArtifactVersion, second.Identity.ArtifactVersion);
    }

    [Fact]
    public async Task ArtifactHashIsBehavioralOnly_ChangingAnInputBindingLiteralYieldsDifferentHash()
    {
        // Behavioral counter-check: a change to Execution Material (here an input-binding literal) must move
        // the hash, even when the source identity is held constant.
        var source = Corpus["literal-input"];
        var changed = Standard(WorkflowVersion(Node("write-one", Text("Goodbye World!"))), source.Activities);

        var sourceCompiler = source.BuildCompiler(_activityStructureService);
        var changedCompiler = changed.BuildCompiler(_activityStructureService);

        var sourceExecutable = await sourceCompiler.CompileAsync(source.Request);
        var changedExecutable = await changedCompiler.CompileAsync(changed.Request);

        Assert.NotEqual(sourceExecutable.Identity.ArtifactHash, changedExecutable.Identity.ArtifactHash);
        Assert.NotEqual(sourceExecutable.Identity.ArtifactId, changedExecutable.Identity.ArtifactId);
    }

    private static string Normalize(string json) => json.Replace("\r\n", "\n").TrimEnd('\n');

    // Resolves the source-tree Fixtures/CompilerGoldens directory (not the bin output copy), so a first-run
    // capture writes reviewable goldens into the commit. Walks up from the build output to the project root.
    private static string GoldenPath(string corpusName)
    {
        var projectRoot = ProjectRoot();
        return Path.Combine(projectRoot, "Fixtures", "CompilerGoldens", $"{corpusName}.json");
    }

    private static string ProjectRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Elsa.Workflows.Publishing.Api.Tests.csproj")))
            dir = dir.Parent;

        return dir?.FullName
            ?? throw new InvalidOperationException("Could not locate the test project root from the build output directory.");
    }

    /// <summary>
    /// Serializes a <see cref="WorkflowExecutable"/> to a deterministic, key-sorted, indented JSON string so
    /// two structurally-identical artifacts always render byte-identically regardless of dictionary iteration
    /// order or in-memory representation.
    /// </summary>
    private static string CanonicalJson(WorkflowExecutable executable)
    {
        var model = new
        {
            // ADR 0038: the artifact is pure behavior. Source identity (definition/version, artifact-version label)
            // is still carried on Identity as the runtime pinned-snapshot carrier, but scope/publishedAt/expiresAt
            // and the embedded source-reference object have left the artifact document — they are reference facts.
            identity = new
            {
                artifactId = executable.Identity.ArtifactId,
                definitionId = executable.Identity.DefinitionId,
                definitionVersionId = executable.Identity.DefinitionVersionId,
                artifactVersion = executable.Identity.ArtifactVersion,
                artifactHash = executable.Identity.ArtifactHash
            },
            createdAt = executable.CreatedAt,
            compatibilityMetadata = Ordered(executable.CompatibilityMetadata),
            resumeTargets = executable.ResumeTargets
                .OrderBy(t => t.Key, StringComparer.Ordinal)
                .Select(t => new
                {
                    key = t.Key,
                    resumeTargetId = t.Value.ResumeTargetId,
                    executableNodeId = t.Value.ExecutableNodeId,
                    handlerKey = t.Value.HandlerKey,
                    metadata = Ordered(t.Value.Metadata)
                }),
            rootActivity = Node(executable.RootActivity)
        };

        var element = JsonSerializer.SerializeToElement(model);
        return SortJson(element);
    }

    private static object Node(ExecutableNode node) => new
    {
        executableNodeId = node.ExecutableNodeId,
        authoredActivityId = node.AuthoredActivityId,
        activityType = node.ActivityType,
        activityTypeVersion = node.ActivityTypeVersion,
        descriptorType = node.DescriptorType,
        descriptorPayload = node.DescriptorPayload,
        metadata = Ordered(node.Metadata),
        structure = node.Structure is null
            ? null
            : new
            {
                kind = node.Structure.Kind,
                schemaVersion = node.Structure.SchemaVersion,
                payload = node.Structure.Payload
            },
        inputBindings = node.InputBindings
            .OrderBy(b => b.Key, StringComparer.Ordinal)
            .Select(b => new
            {
                key = b.Key,
                inputName = b.Value.InputName,
                source = b.Value.Source.ToString(),
                literalValue = b.Value.LiteralValue,
                expression = b.Value.Expression is null
                    ? null
                    : new
                    {
                        language = b.Value.Expression.Language,
                        expression = b.Value.Expression.Expression,
                        resultTypeId = b.Value.Expression.ResultType?.Id
                    },
                metadata = Ordered(b.Value.Metadata)
            }),
        childSlots = node.ChildSlots
            .OrderBy(s => s.Name, StringComparer.Ordinal)
            .Select(s => new
            {
                name = s.Name,
                activities = s.Activities.Select(Node)
            })
    };

    private static IEnumerable<KeyValuePair<string, string>> Ordered(IReadOnlyDictionary<string, string> map) =>
        map.OrderBy(item => item.Key, StringComparer.Ordinal);

    /// <summary>Recursively rewrites a JSON element with object keys sorted, then pretty-prints it.</summary>
    private static string SortJson(JsonElement element)
    {
        var sorted = Sort(element) ?? throw new InvalidOperationException("A workflow executable serialized to a null JSON node.");
        return sorted.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonNode? Sort(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var obj = new JsonObject();
                foreach (var property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                    obj[property.Name] = Sort(property.Value);
                return obj;
            case JsonValueKind.Array:
                var array = new JsonArray();
                foreach (var item in element.EnumerateArray())
                    array.Add(Sort(item));
                return array;
            default:
                return JsonNode.Parse(element.GetRawText());
        }
    }

    // ---- Corpus -----------------------------------------------------------------------------------------

    private static readonly IReadOnlyDictionary<string, CorpusEntry> Corpus = BuildCorpus();

    private static IReadOnlyDictionary<string, CorpusEntry> BuildCorpus()
    {
        var writeLine = ActivityVersion("activity-write-line", "Text", new TypeReference("String"));
        var sequence = ActivityVersion("activity-sequence", typeof(SequenceActivity).FullName!);
        var probe = ActivityVersion("activity-probe", typeof(ResumeProbeActivity).FullName!);
        var standardActivities = new[] { writeLine, sequence };

        var counter = new Elsa.Expressions.Core.Models.VariableDefinition(
            ReferenceKey: "var-counter",
            Name: "Counter",
            Type: new TypeReference("String"),
            StorageDriverType: null,
            Default: new ArgumentValue("0", "Literal"));

        var variableReference = JsonSerializer.SerializeToElement(new { referenceKey = "var-counter", declaringScopeId = "node-sequence" });

        return new Dictionary<string, CorpusEntry>(StringComparer.Ordinal)
        {
            ["literal-input"] = Standard(WorkflowVersion(Node("write-one", Text("Hello World!"))), standardActivities),
            ["javascript-expression"] = Standard(WorkflowVersion(Node("write-js", JavaScriptText("\"Hello \" + \"World\""))), standardActivities),
            ["structured-variable-ref"] = Standard(WorkflowVersion(Node("write-var", VariableText(variableReference))), standardActivities),
            ["bare-variable-ref"] = Standard(WorkflowVersion(Node("write-var", VariableText(JsonSerializer.SerializeToElement("var-counter")))), standardActivities),
            ["sequence-with-container-variables"] = Standard(
                WorkflowVersion(SequenceNode("sequence", [Node("write-one", Text("one"))], [counter])),
                standardActivities),
            ["nested-child-slots"] = Standard(
                WorkflowVersion(SequenceNode("sequence", [Node("write-one", Text("one")), Node("write-two", Text("two"))])),
                standardActivities),
            ["resume-target-node"] = new(
                WorkflowVersion(new ActivityNode("delay-1", "activity-probe", Inputs: [], Outputs: [])),
                [probe, sequence],
                RegisterResumeTypes: true)
        };
    }

    private static CorpusEntry Standard(WorkflowDefinitionVersion version, ActivityDefinitionVersion[] activities) =>
        new(version, activities, RegisterResumeTypes: false);

    private sealed record CorpusEntry(
        WorkflowDefinitionVersion Version,
        ActivityDefinitionVersion[] Activities,
        bool RegisterResumeTypes)
    {
        public WorkflowExecutableCompileRequest Request => new(
            VersionId: "version-1",
            Scope: WorkflowExecutableReferenceScope.Published,
            CreatedAt: Now,
            PublishedAt: Now,
            ExpiresAt: null,
            ArtifactIdPrefix: "artifact-");

        public WorkflowExecutableCompileRequest RequestWith(WorkflowExecutableCompileSource source) =>
            Request with { Source = source };

        public WorkflowExecutableCompileSource SourceWith(string definitionVersionId, string artifactVersion, string sourceVersion) =>
            new(
                DefinitionId: Version.DefinitionId,
                DefinitionVersionId: definitionVersionId,
                ArtifactVersion: artifactVersion,
                State: Version.State!,
                SourceKind: "WorkflowDefinitionVersion",
                SourceId: definitionVersionId,
                SourceVersion: sourceVersion);

        public WorkflowExecutableCompiler BuildCompiler(IActivityStructureService structureService)
        {
            var registry = TestWellKnownTypeRegistry.Create();
            if (RegisterResumeTypes)
            {
                registry.RegisterType(typeof(ResumeProbeActivity), typeof(ResumeProbeActivity).FullName!);
                registry.RegisterType(typeof(SequenceActivity), typeof(SequenceActivity).FullName!);
            }

            return TestCompiler.Create(
                new FakeVersionStore(Version),
                new FakeActivityVersionStore([.. Activities]),
                structureService,
                registry);
        }
    }

    private sealed class ResumeProbeActivity
    {
        [ResumeTarget("resume-target:probe")]
        public ValueTask OnResumeAsync() => ValueTask.CompletedTask;
    }

    // ---- Builders (mirror WorkflowExecutableCompilerTests) --------------------------------------------

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

    /// <summary>
    /// Decorates an <see cref="IActivityStructureService"/> and counts <c>ProjectChildren</c> calls per node id,
    /// so the traverse-once fix (single projection per node) can be asserted.
    /// </summary>
    private sealed class CountingActivityStructureService(IActivityStructureService inner) : IActivityStructureService
    {
        public Dictionary<string, int> ProjectChildrenCallsByNode { get; } = new(StringComparer.Ordinal);

        public IReadOnlyCollection<Elsa.Workflows.Design.Core.Models.ActivityChildProjection> ProjectChildren(ActivityNode activity)
        {
            ProjectChildrenCallsByNode[activity.NodeId] =
                ProjectChildrenCallsByNode.GetValueOrDefault(activity.NodeId) + 1;
            return inner.ProjectChildren(activity);
        }

        public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<Elsa.Workflows.Design.Core.Models.ActivityChildProjection> childProjections) =>
            inner.ReplaceChildren(activity, childProjections);

        public ActivityNodeStructure? CompileExecutableStructure(ActivityNode activity) =>
            inner.CompileExecutableStructure(activity);

        public IReadOnlyCollection<Elsa.Expressions.Core.Models.VariableDefinition> ProjectScopedVariables(ActivityNode activity) =>
            inner.ProjectScopedVariables(activity);

        public bool SupportsScopedVariables(ActivityNode activity) => inner.SupportsScopedVariables(activity);
    }
}
