using System.Text.Json;
using Elsa.Activities.Bpmn.Activities;
using Elsa.Activities.Bpmn.Design;
using Elsa.Activities.Bpmn.Models;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Testing;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Validations;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Values;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

public sealed class BpmnDataSelectionPublicationTests
{
    [Fact]
    public async Task Required_whole_result_in_linear_Bpmn_reaches_real_headless_publication_compilation()
    {
        using var fixture = new Fixture();
        var process = BpmnChildResultFactsTests.Process(children: [Capture(), Decision()]);
        WorkflowExecutable? executable = null;
        var error = await Record.ExceptionAsync(async () => { executable = await fixture.Compile(process); });
        // Without the BPMN owner facts this assertion fails with the same refusal as the real Decisions CI driver.
        Assert.Null(error);
        var children = Assert.Single(executable!.RootActivity.ChildSlots).Activities;
        var read = children.Single(child => child.AuthoredActivityId == "capture").InputBindings["value"].ActivityResult;
        Assert.NotNull(read);
        Assert.Equal("decision", read.ProducerExecutableNodeId);
        Assert.Equal("process", read.ProducerScopeId);
        Assert.Equal("$result", read.ProjectionKey);
        Assert.Empty(fixture.Catalog.Versions.Single(version => version.Id == "decision-version").Outputs);
    }

