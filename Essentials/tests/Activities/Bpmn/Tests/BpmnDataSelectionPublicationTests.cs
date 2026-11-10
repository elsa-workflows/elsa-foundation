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

    /// <summary>
    /// A result binding without a scope names only the producer. A linear process proves that each task runs once, so
    /// the binding is offered and publishes. A process with a gateway proves nothing about its order, so a linear
    /// subprocess placed on its loop does not get the binding, and a stored one is refused, although the subprocess
    /// itself runs its children once per run.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_scope_free_result_binding_is_offered_and_published_only_when_no_enclosing_process_can_run_it_again(bool gatewayLoop)
    {
        using var fixture = new Fixture();
        ActivityNode Root(ActivityNode capture)
        {
            var linear = BpmnChildResultFactsTests.Process(children: [capture, Decision()]);
            if (!gatewayLoop) return linear;
            return new("outer", "process-version", [], [], new("elsa.bpmn.structure", "1.0.0", JsonSerializer.SerializeToElement(
                new BpmnAuthoredStructure([linear, Decision("gate")],
                    [new("start", "startEvent"), new("task", "task", childNodeId: "process"), new("gateway", "exclusiveGateway", childNodeId: "gate", defaultFlowId: "exit"), new("end", "endEvent")],
                    [new("f1", "start", "task"), new("f2", "task", "gateway"), new("again", "gateway", "task", conditionOutcome: "again"), new("exit", "gateway", "end", isDefault: true)]), Web)));
        }

        var browsed = await fixture.Analyzer.AnalyzeAsync(new("WorkflowDefinition", "definition", "revision", Root(Capture(read: null)), [], [], []),
            new("capture", "value"));
        var source = Assert.Single(browsed.Sources, source => source.Id == "activity:decision:$result");
        Assert.Equal(ActivityChildResultAvailability.Definite, source.Availability);
        Assert.Equal(gatewayLoop ? "EarlierProducerRun" : null, source.ReasonCode);
        Assert.Equal(gatewayLoop, source.Binding is null);

        var stored = Root(Capture(read: new { producerNodeId = "decision", projectionKey = "$result" }));
        if (gatewayLoop)
        {
            var error = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() => fixture.Compile(stored).AsTask());
            var refusal = Assert.IsType<ActivityDataSelectionRejectedException>(error.InnerException);
            Assert.Equal(new[] { ("capture/inputs/value", "ValueFlow/EarlierProducerRun") }, refusal.Findings.Select(finding => (finding.Path, finding.Type)));
            return;
        }
        var executable = await fixture.Compile(stored);
        var read = Assert.Single(executable.RootActivity.ChildSlots).Activities
            .Single(child => child.AuthoredActivityId == "capture").InputBindings["value"].ActivityResult!;
        Assert.Equal("decision", read.ProducerExecutableNodeId);
        Assert.Null(read.ProducerScopeId);
    }

    /// <summary>
    /// One workflow end to end: the binding the analyzer offers is stored unchanged, compiled by the publication
    /// compiler and run by the engine, and the reader commits the value its producer returned.
    /// </summary>
    [Fact]
    public async Task The_offered_scope_free_binding_publishes_unchanged_and_reads_the_producer_at_run_time()
    {
        using var fixture = new Fixture();
        ActivityNode Process(ArgumentState[] readerInputs) => BpmnChildResultFactsTests.Process(children:
            [new ActivityNode("capture", "read-version", readerInputs, []), Decision()]);
        var browsed = await fixture.Analyzer.AnalyzeAsync(new("WorkflowDefinition", "definition", "revision", Process([]), [], [], []),
            new("capture", "Value"));
        var offered = Assert.Single(browsed.Sources, source => source.Id == "activity:decision:$result").Binding!;

        var executable = await fixture.Compile(Process([new("Value", offered, null, null, null, null)]));
        await using var runtime = await BpmnRuntimeFixture.CreateAsync(["process-exec", "decision-exec", "capture-exec"],
            services => new Elsa.Serialization.SystemText.SerializationFeature().ConfigureServices(services));
        var run = await runtime.RunAsync(executable);

        run.AssertWorkflowCompleted();
        var produced = run.AssertCompleted("decision").Completion!.Result.InlineValue!.Value;
        var read = run.AssertCompleted("capture").InputSnapshot!.Values["Value"].InlineValue!.Value;
        Assert.Equal("Done", read.GetProperty("outcome").GetString());
        Assert.True(JsonElement.DeepEquals(produced, read));
    }

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web)
    { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };

    private static ArgumentState Input(string key, object value, string role = "Literal") =>
        new(key, new ArgumentValue(JsonSerializer.SerializeToElement(value), role), null, null, null, null);
    private static ActivityNode Decision(string id = "decision") => new(id, "decision-version", [Input("Outcome", "Done")], []);
    private static ActivityNode Capture(string producer = "decision") => new("capture", "elsa.intrinsic.set-output@1",
        [Input("name", "decisionResult"), Input("value", new { producerNodeId = producer, producerScopeId = "process", projectionKey = "$result" }, "ActivityResult")], [])
    { Intrinsic = new(AuthoredWorkflowIntrinsicKind.SetOutput, new TypeReference("Elsa.Any")) };
    private static ActivityNode Capture(object? read) => new("capture", "elsa.intrinsic.set-output@1",
        read is null ? [Input("name", "decisionResult")] : [Input("name", "decisionResult"), Input("value", read, "ActivityResult")], [])
    { Intrinsic = new(AuthoredWorkflowIntrinsicKind.SetOutput, new TypeReference("Elsa.Any")) };

    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly IServiceScope _scope;
        private readonly WorkflowExecutableCompiler _compiler;
        internal Catalog Catalog { get; }
        internal IActivityDataSelectionAnalyzer Analyzer { get; }

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
                new InputDefinition("Outcome", "Outcome", new TypeReference("String"), null, "Outcome", null, IsNullable: true)),
                Version(typeof(ReadBack), "read-version",
                    new InputDefinition("Value", "Value", new TypeReference("Elsa.Any"), null, "Value", null, IsNullable: true))]);
            var services = new ServiceCollection();
            services.AddOptions();
            new Elsa.Events.EventsFeature().ConfigureServices(services);
            new ActivitiesBpmnDesignFeature().ConfigureServices(services);
            new WorkflowDesignValidationsFeature().ConfigureServices(services);
            services.AddSingleton<IActivityDefinitionLookup>(Catalog);
            _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            _scope = _provider.CreateScope();
            var structures = _scope.ServiceProvider.GetRequiredService<IActivityStructureService>();
            var authority = Analyzer = _scope.ServiceProvider.GetRequiredService<IActivityDataSelectionAnalyzer>();
            var registry = ClrConstruction.RegistryFor(typeof(BpmnProcess), typeof(BpmnDecision), typeof(ReadBack));
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

/// <summary>Completes with the value its input resolved to, so a test can see what a binding read.</summary>
public sealed class ReadBack : Activity<ReadBackResult>
{
    [Elsa.Activities.Runtime.Core.Attributes.ActivityInput] public object? Value { get; set; }

    protected override ValueTask<ActivityTransition<ReadBackResult>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(new ReadBackResult(Value)));
}

public sealed record ReadBackResult(object? Value);
