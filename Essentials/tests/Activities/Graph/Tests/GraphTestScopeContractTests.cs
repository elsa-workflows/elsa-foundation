using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Graph.Runtime;
using Elsa.Activities.Graph.Runtime.Models;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Graph.Tests;

public class GraphTestScopeContractTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static IRuntimeTestScopeContractSource Source()
    {
        var services = new ServiceCollection();
        new GraphActivitiesRuntimeFeature().ConfigureServices(services);
        return Assert.Single(services.BuildServiceProvider().GetServices<IRuntimeTestScopeContractSource>());
    }

    [Fact]
    public async Task Actual_graph_owner_returns_its_closed_declarations_and_literal_default()
    {
        var (executable, node) = Candidate();
        var contract = await Source().GetContractAsync(executable, node, VariableFrameKind.Root);
        Assert.NotNull(contract);
        Assert.True(contract.IsClosedBoundary);
        Assert.Equal("boundary", contract.ScopeId);
        Assert.Equal("total", contract.Variables["total-key"].Name);
        Assert.Equal("System.Int32", contract.Variables["total-key"].Type.Alias);
        Assert.Equal(7, contract.Variables["total-key"].InitialBinding!.Literal!.InlineValue!.Value.GetInt32());
    }

    [Theory]
    [InlineData(VariableFrameKind.Container)]
    [InlineData(VariableFrameKind.Iteration)]
    public async Task A_closed_graph_does_not_claim_an_unrelated_scope_kind(VariableFrameKind kind)
    {
        var (executable, node) = Candidate();
        Assert.Null(await Source().GetContractAsync(executable, node, kind));
    }

    [Fact]
    public async Task Mismatched_dependency_or_unqualified_storage_is_refused()
    {
        var (executable, node) = Candidate(templateStamp: "different");
        var pin = await Assert.ThrowsAsync<RuntimeTestContextUnavailableException>(() => Source().GetContractAsync(executable, node, VariableFrameKind.Root).AsTask());
        Assert.Equal("graph.test-scope.pin-mismatch", pin.Code);
        (executable, node) = Candidate(storage: "unknown-driver");
        var storageRefusal = await Assert.ThrowsAsync<RuntimeTestContextUnavailableException>(() => Source().GetContractAsync(executable, node, VariableFrameKind.Root).AsTask());
        Assert.Equal("total-key", storageRefusal.MemberKey);
    }

    [Theory]
    [InlineData("Literal", true)]
    [InlineData("JavaScript", false)]
    public async Task Only_literal_graph_input_defaults_are_materialized_as_values(string syntax, bool literal)
    {
        var (executable, node) = Candidate(inputDefault: new(syntax, JsonSerializer.SerializeToElement("hello")));
        var contract = (await Source().GetContractAsync(executable, node, VariableFrameKind.Root))!;
        var input = contract.Inputs["greeting"];
        Assert.Equal(literal, input.HasDefault);
        Assert.Equal(!literal, input.IsRequired); // An expression default requires explicit materialized setup.
        if (literal) Assert.Equal("hello", input.DefaultValue!.Value.GetString());
        else Assert.Null(input.DefaultValue);
    }

    private static (WorkflowExecutable Executable, ExecutableNode Node) Candidate(string? templateStamp = null, string storage = WellKnownRuntimeDurableValueStorageDrivers.Json,
        GraphActivityExpression? inputDefault = null)
    {
        var graphInputs = inputDefault is null ? Array.Empty<GraphActivityInputDescriptor>() :
            new[] { new GraphActivityInputDescriptor("greeting", JsonSerializer.SerializeToElement(new { alias = "System.String" }), WellKnownRuntimeDurableValueStorageDrivers.Json, inputDefault, "greeting") };
        var inputs = inputDefault is null ? Array.Empty<ActivityInputContract>() :
            new[] { new ActivityInputContract("greeting", "greeting", new("System.String"), false, false, true, inputDefault.Value, ActivityValuePolicy.Default) };
        var descriptor = new GraphActivityDescriptor("definition", "definition-version", "1.0.0", "template-hash", "entry", null, graphInputs,
            [new("total-key", "total", JsonSerializer.SerializeToElement(new { alias = "System.Int32" }), storage,
                new("Literal", JsonSerializer.SerializeToElement(7)))], [], [], [], []);
        var payload = JsonNode.Parse(JsonSerializer.Serialize(descriptor, Json))!.AsObject();
        payload["scopeModel"] = RuntimeMetadataKeys.ActivityExecutionScopeClosed;
        var runtimeDescriptor = new RuntimeActivityDescriptor(WellKnownRuntimeActivityConsumers.GraphActivity, "1", JsonSerializer.SerializeToElement(payload, Json));
        var contract = new ActivityContract("graph", "1.0.0", WellKnownRuntimeActivityConsumers.GraphActivity, runtimeDescriptor.Payload, inputs,
            new(new("object"), true, ActivityValuePolicy.Default with { Lifecycle = ActivityValueLifecycle.Result }, []), ["Done"],
            new(WellKnownRuntimeActivityConsumers.GraphActivity, WellKnownRuntimeActivityConsumers.GraphActivity));
        var node = new ExecutableNode("boundary", "boundary", "graph", "1.0.0", runtimeDescriptor,
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>
            {
                [RuntimeMetadataKeys.ActivityExecutionScope] = RuntimeMetadataKeys.ActivityExecutionScopeClosed,
                ["activity.definitionId"] = descriptor.DefinitionId,
                ["activity.definitionVersionId"] = descriptor.DefinitionVersionId,
                ["activity.version"] = descriptor.Version,
                ["activity.templateHash"] = templateStamp ?? descriptor.TemplateHash,
                ["graph.templateHash"] = descriptor.TemplateHash
            }, activityContract: contract);
        var executable = new WorkflowExecutable(new("artifact", "workflow", "workflow-version", "1", "artifact-hash"), node,
            new Dictionary<string, WorkflowExecutableResumeTarget>(), DateTimeOffset.UnixEpoch,
            new Dictionary<string, string>(), IncidentStrategyBuiltIns.FaultReference);
        return (executable, node);
    }
}
