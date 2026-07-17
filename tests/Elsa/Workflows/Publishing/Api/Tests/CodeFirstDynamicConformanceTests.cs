using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Sequence;
using Elsa.Activities.Sequence.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Entities;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Authoring;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.CodeGeneration.Generated;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ArgumentValue = Elsa.Expressions.Core.Models.ArgumentValue;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed class CodeFirstDynamicConformanceTests
{
    private const string ProducerVersionId = "actver_L_fnSHOvU1fBt1DIPoBkEOI3idvorapeGRYQMbhDXkE";
    private const string ConsumerVersionId = "actver_Y5CcEH_PHxAHMVlKY8akaR3uJhPUUhRg30v4Rtppyk0";
    private static readonly DateTimeOffset CompiledAt = new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task EquivalentGeneratedCodeFirstAndDynamicDefinitionsCompileToTheSameCanonicalBindingsAndHash()
    {
        var codeFirstState = new ConformanceWorkflow().Compile("version-1");
        var dynamicState = DynamicState();

        var codeFirst = await CompileAsync(codeFirstState);
        var dynamic = await CompileAsync(dynamicState);

        var codeFirstBindings = codeFirst.NodesById["consumer"].InputBindings;
        var dynamicBindings = dynamic.NodesById["consumer"].InputBindings;
        Assert.Equal(CanonicalBindings(dynamicBindings), CanonicalBindings(codeFirstBindings));

        Assert.Equal(dynamic.NodesById.Keys.Order(StringComparer.Ordinal), codeFirst.NodesById.Keys.Order(StringComparer.Ordinal));
        foreach (var nodeId in dynamic.NodesById.Keys)
        {
            var dynamicNode = dynamic.NodesById[nodeId];
            var codeFirstNode = codeFirst.NodesById[nodeId];
            Assert.Equal(dynamicNode.ActivityType, codeFirstNode.ActivityType);
            Assert.Equal(dynamicNode.ActivityTypeVersion, codeFirstNode.ActivityTypeVersion);
            Assert.Equal(dynamicNode.Structure is null, codeFirstNode.Structure is null);
            if (dynamicNode.Structure is not null)
                Assert.True(JsonElement.DeepEquals(dynamicNode.Structure.Payload, codeFirstNode.Structure!.Payload), nodeId);
        }

        Assert.Equal(dynamic.Identity.ArtifactHash, codeFirst.Identity.ArtifactHash);
        Assert.Equal(dynamic.Identity.ArtifactId, codeFirst.Identity.ArtifactId);

        Assert.Equal(RuntimeInputBindingSource.WorkflowRequest, codeFirstBindings["request"].Source);
        Assert.Equal(RuntimeInputBindingSource.VariableRead, codeFirstBindings["retry"].Source);
        Assert.Equal(RuntimeInputBindingSource.ActivityResult, codeFirstBindings["prior-result"].Source);
        Assert.Equal(RuntimeInputBindingSource.Expression, codeFirstBindings["formatted"].Source);
        Assert.Equal(RuntimeInputBindingSource.Literal, codeFirstBindings["literal"].Source);
        Assert.Equal("contract-default", codeFirstBindings["fallback"].Literal!.InlineValue!.Value.GetString());
    }

    [Fact]
    public async Task CodeFirstAndDynamicDefinitionsReceiveTheSamePublicationValidation()
    {
        var codeFirstState = new InvalidLiteralWorkflow().Compile("version-1");
        var dynamicState = InvalidLiteralDynamicState();

        var codeFirst = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() => CompileAsync(codeFirstState));
        var dynamic = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() => CompileAsync(dynamicState));

        Assert.Equal(dynamic.Message, codeFirst.Message);
        Assert.Contains("cannot be converted to alias 'Int32'", codeFirst.Message, StringComparison.Ordinal);
    }

    private static async Task<WorkflowExecutable> CompileAsync(WorkflowDefinitionState state)
    {
        var structureService = CreateStructureService();
        var compiler = TestCompiler.Create(
            new UnusedWorkflowVersionStore(),
            new FakeActivityVersionStore(ActivityVersions().ToList()),
            structureService,
            TestWellKnownTypeRegistry.Create());

        return await compiler.CompileAsync(new WorkflowExecutableCompileRequest(
            VersionId: "version-1",
            Scope: WorkflowExecutableReferenceScope.Published,
            CreatedAt: CompiledAt,
            PublishedAt: CompiledAt,
            ExpiresAt: null,
            ArtifactIdPrefix: "artifact-")
        {
            Source = new WorkflowExecutableCompileSource(
                DefinitionId: "definition-1",
                DefinitionVersionId: "version-1",
                ArtifactVersion: "1.0.0",
                State: state,
                SourceKind: "Conformance",
                SourceId: "version-1",
                SourceVersion: "1.0.0")
        });
    }

    private static WorkflowDefinitionState DynamicState()
    {
        var retry = RetryVariable();
        var producer = Node("producer", ProducerVersionId);
        var consumer = Node(
            "consumer",
            ConsumerVersionId,
            Input("request", new
            {
                memberKey = "customer-id",
                path = "CustomerId"
            }, "WorkflowRequest"),
            Input("retry", new
            {
                referenceKey = retry.ReferenceKey,
                declaringScopeId = VariableReference.WorkflowScopeId
            }, "Variable"),
            Input("prior-result", new
            {
                producerNodeId = "producer",
                projectionKey = "$result",
                producerScopeId = "root"
            }, "ActivityResult"),
            Input("formatted", "request.customerId", "JavaScript"),
            Input("literal", "fixed", "Literal"),
            Input("fallback", null, "Default"));

        return State([producer, consumer], [retry]);
    }

    private static WorkflowDefinitionState InvalidLiteralDynamicState() =>
        State([Node("consumer", ConsumerVersionId, Input("retry", "not-an-integer", "Literal"))], []);

    private static WorkflowDefinitionState State(
        IReadOnlyCollection<ActivityNode> activities,
        IReadOnlyCollection<VariableDefinition> variables)
    {
        var root = new ActivityNode(
            "root",
            "elsa.sequence@1",
            Inputs: [],
            Outputs: [],
            new ActivityNodeStructure(
                "elsa.sequence.structure",
                "1.0.0",
                JsonSerializer.SerializeToElement(new SequenceAuthoredStructure(activities, variables), JsonOptions)));
        return new WorkflowDefinitionState(variables, root, [], [], null);
    }

    private static ActivityNode Node(string nodeId, string activityVersionId, params ArgumentState[] inputs) =>
        new(nodeId, activityVersionId, inputs, Outputs: []);

    private static ArgumentState Input(string key, object? value, string expressionType) =>
        new(key, new ArgumentValue(value, expressionType), null, null, null, null);

    private static VariableDefinition RetryVariable() =>
        new(
            "retry-count",
            "RetryCount",
            TypeReferenceFactory.FromClrType(typeof(int), TypeAliasConvention.CanonicalAlias),
            StorageDriverType: null,
            Default: new ArgumentValue(3, "Literal"));

    private static IReadOnlyCollection<ActivityDefinitionVersion> ActivityVersions() =>
    [
        ActivityVersion("elsa.sequence@1", "Elsa.Sequence", []),
        ActivityVersion(ProducerVersionId, typeof(GeneratedProducerActivity).FullName!, []),
        ActivityVersion(ConsumerVersionId, typeof(GeneratedConsumerActivity).FullName!,
        [
            InputDefinition("request", "String"),
            InputDefinition("retry", "Int32"),
            InputDefinition("prior-result", "String"),
            InputDefinition("formatted", "String"),
            InputDefinition("literal", "String"),
            InputDefinition(
                "fallback",
                "String",
                defaultValue: JsonSerializer.SerializeToElement("contract-default"),
                defaultSyntax: "Literal")
        ])
    ];

    private static ActivityDefinitionVersion ActivityVersion(
        string id,
        string activityTypeKey,
        IReadOnlyCollection<InputDefinition> inputs) =>
        new("1.0.0", $"{id}-definition")
        {
            Id = id,
            Definition = new ActivityDefinition
            {
                Id = $"{id}-definition",
                ActivityTypeKey = activityTypeKey,
                Category = "Tests"
            },
            ProviderKey = WellKnownRuntimeActivityConsumers.ClrActivity,
            ProviderSchemaVersion = RuntimeActivityDescriptor.InitialSchemaVersion,
            ConsumerKey = WellKnownRuntimeActivityConsumers.ClrActivity,
            ConsumerSchemaVersion = RuntimeActivityDescriptor.InitialSchemaVersion,
            DescriptorPayload = JsonSerializer.SerializeToElement(new ClrActivityDescriptor("Object")),
            Inputs = inputs
        };

    private static InputDefinition InputDefinition(
        string key,
        string alias,
        JsonElement? defaultValue = null,
        string? defaultSyntax = null) =>
        new(key, key, new TypeReference(alias), null, key, null, DefaultValue: defaultValue, DefaultSyntax: defaultSyntax);

    private static string CanonicalBindings(IReadOnlyDictionary<string, RuntimeInputBinding> bindings) =>
        JsonSerializer.Serialize(bindings.OrderBy(binding => binding.Key, StringComparer.Ordinal));

    private static IActivityStructureService CreateStructureService()
    {
        var services = new ServiceCollection();
        services.AddScoped<IActivityStructureService, DefaultActivityStructureService>();
        new ActivitiesSequenceFeature().ConfigureServices(services);
        return services.BuildServiceProvider().GetRequiredService<IActivityStructureService>();
    }

    private sealed class ConformanceWorkflow : WorkflowDefinition<ConformanceRequest, string>
    {
        protected override void Build(IWorkflowBuilder<ConformanceRequest, string> workflow)
        {
            var retry = workflow.Variable<int>("RetryCount", 3);
            var producer = workflow.Sequence.GeneratedProducerActivity(nodeId: "producer");

            workflow.Sequence.GeneratedConsumerActivity(
                request: workflow.From(request => request.CustomerId),
                retry: retry.Value,
                priorResult: producer.Result,
                formatted: workflow.Expression<string>("JavaScript", "request.customerId"),
                literal: "fixed",
                nodeId: "consumer");
        }
    }

    private sealed class InvalidLiteralWorkflow : WorkflowDefinition<ConformanceRequest, string>
    {
        protected override void Build(IWorkflowBuilder<ConformanceRequest, string> workflow) =>
            workflow.Sequence.Add<GeneratedConsumerActivity, string>(
                ConsumerVersionId,
                inputs => inputs.Value("retry", "not-an-integer"),
                "consumer");
    }

    private sealed record ConformanceRequest(string CustomerId);
    private sealed class UnusedWorkflowVersionStore : Elsa.Workflows.Design.Persistence.Core.Stores.IWorkflowDefinitionVersionStore
    {
        public Task<WorkflowDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkflowDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkflowDefinitionVersion?> FindByIdAsync(string versionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkflowDefinitionVersion?> FindLatestVersionAsync(string definitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<WorkflowDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
