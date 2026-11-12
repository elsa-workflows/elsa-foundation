using System.Collections.Immutable;
using System.Text.Json;
using Elsa.Activities.Behavioral.Infrastructure;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Flowchart;
using Elsa.Activities.Flowchart.Models;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Sequence;
using Elsa.Activities.Sequence.Models;
using Elsa.Activities.Testing;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.SystemText;
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
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Activities.Behavioral;

/// <summary>
/// A member read of an activity result or a variable end to end: authored, admitted by the real data selection
/// analysis, compiled by the real publication compiler, then run by the engine. The reader commits exactly the selected
/// member, in a Sequence and in a top-level Flowchart, whether the analysis could type the member (pinned and planned)
/// or not (checked at run time; a variable member always is). With member reads off, nothing publishes one.
/// </summary>
public sealed class MemberReadPublicationRunTests
{
    public static TheoryData<string, bool> Structures => new()
    {
        { "sequence", false }, { "sequence", true }, { "flowchart", false }, { "flowchart", true }
    };

    [Theory]
    [MemberData(nameof(Structures))]
    public async Task A_published_result_member_read_commits_exactly_the_selected_member(string structure, bool typed)
    {
        using var publication = new Publication(memberReads: true, typed);

        var executable = await publication.Compile(Root(structure, Read(["address", "street"])));

        var reader = Assert.Single(executable.RootActivity.DescendantsAndSelf(), node => node.AuthoredActivityId == "reader");
        var binding = reader.InputBindings[nameof(MemberReader.Value)];
        Assert.Equal(RuntimeInputBindingSource.ActivityResultMember, binding.Source);
        Assert.Equal(typed ? RuntimeReadSelectionMode.Known : RuntimeReadSelectionMode.DeferredFit, binding.Selection!.Mode);
        Assert.Equal(typed, binding.ConversionPlan is not null);
        Assert.Contains(new RuntimeRequirement(RuntimeReadSelection.RequirementKey, "1"), executable.RuntimeRequirements);

        await using var harness = Harness();
        var run = await harness.RunAsync(executable);

        run.AssertWorkflowCompleted();
        Assert.Equal("Main", run.AssertCompleted("reader").InputSnapshot!.Values[nameof(MemberReader.Value)].InlineValue!.Value.GetString());
        Assert.Equal("Main", run.AssertCompleted("reader").Completion!.Result.InlineValue!.Value.GetProperty("value").GetString());
    }

    [Theory]
    [InlineData("sequence")]
    [InlineData("flowchart")]
    public async Task With_member_reads_off_nothing_publishes_a_member_read(string structure)
    {
        using var publication = new Publication(memberReads: false, typed: true);

        var error = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() =>
            publication.Compile(Root(structure, Read(["address", "street"]))).AsTask());

