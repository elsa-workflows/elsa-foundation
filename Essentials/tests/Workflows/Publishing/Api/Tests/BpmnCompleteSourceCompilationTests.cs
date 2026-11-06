using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Activities.Bpmn.Activities;
using Elsa.Activities.Bpmn.Design;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Sequence;
using Elsa.Expressions.Core.Models;
using Elsa.Events.Core.Contracts;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Events;
using Elsa.Workflows.Publishing.Handlers;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>Actual coordinators, exact native descriptors and real registered structure handlers.</summary>
public sealed partial class BpmnCompleteSourceCompilationTests
{
    private const string Model = "http://www.omg.org/spec/BPMN/20100524/MODEL";
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Workflow_compiler_lowers_complete_source_at_root_and_inside_native_Sequence(bool nested, bool canStart)
    {
        var process = Process(canStart: canStart);
        var executable = await CompileWorkflow(nested ? Sequence("outer", process) : process);
        var compiled = Assert.Single(executable.RootActivity.DescendantsAndSelf(), node => node.AuthoredActivityId == process.NodeId);
        Assert.Equal(typeof(BpmnProcess).FullName, compiled.ActivityType);
        Assert.Equal(BpmnProcess.StructureKind, compiled.Structure?.Kind);
        Assert.Equal(BpmnProcess.StructureSchemaVersion, compiled.Structure?.SchemaVersion);
        var structure = Assert.IsType<ExecutableActivityStructure>(compiled.Structure);
        Assert.Equal(3, structure.Payload.GetProperty("elements").GetArrayLength());
        Assert.Equal(2, structure.Payload.GetProperty("sequenceFlows").GetArrayLength());
        Assert.Equal("work", Assert.Single(Assert.Single(compiled.ChildSlots).Activities).AuthoredActivityId);
        Assert.Equal(canStart, compiled.InputBindings[nameof(BpmnProcess.CanStartWorkflow)].Literal?.InlineValue?.GetBoolean());
        Assert.DoesNotContain("documentXml", structure.Payload.GetRawText(), StringComparison.Ordinal);
        Assert.Equal("2.0.0", process.Structure!.SchemaVersion);
        Assert.Contains("<b:task id=\"T\"", process.Structure.Payload.GetProperty("documentXml").GetString()!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Workflow_compiler_refuses_unsupported_complete_intent_even_when_start_is_disabled(bool nested)
    {
        var process = Process(canStart: false, taskAttributes: " implementation=\"urn:unqualified\"");
        var before = process.Structure!.Payload.GetRawText();
        var error = await Record.ExceptionAsync(() => CompileWorkflow(nested ? Sequence("outer", process) : process));
        Assert.NotNull(error);
        Assert.Contains("implementation", error.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, process.Structure.Payload.GetRawText());
    }

    [Fact]
    public async Task Workflow_compiler_keeps_standard_scope_arguments_and_normalizes_its_own_variables()
    {
        var compiled = (await CompileWorkflow(Process(canStart: true, nestedStandardScope: true))).RootActivity;
        Assert.True(compiled.InputBindings[nameof(BpmnProcess.CanStartWorkflow)].Literal?.InlineValue?.GetBoolean());
        var nested = Assert.Single(compiled.DescendantsAndSelf(), node => node.AuthoredActivityId == "scope-sub");
        Assert.False(nested.InputBindings[nameof(BpmnProcess.CanStartWorkflow)].Literal?.InlineValue?.GetBoolean());
        var variable = Assert.Single(nested.Structure!.Payload.GetProperty("variables").Deserialize<RuntimeVariableDeclaration[]>(Web)!);
        Assert.Equal("header-value", variable.VariableKey);
        Assert.Equal("Int32", variable.Type.Alias);
        Assert.Equal(42, variable.InitialBinding?.Literal?.InlineValue?.GetInt32());
        Assert.Empty(compiled.Structure!.Payload.GetProperty("variables").EnumerateArray());
        Assert.Equal("work", Assert.Single(Assert.Single(nested.ChildSlots).Activities).AuthoredActivityId);
    }

    private static async Task<WorkflowExecutable> CompileWorkflow(ActivityNode root, IEnumerable<ActivityDefinitionVersion>? overrideVersions = null)
    {
        var registry = TestWellKnownTypeRegistry.Create();
        registry.RegisterType(typeof(BpmnProcess), TypeAliasConvention.CanonicalAlias(typeof(BpmnProcess)));
        var services = new ServiceCollection();
        services.AddSingleton<IWellKnownTypeRegistry>(registry);
        services.AddSingleton(TimeProvider.System);
        new ActivitiesBpmnDesignFeature().ConfigureServices(services);
        new ActivitiesSequenceFeature().ConfigureServices(services);
        new Elsa.Workflows.Publishing.WorkflowsPublishingFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        var structures = new DefaultActivityStructureService(provider.GetServices<IActivityStructureHandler>());
        var workflow = new WorkflowDefinitionVersion("definition-1", "1.0.0")
        {
            Id = "version-1", Definition = new WorkflowDefinition { Id = "definition-1", Name = "Complete source" },
            State = new([], root, [], [], null)
        };
        var compiler = TestCompiler.Create(new FakeVersionStore(workflow),
            new FakeActivityVersionStore((overrideVersions ?? Versions()).ToList()), structures, registry,
            metadataEnricher: new ExecutableNodeMetadataEnricher(new CompilationEvents(provider.GetServices<IExecutableCompilationSource>())));
        var now = new DateTimeOffset(2026, 11, 6, 0, 0, 0, TimeSpan.Zero);
        return await compiler.CompileAsync(new("version-1", WorkflowExecutableReferenceScope.Published, now, now, null, "bpmn-"));
    }

    private static ActivityNode Process(bool canStart = false, string taskAttributes = "", bool nestedStandardScope = false)
    {
        var task = $"<b:task id=\"T\"{taskAttributes}/><b:endEvent id=\"E\"/><b:sequenceFlow id=\"TE\" sourceRef=\"T\" targetRef=\"E\"/>";
        var body = nestedStandardScope
            ? $"<b:startEvent id=\"Start\"/><b:subProcess id=\"Sub\"><b:startEvent id=\"S\"/>{task}<b:sequenceFlow id=\"ST\" sourceRef=\"S\" targetRef=\"T\"/></b:subProcess><b:endEvent id=\"End\"/><b:sequenceFlow id=\"A\" sourceRef=\"Start\" targetRef=\"Sub\"/><b:sequenceFlow id=\"B\" sourceRef=\"Sub\" targetRef=\"End\"/>"
            : $"<b:startEvent id=\"S\"/>{task}<b:sequenceFlow id=\"ST\" sourceRef=\"S\" targetRef=\"T\"/>";
        var payload = JsonSerializer.SerializeToElement(new
        {
            documentXml = $"<b:definitions xmlns:b=\"{Model}\" targetNamespace=\"urn:compile\"><b:process id=\"P\" isExecutable=\"true\">{body}</b:process></b:definitions>",
            processId = "P", activities = new[] { Sequence("work") }, bindings = new[] { new { elementId = "T", childNodeId = "work" } },
            variables = Array.Empty<VariableDefinition>(),
            scopeHeaders = nestedStandardScope ? new[] { new
            {
                scopeElementId = "Sub", nodeId = "scope-sub", activityVersionId = "bpmn-version",
                inputs = new[] { StartArgument(false) }, outputs = Array.Empty<ArgumentState>(),
                variables = new[] { new VariableDefinition("header-value", "Header value", new("Int32"), null, new ArgumentValue("42", "Literal")) }
            } } : []
        }, Web);
        return new("process", "bpmn-version", [StartArgument(canStart)], [], new(BpmnProcess.StructureKind, "2.0.0", payload));
    }

    private static ArgumentState StartArgument(bool value) => new(nameof(BpmnProcess.CanStartWorkflow), new ArgumentValue(value, "Literal"), null, null, null, null);
    private static ActivityNode Sequence(string id, params ActivityNode[] children) => new(id, "sequence-version", [], [],
        new(SequenceActivity.StructureKind, SequenceActivity.StructureSchemaVersion, JsonSerializer.SerializeToElement(new { activities = children, variables = Array.Empty<VariableDefinition>() }, Web)));

    private static ActivityDefinitionVersion[] Versions() => [Version("bpmn-version", typeof(BpmnProcess), BpmnProcess.StructureKind), Version("sequence-version", typeof(SequenceActivity), SequenceActivity.StructureKind)];
    private static ActivityDefinitionVersion Version(string id, Type type, string kind) => new("1.0.0", $"definition:{id}")
    {
        Id = id, Definition = new ActivityDefinition { Id = $"definition:{id}", ActivityTypeKey = type.FullName!, Category = "BPMN" },
        ProviderKey = WellKnownRuntimeActivityConsumers.ClrActivity, ProviderSchemaVersion = RuntimeActivityDescriptor.InitialSchemaVersion,
        ConsumerKey = WellKnownRuntimeActivityConsumers.ClrActivity, ConsumerSchemaVersion = RuntimeActivityDescriptor.InitialSchemaVersion,
        DescriptorPayload = JsonSerializer.SerializeToElement(new ClrActivityDescriptor(TypeAliasConvention.CanonicalAlias(type)), Web),
        Inputs = type == typeof(BpmnProcess) ? [new(nameof(BpmnProcess.CanStartWorkflow), "Start", new("Boolean"), null, nameof(BpmnProcess.CanStartWorkflow), null, false)] : [],
        DesignFacets = [new(kind, "1.0.0", JsonSerializer.SerializeToElement(new { }, Web))]
    };

    private sealed class CompilationEvents(IEnumerable<IExecutableCompilationSource> sources) : IInlineEventPublisher
    {
        private readonly CollectExecutableCompilation _handler = new(sources);
        public Task Publish(IEvent @event, CancellationToken cancellationToken = default) =>
            _handler.Handle(Assert.IsType<ExecutableCompilationCollecting>(@event), cancellationToken);
    }
}
