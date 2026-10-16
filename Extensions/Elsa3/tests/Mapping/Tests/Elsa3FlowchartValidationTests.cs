using System.Text.Json;
using Elsa.Activities.ControlFlow;
using Elsa.Activities.Flowchart;
using Elsa.Activities.Flowchart.Models;
using Elsa.Activities.Sequence;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Validations.Validators;
using Elsa3.Activities.Design.Import.Models;
using Elsa3.Mapping.Tests.Support;
using Elsa3.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Elsa3.Mapping.Tests.Elsa3BuiltInConversionTests;
using static Elsa3.Mapping.Tests.Elsa3FlowchartDiagnosticsTests;
using static Elsa3.Mapping.Tests.Support.Elsa3FlowchartFixtures;

namespace Elsa3.Mapping.Tests;

/// <summary>
/// Spec 239 SC-004: a converted flowchart is a valid Elsa 4 Flowchart. Its payload reads as the authored Flowchart
/// structure, and Elsa 4's design-time value-flow validation, with the real structure handlers, accepts it.
/// </summary>
public sealed class Elsa3FlowchartValidationTests
{
    private const string MultiEntryHalt = "Needs spec 238 (#2850): value-flow validation from every trigger's entry point (H-1).";
    // The options the Flowchart structure handler reads its payload with (FlowchartStructureHandler).
    private static readonly JsonSerializerOptions FlowchartJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private static readonly string[] SingleEntry = Converting.Except(["two-http-triggers", "two-triggers-into-wait-any"]).ToArray();

    public static TheoryData<string> SingleEntryFixtures() => new(SingleEntry);

    [Theory]
    [MemberData(nameof(SingleEntryFixtures))]
    public async Task A_converted_flowchart_is_a_valid_elsa4_flowchart(string fixture)
    {
        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), Load(fixture));
        Assert.True(result.Succeeded, Describe(result.Diagnostics));

        foreach (var flowchart in Flowcharts(result.Value!.State.RootActivity!))
        {
            var payload = flowchart.Structure!.Payload.Deserialize<FlowchartAuthoredStructure>(FlowchartJson)!;
            var ids = payload.Activities.Select(child => child.NodeId).ToHashSet(StringComparer.Ordinal);
            Assert.NotNull(payload.StartNodeId);
            Assert.Contains(payload.StartNodeId, ids);
            Assert.All(payload.Connections, connection =>
            {
                Assert.Contains(connection.Source.NodeId, ids);
                Assert.Contains(connection.Target.NodeId, ids);
            });
        }

        Assert.Empty(await ValidateAsync(result.Value.State));
    }

    [Fact(Skip = MultiEntryHalt)]
    public async Task A_converted_multi_trigger_flowchart_passes_value_flow_validation()
    {
        // The second trigger's handler reads that trigger's result: valid only once validation enters at every trigger.
        var invoices = Activity("invoices", "Elsa.HttpEndpoint", ("path", Literal("/invoices")), ("parsedContent", Captured("invoice-body")));
        invoices.CustomProperties = new Elsa3ActivityCustomProperties { CanStartWorkflow = true };
        var orders = Activity("orders", "Elsa.HttpEndpoint", ("path", Literal("/orders")));
        orders.CustomProperties = new Elsa3ActivityCustomProperties { CanStartWorkflow = true };
        var chart = Flowchart("root", orders, Write("handle-orders"), invoices,
            Activity("handle-invoices", "Elsa.WriteLine", ("text", Captured("invoice-body", "String"))));
        chart.Connections = [Connect("orders", "Done", "handle-orders"), Connect("invoices", "Done", "handle-invoices")];

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(chart));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Empty(await ValidateAsync(result.Value!.State));
    }

    private static async Task<IReadOnlyList<Elsa.Workflows.Design.Validations.Core.Models.ValidationError>> ValidateAsync(WorkflowDefinitionState state)
    {
        var services = new ServiceCollection();
        new ActivitiesSequenceFeature().ConfigureServices(services);
        new ActivitiesControlFlowFeature().ConfigureServices(services);
        new ActivitiesFlowchartFeature().ConfigureServices(services);
        await using var provider = services.BuildServiceProvider();
        var validator = new ValueFlowValidator(new DefaultActivityStructureService(provider.GetServices<IActivityStructureHandler>()));
        return [.. await validator.Validate(new Draft(state), CancellationToken.None)];
    }

    private static IEnumerable<ActivityNode> Flowcharts(ActivityNode root) =>
        Elsa3ImportedActivityNodes.Walk(root).Where(node => node.Structure?.Kind == "elsa.flowchart.structure");

    private sealed class Draft(WorkflowDefinitionState state) : IWorkflowDefinitionDraft
    {
        public string Id => "draft";
        public string WorkflowDefinitionId => "workflow";
        public WorkflowDefinitionState State { get; } = state;
        public DateTimeOffset CreatedAt => DateTimeOffset.UnixEpoch;
        public DateTimeOffset LastModifiedAt => DateTimeOffset.UnixEpoch;
    }
}
