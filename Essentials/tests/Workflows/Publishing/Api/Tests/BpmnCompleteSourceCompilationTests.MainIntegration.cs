using System.Text.Json;
using Elsa.Activities.Bpmn.Activities;
using Elsa.Activities.Bpmn.Design;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Sequence;
using Elsa.Expressions.Core.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Values;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed partial class BpmnCompleteSourceCompilationTests
{
    [Theory]
    [InlineData("Input", false)]
    [InlineData("Input", true)]
    [InlineData("InputMember", false)]
    [InlineData("InputMember", true)]
    public async Task Contextual_compilation_snapshots_reused_declared_inputs_for_each_compile(string role, bool nested)
    {
        using var publication = new MainIntegrationPublication(memberReads: true);
        var owner = Process() with { Inputs = [BoundaryStart(role, "process")] };
        var root = nested ? Sequence("outer", owner) : owner;
        // Keep this exact array across both actual workflow compiles. Replacing its item must not reuse the first index.
        InputDefinition[] inputs = [BoundaryInput("process", "original-public-name", role)];
        var before = owner.Structure!.Payload.GetRawText();

        var first = await publication.Compile(root, inputs);
        inputs[0] = inputs[0] with { Name = "renamed-public-name" };
        var second = await publication.Compile(root, inputs);

        var original = ProcessStart(first);
        var current = ProcessStart(second);
        Assert.Equal("original-public-name", original.WorkflowRequest!.MemberKey);
        Assert.Equal("renamed-public-name", current.WorkflowRequest!.MemberKey);
        Assert.Equal(role == "Input" ? RuntimeInputBindingSource.WorkflowRequest : RuntimeInputBindingSource.WorkflowRequestMember, current.Source);
        if (role == "InputMember")
        {
            Assert.Equal(["enabled"], current.Selection!.Segments);
            Assert.Equal(RuntimeReadSelectionMode.DeferredFit, current.Selection.Mode);
            Assert.Null(current.ConversionPlan);
        }
        Assert.Equal(before, owner.Structure.Payload.GetRawText());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Complete_source_keeps_default_off_member_admission(bool nested)
    {
        using var publication = new MainIntegrationPublication(memberReads: false);
        var owner = Process() with { Inputs = [BoundaryStart("InputMember", "process")] };
        var error = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() => publication.Compile(
            nested ? Sequence("outer", owner) : owner, [BoundaryInput("process", "public-name", "InputMember")]));

        var refusal = Assert.IsType<ActivityDataSelectionRejectedException>(error.InnerException);
        Assert.Contains(refusal.Findings, finding => finding.Path == $"process/inputs/{nameof(BpmnProcess.CanStartWorkflow)}"
            && finding.Type == "ActivityData/UnsupportedMemberBinding");
    }

    [Theory]
    [InlineData("Input", false)]
    [InlineData("Input", true)]
    [InlineData("InputMember", false)]
    [InlineData("InputMember", true)]
    public async Task Complete_source_never_selects_a_missing_or_ambiguous_declared_input(string role, bool duplicate)
    {
        using var publication = new MainIntegrationPublication(memberReads: true);
        var owner = Process() with { Inputs = [BoundaryStart(role, "process")] };
        InputDefinition[] inputs = duplicate
            ? [BoundaryInput("process", "first", role), BoundaryInput("process", "second", role)]
            : [BoundaryInput("other", "unrelated", role)];
        var before = owner.Structure!.Payload.GetRawText();

        await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() => publication.Compile(owner, inputs));

        Assert.Equal(before, owner.Structure.Payload.GetRawText());
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[1]")]
    public async Task Complete_source_preserves_malformed_input_member_paths_for_refusal(string path)
    {
        using var publication = new MainIntegrationPublication(memberReads: true);
        var read = new ArgumentValue(JsonSerializer.SerializeToElement(new
            { referenceKey = "process", memberPath = JsonDocument.Parse(path).RootElement.Clone() }), "InputMember");
        var owner = Process() with { Inputs = [new(nameof(BpmnProcess.CanStartWorkflow), read, null, null, null, null)] };

        var error = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() => publication.Compile(owner,
            [BoundaryInput("process", "public-name", "InputMember")]));

        Assert.Contains("ActivityData/InvalidMemberPath", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Complete_source_in_an_actual_Graph_keeps_the_boundary_reference_key_after_placement()
    {
        using var fixture = new GraphFixture(memberReads: true);
        var owner = Process() with { Inputs = [BoundaryStart("InputMember", "process")] };

        ActivityInputContract[] inputs = [new("process", "different-public-name", new("Object"), false, true, null, "elsa.json")];
        var graph = await fixture.Compile(owner, inputs);

        Assert.NotNull(graph.Template);
        var supplied = new RuntimeInputBindingCompiler(TestWellKnownTypeRegistry.Create()).Compile("graph-call",
            BoundaryInput("process", "different-public-name", "InputMember"), new ArgumentValue(
                JsonSerializer.SerializeToElement(new { enabled = false }), "Object"));
        var placed = await fixture.Place(graph.Template!, inputs, new Dictionary<string, RuntimeInputBinding> { ["process"] = supplied });
        var process = Assert.Single(placed.Root.DescendantsAndSelf(), node => node.ActivityType == typeof(BpmnProcess).FullName);
        var binding = process.InputBindings[nameof(BpmnProcess.CanStartWorkflow)];
        Assert.Equal(RuntimeInputBindingSource.WorkflowRequestMember, binding.Source);
        Assert.Equal("process", binding.WorkflowRequest!.MemberKey); // Also an occurrence ID; it is not a placed node reference.
        Assert.Equal(["enabled"], binding.Selection!.Segments);
        Assert.Equal(RuntimeReadSelectionMode.DeferredFit, binding.Selection.Mode);
        Assert.Null(binding.ConversionPlan);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Complete_source_member_result_reads_follow_raw_flow_order_not_activity_array_order(bool producerFirst)
    {
        using var publication = new MainIntegrationPublication(memberReads: true);
        var first = producerFirst ? "Produce" : "Consume";
        var last = producerFirst ? "Consume" : "Produce";
        var read = new ArgumentValue(JsonSerializer.SerializeToElement(new
            { producerNodeId = "producer", projectionKey = "$result", memberPath = new[] { "customerId" } }), "ActivityResultMember");
        var owner = Process() with { Structure = new(BpmnProcess.StructureKind, "2.0.0", JsonSerializer.SerializeToElement(new
        {
            documentXml = $"<b:definitions xmlns:b='{Model}'><b:process id='P' isExecutable='true'><b:startEvent id='S'/><b:task id='Produce'/><b:task id='Consume'/><b:endEvent id='E'/><b:sequenceFlow id='A' sourceRef='S' targetRef='{first}'/><b:sequenceFlow id='B' sourceRef='{first}' targetRef='{last}'/><b:sequenceFlow id='C' sourceRef='{last}' targetRef='E'/></b:process></b:definitions>",
            processId = "P",
            activities = new[] { new ActivityNode("consumer", typeof(TestWriteLineActivity).FullName!,
                [new("Text", read, null, null, null, null)], []),
                new ActivityNode("producer", typeof(PublishWorkflowRequestHandlerTests.CustomerProducer).FullName!, [], []) },
            bindings = new[] { new { elementId = "Produce", childNodeId = "producer" }, new { elementId = "Consume", childNodeId = "consumer" } },
            variables = Array.Empty<VariableDefinition>()
        }, Web)) };
        if (!producerFirst)
        {
            var error = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() => publication.Compile(owner, []));
            var refusal = Assert.IsType<ActivityDataSelectionRejectedException>(error.InnerException);
            Assert.Contains(refusal.Findings, finding => finding.Type == "ValueFlow/UnavailableProducer");
            return;
        }

        var executable = await publication.Compile(owner, []);

        var consumer = Assert.Single(executable.RootActivity.DescendantsAndSelf(), node => node.AuthoredActivityId == "consumer");
        Assert.Equal(RuntimeInputBindingSource.ActivityResultMember, consumer.InputBindings["Text"].Source);
        Assert.Equal(["customerId"], consumer.InputBindings["Text"].Selection!.Segments);
    }

    private static InputDefinition BoundaryInput(string key, string name, string role) =>
        new(key, name, new(role == "Input" ? "Boolean" : "Object"), null, name, null, IsNullable: false);
    private static ArgumentState BoundaryStart(string role, string key) => new(nameof(BpmnProcess.CanStartWorkflow),
        new ArgumentValue(JsonSerializer.SerializeToElement(role == "Input" ? (object)new { referenceKey = key }
            : new { referenceKey = key, memberPath = new[] { "enabled" } }), role), null, null, null, null);
    private static RuntimeInputBinding ProcessStart(WorkflowExecutable executable) => ProcessStart(executable.RootActivity);
    private static RuntimeInputBinding ProcessStart(ExecutableNode root) =>
        Assert.Single(root.DescendantsAndSelf(), node => node.AuthoredActivityId == "process").InputBindings[nameof(BpmnProcess.CanStartWorkflow)];

    private sealed class MainIntegrationPublication : IDisposable
    {
        private readonly ServiceProvider _services;
        private readonly IServiceScope _scope;
        private readonly WorkflowExecutableCompiler _compiler;

        internal MainIntegrationPublication(bool memberReads)
        {
            var registry = TestWellKnownTypeRegistry.Create();
            registry.RegisterType(typeof(BpmnProcess), TypeAliasConvention.CanonicalAlias(typeof(BpmnProcess)));
            registry.RegisterType(typeof(PublishWorkflowRequestHandlerTests.CustomerProducer), TypeAliasConvention.CanonicalAlias(typeof(PublishWorkflowRequestHandlerTests.CustomerProducer)));
            var services = new ServiceCollection();
            services.AddSingleton<IWellKnownTypeRegistry>(registry);
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton<IRuntimeDurableValueStorageDriverRegistry>(new RuntimeDurableValueStorageDriverRegistry([new JsonRuntimeDurableValueStorageDriver()]));
            new ActivitiesBpmnDesignFeature().ConfigureServices(services);
            new ActivitiesSequenceFeature().ConfigureServices(services);
            new Elsa.Workflows.Publishing.WorkflowsPublishingFeature().ConfigureServices(services);
            _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            _scope = _services.CreateScope();
            var structures = _scope.ServiceProvider.GetRequiredService<IActivityStructureService>();
            var versions = new FakeActivityVersionStore([.. Versions(), SecretBindingCompilerFixture.ClrActivityVersion(typeof(TestWriteLineActivity)),
                SecretBindingCompilerFixture.ClrActivityVersion(typeof(PublishWorkflowRequestHandlerTests.CustomerProducer))]);
            var workflow = new WorkflowDefinitionVersion("definition-1", "1.0.0") { Id = "version-1" };
            _compiler = TestCompiler.Create(new FakeVersionStore(workflow), versions, structures, registry,
                metadataEnricher: new ExecutableNodeMetadataEnricher(new CompilationEvents(_scope.ServiceProvider.GetServices<IExecutableCompilationSource>())),
                dataSelectionAnalyzer: TestCompiler.SourceAuthority(versions, structures, memberReads),
                nodeCompiler: _scope.ServiceProvider.GetRequiredService<ExecutableNodeCompiler>());
        }

        internal Task<WorkflowExecutable> Compile(ActivityNode root, IEnumerable<InputDefinition> inputs)
        {
            var now = new DateTimeOffset(2026, 11, 13, 0, 0, 0, TimeSpan.Zero);
            return _compiler.CompileAsync(new("version-1", WorkflowExecutableReferenceScope.Published, now, now, null, "bpmn-main-")
            { Source = new("definition-1", "version-1", "1.0.0", new([], root, inputs, [], null), "WorkflowDefinitionVersion", "version-1", "1.0.0") }).AsTask();
        }

        public void Dispose() { _scope.Dispose(); _services.Dispose(); }
    }
}
