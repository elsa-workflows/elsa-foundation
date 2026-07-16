using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Validations;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Validators;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Design.Tests;

public sealed class ValueFlowValidatorTests
{
    [Fact]
    public void Validation_feature_registers_value_flow_validation_in_the_publication_gate()
    {
        var services = new ServiceCollection();

        new WorkflowDesignValidationsFeature().ConfigureServices(services);

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IDraftValidator) &&
            descriptor.ImplementationType == typeof(ValueFlowValidator));
    }

    [Fact]
    public async Task Concurrent_ordinary_writes_to_the_same_variable_are_rejected()
    {
        var parallel = Parallel(
            ("left", Set("set-left", "counter", "$workflow")),
            ("right", Set("set-right", "counter", "$workflow")));

        var errors = await Validate(Sequence("root", parallel));

        var error = Assert.Single(errors, item => item.Type == "ValueFlow/ConcurrentWrite");
        Assert.Equal("$workflow/variables/counter", error.Path);
    }

    [Fact]
    public async Task Explicit_merge_after_parallel_join_is_accepted()
    {
        var parallel = Parallel(
            ("left", Activity("left")),
            ("right", Activity("right")));

        var errors = await Validate(Sequence("root", parallel, Merge("merge", "counter", "workflow")));

        Assert.DoesNotContain(errors, item => item.Type == "ValueFlow/ConcurrentWrite");
    }

    [Fact]
    public async Task Merge_nodes_inside_parallel_branches_do_not_make_last_writer_wins_deterministic()
    {
        var parallel = Parallel(
            ("left", Merge("merge-left", "counter", "workflow")),
            ("right", Merge("merge-right", "counter", "workflow")));

        var errors = await Validate(Sequence("root", parallel));

        var error = Assert.Single(errors, item => item.Type == "ValueFlow/ConcurrentWrite");
        Assert.Equal("$workflow/variables/counter", error.Path);
    }

    [Fact]
    public async Task Required_result_from_a_later_sequence_node_is_rejected_as_unavailable()
    {
        var consumer = Activity("consumer", ResultInput("value", "producer", "$workflow"));
        var producer = Activity("producer");

        var errors = await Validate(Sequence("root", consumer, producer));

        var error = Assert.Single(errors, item => item.Type == "ValueFlow/UnavailableProducer");
        Assert.Equal("consumer/inputs/value", error.Path);
    }

    [Fact]
    public async Task Required_result_that_does_not_dominate_a_flowchart_consumer_is_rejected()
    {
        var start = Activity("start");
        var producer = Activity("producer");
        var bypass = Activity("bypass");
        var consumer = Activity("consumer", ResultInput("value", "producer", "flow"));
        var flowchart = Flowchart(
            "flow",
            [start, producer, bypass, consumer],
            [
                ("produce", "start", "producer"),
                ("skip", "start", "bypass"),
                ("produced", "producer", "consumer"),
                ("bypassed", "bypass", "consumer")
            ],
            "start");

        var errors = await Validate(Sequence("root", flowchart));

        var error = Assert.Single(errors, item => item.Type == "ValueFlow/UnavailableProducer");
        Assert.Equal("consumer/inputs/value", error.Path);
    }

    [Fact]
    public async Task Direct_result_read_across_a_structured_scope_boundary_is_rejected()
    {
        var producer = Activity("inner-producer");
        var innerScope = Sequence("inner-scope", producer, Return("return", "inner-producer", "inner-scope"));
        var consumer = Activity("consumer", ResultInput("value", "inner-producer", "inner-scope"));

        var errors = await Validate(Sequence("root", innerScope, consumer));

        var error = Assert.Single(errors, item => item.Type == "ValueFlow/ScopeBoundary");
        Assert.Equal("consumer/inputs/value", error.Path);
    }

    [Fact]
    public async Task Structured_result_with_an_explicit_return_is_available_to_a_downstream_consumer()
    {
        var producer = Activity("inner-producer");
        var innerScope = Sequence("inner-scope", producer, Return("return", "inner-producer", "inner-scope"));
        var consumer = Activity("consumer", ResultInput("value", "inner-scope", "$workflow"));

        var errors = await Validate(Sequence("root", innerScope, consumer));

        Assert.DoesNotContain(errors, item => item.Type.StartsWith("ValueFlow/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Result_lookup_over_a_cyclic_back_edge_is_rejected()
    {
        var first = Activity("first", ResultInput("previous", "second", "flow"));
        var second = Activity("second");
        var flowchart = Flowchart(
            "flow",
            [first, second],
            [("forward", "first", "second"), ("back", "second", "first")],
            "first");

        var errors = await Validate(Sequence("root", flowchart));

        var error = Assert.Single(errors, item => item.Type == "ValueFlow/CyclicBackEdge");
        Assert.Equal("first/inputs/previous", error.Path);
    }

    [Fact]
    public async Task Parallel_branches_require_unique_stable_identities()
    {
        var parallel = Parallel(
            ("same", Activity("left")),
            ("same", Activity("right")));

        var errors = await Validate(Sequence("root", parallel));

        var error = Assert.Single(errors, item => item.Type == "ValueFlow/UnstableCollectionIdentity");
        Assert.Equal("parallel/structure/branches/same", error.Path);
    }

    private static async Task<IReadOnlyCollection<Elsa.Workflows.Design.Validations.Core.Models.ValidationError>> Validate(ActivityNode root)
    {
        var validator = new ValueFlowValidator(StructureService());
        var draft = new StubDraft(new WorkflowDefinitionState([], root, [], [], null, null));
        return [.. await validator.Validate(draft, CancellationToken.None)];
    }

    private static IActivityStructureService StructureService() =>
        new DefaultActivityStructureService([
            new JsonStructureHandler("elsa.sequence.structure", StructureShape.Sequence),
            new JsonStructureHandler("elsa.parallel.structure", StructureShape.Parallel),
            new JsonStructureHandler("elsa.flowchart.structure", StructureShape.Flowchart)
        ]);

    private static ActivityNode Activity(string nodeId, params ArgumentState[] inputs) =>
        new(nodeId, "test.activity@1", inputs, []);

    private static ActivityNode Set(string nodeId, string variableKey, string declaringScopeId) =>
        new ActivityNode(
            nodeId,
            "$intrinsic",
            [
                new ArgumentState("value", new ArgumentValue(1, "Literal"), null, null, null, null)
            ],
            [])
        {
            Intrinsic = new AuthoredWorkflowIntrinsic(
                AuthoredWorkflowIntrinsicKind.Set,
                new TypeReference("Int32"),
                new VariableReference(variableKey, declaringScopeId))
        };

    private static ActivityNode Merge(string nodeId, string variableKey, string declaringScopeId) =>
        new ActivityNode(
            nodeId,
            "$intrinsic",
            [new ArgumentState("value", new ArgumentValue(1, "Literal"), null, null, null, null)],
            [])
        {
            Intrinsic = new AuthoredWorkflowIntrinsic(
                AuthoredWorkflowIntrinsicKind.Merge,
                new TypeReference("Int32"),
                new VariableReference(variableKey, declaringScopeId))
        };

    private static ActivityNode Return(string nodeId, string producerNodeId, string producerScopeId) =>
        new ActivityNode(nodeId, "$intrinsic", [ResultInput("value", producerNodeId, producerScopeId)], [])
        {
            Intrinsic = new AuthoredWorkflowIntrinsic(AuthoredWorkflowIntrinsicKind.Return, new TypeReference("Int32"))
        };

    private static ArgumentState ResultInput(string inputKey, string producerNodeId, string producerScopeId, bool isOptional = false) =>
        new(
            inputKey,
            new ArgumentValue(
                JsonSerializer.SerializeToElement(new { producerNodeId, projectionKey = "$result", producerScopeId, isOptional }),
                "ActivityResult"),
            null,
            null,
            null,
            null);

    private static ActivityNode Sequence(string nodeId, params ActivityNode[] activities) =>
        Structured(nodeId, "elsa.sequence.structure", new { activities, variables = Array.Empty<object>() });

    private static ActivityNode Parallel(params (string Name, ActivityNode Activity)[] branches) =>
        Structured(
            "parallel",
            "elsa.parallel.structure",
            new { branches = branches.Select(branch => new { name = branch.Name, activity = branch.Activity }).ToArray(), threshold = (int?)null });

    private static ActivityNode Flowchart(
        string nodeId,
        IReadOnlyCollection<ActivityNode> activities,
        IReadOnlyCollection<(string Id, string Source, string Target)> connections,
        string startNodeId) =>
        Structured(
            nodeId,
            "elsa.flowchart.structure",
            new
            {
                activities,
                connections = connections.Select(connection => new
                {
                    id = connection.Id,
                    source = new { nodeId = connection.Source, port = (string?)null },
                    target = new { nodeId = connection.Target, port = (string?)null }
                }).ToArray(),
                startNodeId,
                nodeMetadata = new Dictionary<string, object>(),
                connectionMetadata = new Dictionary<string, object>(),
                variables = Array.Empty<object>()
            });

    private static ActivityNode Structured(string nodeId, string kind, object payload) =>
        new(
            nodeId,
            $"{kind}@1",
            [],
            [],
            new ActivityNodeStructure(kind, "1.0.0", JsonSerializer.SerializeToElement(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web))));

    private sealed class StubDraft(WorkflowDefinitionState state) : IWorkflowDefinitionDraft
    {
        public string Id => "draft";
        public string WorkflowDefinitionId => "workflow";
        public WorkflowDefinitionState State { get; } = state;
        public DateTimeOffset CreatedAt => DateTimeOffset.UnixEpoch;
        public DateTimeOffset LastModifiedAt => DateTimeOffset.UnixEpoch;
    }

    private enum StructureShape
    {
        Sequence,
        Parallel,
        Flowchart
    }

    private sealed class JsonStructureHandler(string kind, StructureShape shape) : IActivityStructureHandler
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        public string Kind => kind;
        public string SchemaVersion => "1.0.0";

        public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity)
        {
            var payload = activity.Structure!.Payload;
            if (shape == StructureShape.Parallel)
            {
                return payload.GetProperty("branches")
                    .EnumerateArray()
                    .Select(branch => new ActivityChildProjection(
                        branch.GetProperty("name").GetString() ?? string.Empty,
                        branch.TryGetProperty("activity", out var child) && child.ValueKind != JsonValueKind.Null
                            ? [child.Deserialize<ActivityNode>(JsonOptions)!]
                            : []))
                    .ToArray();
            }

            var activities = payload.GetProperty("activities").Deserialize<ActivityNode[]>(JsonOptions) ?? [];
            return [new ActivityChildProjection("Activities", activities)];
        }

        public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections) =>
            throw new NotSupportedException();

        public ActivityNodeStructure CompileExecutableStructure(ActivityNode activity) => activity.Structure!;
    }
}