    [Theory]
    [InlineData("reverse")]
    [InlineData("fork")]
    [InlineData("condition")]
    [InlineData("loop")]
    [InlineData("nested-scope")]
    public async Task Mandatory_publication_admission_still_refuses_unproven_or_out_of_scope_results(string scenario)
    {
        using var fixture = new Fixture();
        var process = BpmnChildResultFactsTests.Process(scenario, [Capture(), Decision()]);
        if (scenario == "reverse")
        {
            using var json = JsonDocument.Parse(process.Structure!.Payload.GetRawText());
            var authored = json.RootElement.Deserialize<BpmnAuthoredStructure>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            process = process with { Structure = new(process.Structure.Kind, process.Structure.SchemaVersion, JsonSerializer.SerializeToElement(
                new BpmnAuthoredStructure(authored.Activities, authored.Elements,
                    [new("f1", "start", "capture-task"), new("f2", "capture-task", "task"), new("f3", "task", "end")]), Web)) };
        }
        if (scenario == "nested-scope")
        {
            var nested = BpmnChildResultFactsTests.Process(children: [Capture(), Decision()]) with { NodeId = "decision" };
            nested = nested with { Structure = new("elsa.bpmn.structure", "1.0.0", JsonSerializer.SerializeToElement(
                new BpmnAuthoredStructure([Decision("nested-producer")],
                    [new("start", "startEvent"), new("task", "task", childNodeId: "nested-producer"), new("end", "endEvent")],
                    [new("f1", "start", "task"), new("f2", "task", "end")]), Web)) };
            process = BpmnChildResultFactsTests.Process(children: [Capture("nested-producer"), nested]);
        }
        var error = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() => fixture.Compile(process).AsTask());
        var refusal = Assert.IsType<ActivityDataSelectionRejectedException>(error.InnerException);
        Assert.Contains(refusal.Findings, finding => finding.Path == "capture/inputs/value" &&
            finding.Type == (scenario == "nested-scope" ? "ValueFlow/ScopeBoundary" : "ValueFlow/UnavailableProducer"));
    }

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web)
    { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };

    private static ArgumentState Input(string key, object value, string role = "Literal") =>
        new(key, new ArgumentValue(JsonSerializer.SerializeToElement(value), role), null, null, null, null);
    private static ActivityNode Decision(string id = "decision") => new(id, "decision-version", [Input("Outcome", "Done")], []);
    private static ActivityNode Capture(string producer = "decision") => new("capture", "elsa.intrinsic.set-output@1",
        [Input("name", "decisionResult"), Input("value", new { producerNodeId = producer, producerScopeId = "process", projectionKey = "$result" }, "ActivityResult")], [])
    { Intrinsic = new(AuthoredWorkflowIntrinsicKind.SetOutput, new TypeReference("Elsa.Any")) };

    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly IServiceScope _scope;
        private readonly WorkflowExecutableCompiler _compiler;
        internal Catalog Catalog { get; }

        internal Fixture()
        {
            var serializer = SerializationServices.PayloadSerializer();
            ActivityDefinitionVersion Version(Type type, string id, params InputDefinition[] inputs) => new("1.0.0", "definition:" + id)
            {
                Id = id, Definition = new ActivityDefinition { Id = "definition:" + id, ActivityTypeKey = type.FullName!, Category = "BPMN" },
                ProviderKey = WellKnownRuntimeActivityConsumers.ClrActivity, ProviderSchemaVersion = RuntimeActivityDescriptor.InitialSchemaVersion,
                ConsumerKey = WellKnownRuntimeActivityConsumers.ClrActivity, ConsumerSchemaVersion = RuntimeActivityDescriptor.InitialSchemaVersion,
                DescriptorPayload = ClrConstruction.Payload(serializer, type), Inputs = inputs
            };
            Catalog = new([Version(typeof(BpmnProcess), "process-version"), Version(typeof(BpmnDecision), "decision-version",
                new InputDefinition("Outcome", "Outcome", new TypeReference("String"), null, "Outcome", null, IsNullable: true))]);
            var services = new ServiceCollection();
            services.AddOptions();
            new Elsa.Events.EventsFeature().ConfigureServices(services);
            new ActivitiesBpmnDesignFeature().ConfigureServices(services);
            new WorkflowDesignValidationsFeature().ConfigureServices(services);
            services.AddSingleton<IActivityDefinitionLookup>(Catalog);
            _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            _scope = _provider.CreateScope();
            var structures = _scope.ServiceProvider.GetRequiredService<IActivityStructureService>();
            var authority = _scope.ServiceProvider.GetRequiredService<IActivityDataSelectionAnalyzer>();
            var registry = ClrConstruction.RegistryFor(typeof(BpmnProcess), typeof(BpmnDecision));
            var inputs = new RuntimeInputBindingCompiler(registry);
            var outputs = new RuntimeOutputCaptureCompiler(new RuntimeDurableValueStorageDriverRegistry([new JsonRuntimeDurableValueStorageDriver()]));
            var unused = new UnusedReaders();
            _compiler = new(authority, unused, Catalog, unused, unused, unused,
                new ActivityTemplatePlacer(unused, unused, unused, new Sha256ActivityPlacementHasher(), structures, inputs, outputs),
                inputs, outputs, new WorkflowExecutableHasher(), new ActivityTreeProjector(structures),
                new ExecutableNodeCompiler(structures, registry, inputs, outputs));
        }

        internal ValueTask<WorkflowExecutable> Compile(ActivityNode root) => _compiler.CompileAsync(
            new("version", WorkflowExecutableReferenceScope.Published, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, "artifact-")
            { Source = new("definition", "version", "1.0.0", new([], root, [], [], null), "WorkflowDefinitionVersion", "version", "1.0.0") });
        public void Dispose() { _scope.Dispose(); _provider.Dispose(); }
    }

    // Real catalog/read boundaries only; no fake structure facts, analyzer, compiler or publication bypass.
    private sealed class Catalog(IReadOnlyList<ActivityDefinitionVersion> versions) : IActivityDefinitionVersionStore, IActivityDefinitionLookup
    {
        internal IReadOnlyList<ActivityDefinitionVersion> Versions => versions;
        public Task<ActivityDefinitionVersion> GetAsync(string id, CancellationToken ct = default) => Task.FromResult(versions.Single(x => x.Id == id));
        public Task<ActivityDefinitionVersion> GetWithDefinitionAsync(string id, CancellationToken ct = default) => GetAsync(id, ct);
        public Task<ActivityDefinitionVersion?> FindByDefinitionAndSortKeyAsync(string id, string key, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionIdsAsync(IEnumerable<string> ids, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListAsync(CancellationToken ct = default) => Task.FromResult(versions);
        public async Task<IActivityDefinitionVersion> GetVersion(string id, CancellationToken ct = default) => await GetAsync(id, ct);
        public Task<IActivityDefinitionVersion?> FindVersion(string id, CancellationToken ct = default) => Task.FromResult<IActivityDefinitionVersion?>(versions.SingleOrDefault(x => x.Id == id));
        public Task<IActivityDefinition> GetDefinition(string id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IEnumerable<IActivityDefinition>> ListDefinitions(string? id = null, string? category = null, string? searchTerm = null, string? displayName = null, string? description = null, bool? tenantAgnostic = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IEnumerable<ActivityDefinitionVersionSummary>> ListVersions(string id, CancellationToken ct = default) => throw new NotSupportedException();
    }

    // The public transient-source compile path uses no workflow-version or retained-template readers.
    // Throwing on every unexpected read keeps this fixture from silently inventing missing material.
    private sealed class UnusedReaders : IWorkflowDefinitionVersionStore, IActivityDefinitionVersionPublicationStore,
        IExecutableActivityTemplateReader, IWorkflowExecutableSourceReferenceReader
    {
        public Task<ActivityDefinitionVersionPublication?> FindAsync(string id, CancellationToken ct = default) => Task.FromResult<ActivityDefinitionVersionPublication?>(null);
        Task<IReadOnlyList<ActivityDefinitionVersionPublication>> IActivityDefinitionVersionPublicationStore.ListByDefinitionAsync(string id, CancellationToken ct) => throw new NotSupportedException();
        public Task<WorkflowDefinitionVersion> GetAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WorkflowDefinitionVersion> GetWithDefinitionAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WorkflowDefinitionVersion?> FindByIdAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WorkflowDefinitionVersion?> FindLatestVersionAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
        Task<IReadOnlyList<WorkflowDefinitionVersion>> IWorkflowDefinitionVersionStore.ListByDefinitionAsync(string id, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(string id, string key, CancellationToken ct = default) => throw new NotSupportedException();
        ValueTask<ExecutableActivityTemplate?> IExecutableActivityTemplateReader.FindAsync(string id, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<ExecutableActivityTemplate?> FindByHashAsync(string hash, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask<RuntimeStorePage<ExecutableActivityTemplate>> ListPageAsync(RuntimeStorePageRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        ValueTask<WorkflowExecutableSourceReference?> IWorkflowExecutableSourceReferenceReader.FindAsync(string id, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<RuntimeStorePage<WorkflowExecutableSourceReference>> ListByArtifactPageAsync(WorkflowExecutableSourceReferenceArtifactPageQuery query, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask<RuntimeStorePage<WorkflowExecutableSourceReference>> ListPageAsync(WorkflowExecutableSourceReferencePageQuery query, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyCollection<string>> ListUnreferencedArtifactIdsAsync(WorkflowExecutableArtifactCandidateBatch candidates, DateTimeOffset now, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