        var refusal = Assert.IsType<ActivityDataSelectionRejectedException>(error.InnerException);
        Assert.Contains(refusal.Findings, finding => finding.Path == "reader/inputs/Value" && finding.Type == "ActivityData/UnsupportedMemberBinding");
    }

    [Theory]
    [InlineData("sequence")]
    [InlineData("flowchart")]
    public async Task A_published_variable_member_read_commits_exactly_the_selected_member(string structure)
    {
        using var publication = new Publication(memberReads: true, typed: true);

        var executable = await publication.Compile(Root(structure, VariableRead(["address", "street"])), [Customer()]);

        var reader = Assert.Single(executable.RootActivity.DescendantsAndSelf(), node => node.AuthoredActivityId == "reader");
        var binding = reader.InputBindings[nameof(MemberReader.Value)];
        Assert.Equal(RuntimeInputBindingSource.VariableMember, binding.Source);
        Assert.Equal(("customer", "workflow"), (binding.Variable!.VariableKey, binding.Variable.DeclaringScopeId));
        Assert.Equal(["address", "street"], binding.Selection!.Segments);
        Assert.Equal(RuntimeReadSelectionMode.DeferredFit, binding.Selection.Mode);
        Assert.Null(binding.ConversionPlan);
        Assert.Contains(new RuntimeRequirement(RuntimeReadSelection.RequirementKey, "1"), executable.RuntimeRequirements);

        await using var harness = Harness();
        var run = await harness.RunAsync(executable);

        run.AssertWorkflowCompleted();
        Assert.Equal("Main", run.AssertCompleted("reader").InputSnapshot!.Values[nameof(MemberReader.Value)].InlineValue!.Value.GetString());
        Assert.Equal("Main", run.AssertCompleted("reader").Completion!.Result.InlineValue!.Value.GetProperty("value").GetString());
    }

    [Theory]
    [InlineData("sequence")]
    [InlineData("flowchart")]
    public async Task With_member_reads_off_nothing_publishes_a_variable_member_read(string structure)
    {
        using var publication = new Publication(memberReads: false, typed: true);

        var error = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() =>
            publication.Compile(Root(structure, VariableRead(["address", "street"])), [Customer()]).AsTask());

        var refusal = Assert.IsType<ActivityDataSelectionRejectedException>(error.InnerException);
        Assert.Contains(refusal.Findings, finding => finding.Path == "reader/inputs/Value" && finding.Type == "ActivityData/UnsupportedMemberBinding");
    }

    private static ArgumentState VariableRead(string[] path) => new(nameof(MemberReader.Value), new ArgumentValue(JsonSerializer.SerializeToElement(new
    {
        referenceKey = "customer", declaringScopeId = "workflow", memberPath = path
    }), "VariableMember"), null, null, null, null);

    private static VariableDefinition Customer() => new("customer", "Customer", new TypeReference("Object"), null,
        new ArgumentValue(JsonSerializer.SerializeToElement(new { customerId = "c-1", address = new { street = "Main" } }), "Object"));

    private static ArgumentState Read(string[] path) => new(nameof(MemberReader.Value), new ArgumentValue(JsonSerializer.SerializeToElement(new
    {
        producerNodeId = "producer", projectionKey = "$result", isOptional = false, memberPath = path
    }), "ActivityResultMember"), null, null, null, null);

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static ActivityNode Root(string structure, ArgumentState read)
    {
        var producer = new ActivityNode("producer", "producer-version", [], []);
        var reader = new ActivityNode("reader", "reader-version", [read], []);
        return structure == "sequence"
            ? new("root", "sequence-version", [], [], new(SequenceActivity.StructureKind, SequenceActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new SequenceAuthoredStructure([producer, reader]), Web)))
            : new("root", "flowchart-version", [], [], new(FlowchartActivity.StructureKind, FlowchartActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new FlowchartAuthoredStructure([producer, reader],
                    [new(new FlowchartEndpoint("producer", "Done"), new FlowchartEndpoint("reader", null))], "producer"), Web)));
    }

    internal static WorkflowExecutionHarness Harness() => WorkflowExecutionHarness.Create()
        .WithFeature(services => new SerializationFeature().ConfigureServices(services))
        .WithFeature(services => new ActivitiesFlowchartFeature().ConfigureServices(services))
        .WithFeature(services => new ActivitiesSequenceFeature().ConfigureServices(services))
        .Build(Enumerable.Range(1, 16).Select(index => $"actexec-{index}"));

    /// <summary>The real analysis and publication compiler over a fixed catalog; nothing is stored.</summary>
    internal sealed class Publication : IDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly IServiceScope _scope;
        private readonly WorkflowExecutableCompiler _compiler;

        public Publication(bool memberReads, bool typed)
        {
            var serializer = SerializationServices.PayloadSerializer();
            ActivityDefinitionVersion Version(Type type, string id, params InputDefinition[] inputs) => new("1.0.0", "definition:" + id)
            {
                Id = id, Definition = new ActivityDefinition { Id = "definition:" + id, ActivityTypeKey = type.FullName!, Category = "Test" },
                ProviderKey = WellKnownRuntimeActivityConsumers.ClrActivity, ProviderSchemaVersion = RuntimeActivityDescriptor.InitialSchemaVersion,
                ConsumerKey = WellKnownRuntimeActivityConsumers.ClrActivity, ConsumerSchemaVersion = RuntimeActivityDescriptor.InitialSchemaVersion,
                DescriptorPayload = ClrConstruction.Payload(serializer, type), Inputs = inputs
            };
            var catalog = new Catalog([
                Version(typeof(SequenceActivity), "sequence-version"),
                Version(typeof(FlowchartActivity), "flowchart-version"),
                Version(typeof(AddressProducer), "producer-version"),
                Version(typeof(MemberReader), "reader-version",
                    new InputDefinition(nameof(MemberReader.Value), nameof(MemberReader.Value), new TypeReference("String"), null, "Value", null, IsNullable: false))
            ]);
            var services = new ServiceCollection();
            services.AddOptions();
            new Elsa.Events.EventsFeature().ConfigureServices(services);
            services.AddScoped<IActivityStructureService, DefaultActivityStructureService>();
            new ActivitiesSequenceFeature().ConfigureServices(services);
            new ActivitiesFlowchartFeature().ConfigureServices(services);
            new WorkflowDesignValidationsFeature { MemberReads = memberReads }.ConfigureServices(services);
            services.AddSingleton<IActivityDefinitionLookup>(catalog);
            if (typed)
                services.AddSingleton<IActivitySerializedShapeSource>(new StreetShape());
            _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            _scope = _provider.CreateScope();
            var structures = _scope.ServiceProvider.GetRequiredService<IActivityStructureService>();
            var analyzer = _scope.ServiceProvider.GetRequiredService<IActivityDataSelectionAnalyzer>();
            var registry = ClrConstruction.RegistryFor(typeof(SequenceActivity), typeof(FlowchartActivity), typeof(AddressProducer), typeof(MemberReader));
            var inputs = new RuntimeInputBindingCompiler(registry);
            var outputs = new RuntimeOutputCaptureCompiler(new RuntimeDurableValueStorageDriverRegistry([new JsonRuntimeDurableValueStorageDriver()]));
            var unused = new UnusedReaders();
            _compiler = new(analyzer, unused, catalog, unused, unused, unused,
                new ActivityTemplatePlacer(unused, unused, unused, new Sha256ActivityPlacementHasher(), structures, inputs, outputs),
                inputs, outputs, new WorkflowExecutableHasher(), new ActivityTreeProjector(structures),
                new ExecutableNodeCompiler(structures, registry, inputs, outputs));
        }

        public ValueTask<WorkflowExecutable> Compile(ActivityNode root, VariableDefinition[]? variables = null, InputDefinition[]? inputs = null) => _compiler.CompileAsync(
            new("version", WorkflowExecutableReferenceScope.Published, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, "artifact-")
            { Source = new("definition", "version", "1.0.0", new(variables ?? [], root, inputs ?? [], [], null), "WorkflowDefinitionVersion", "version", "1.0.0") });

        public void Dispose()
        {
            _scope.Dispose();
            _provider.Dispose();
        }
    }

    /// <summary>Types the producer's <c>address.street</c> member as a required, non-null string.</summary>
    private sealed class StreetShape : IActivitySerializedShapeSource
    {
        public ValueTask<ActivitySerializedShape?> DescribeResultAsync(IActivityDefinitionVersion admittedVersion, string projectionKey,
            ImmutableArray<string> memberPath, ActivitySerializedShapeLimits limits, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ActivitySerializedShape?>(admittedVersion.Id != "producer-version"
                ? null
                : memberPath.SequenceEqual(["address", "street"])
                    ? new(ActivitySerializedShapeState.Known, ActivitySerializedValueKind.Scalar, new("String"), "contract", "elsa.activity-value-json/1", "metadata")
                    {
                        IsNullable = false, Presence = ActivitySerializedPresence.Required, Representation = ValueRepresentation.TextValue
                    }
                    : new(ActivitySerializedShapeState.Known, ActivitySerializedValueKind.Object, new("Object"), "contract", "elsa.activity-value-json/1", "metadata"));

        public ValueTask<ActivitySerializedShape?> DescribeDeclaredTypeAsync(TypeReference declaredType, string ownerContractIdentity,
            ImmutableArray<string> memberPath, ActivitySerializedShapeLimits limits, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ActivitySerializedShape?>(null);
    }

    private sealed class Catalog(IReadOnlyList<ActivityDefinitionVersion> versions) : IActivityDefinitionVersionStore, IActivityDefinitionLookup
    {
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

    // The transient-source compile path reads no workflow version, publication, template or source reference.
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

    public sealed record StreetAddress(string Street);

    public sealed record AddressResult(string CustomerId, StreetAddress Address);

    public sealed record ReadValue([property: Elsa.Activities.Runtime.Core.Attributes.Output(Key = "Value", Path = "value")] string? Value);

    /// <summary>Produces a customer with an address, whose camelCase members a reader selects from.</summary>
    public sealed class AddressProducer : Activity<AddressResult>
    {
        protected override ValueTask<ActivityTransition<AddressResult>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(new AddressResult("c-1", new StreetAddress("Main"))));
    }

    /// <summary>Returns the text its input resolved to.</summary>
    public sealed class MemberReader : Activity<ReadValue>
    {
        [Elsa.Activities.Runtime.Core.Attributes.ActivityInput] public string Value { get; set; } = null!;

        protected override ValueTask<ActivityTransition<ReadValue>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(new ReadValue(Value)));
    }
}
