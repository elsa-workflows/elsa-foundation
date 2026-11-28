using System.Text.Json;
using Elsa.Activities.DispatchWorkflow.Design;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Design.Tests;

/// <summary>
/// BulkDispatchWorkflows runs its Dispatch node and its two slots concurrently across item passes, so a result binding
/// between two of its children cannot tell which item's run it reads and is refused (spec 1242 FR-010). Bindings inside
/// one slot keep their own rules.
/// </summary>
public sealed partial class ActivityDataSelectionAnalyzerTests
{
    [Theory]
    [InlineData("dispatch", "childCompleted", false, "ValueFlow/UnavailableProducer")]
    [InlineData("dispatch", "childCompleted", true, "ValueFlow/EarlierProducerRun")]
    [InlineData("childFaulted", "childCompleted", false, "ValueFlow/UnavailableProducer")]
    [InlineData("childFaulted", "childCompleted", true, "ValueFlow/EarlierProducerRun")]
    [InlineData("childCompleted", "dispatch", true, "ValueFlow/EarlierProducerRun")]
    public async Task A_scope_free_result_binding_between_two_bulk_dispatch_children_is_refused(
        string producerSlot, string consumerSlot, bool optional, string expected)
    {
        using var provider = BulkProvider();
        var slots = new Dictionary<string, ActivityNode>
        {
            [producerSlot] = Node("producer"),
            [consumerSlot] = Node("consumer", Input(ScopeFreeResult("producer", optional: optional)))
        };

        var result = await Analyzer(provider).AnalyzeAsync(Document(Bulk(slots)));

        Assert.Equal(new[] { expected }, result.Findings.Select(finding => finding.Type));
        Assert.Equal(new[] { "consumer/inputs/CustomerId" }, result.Findings.Select(finding => finding.Path));
    }

    [Fact]
    public async Task A_producer_nested_in_one_slot_is_out_of_scope_for_another_slot()
    {
        using var provider = BulkProvider();
        var inner = Sequence(Node("producer")) with { NodeId = "inner" };

        var result = await Analyzer(provider).AnalyzeAsync(Document(Bulk(new()
        {
            ["childFaulted"] = inner,
            ["childCompleted"] = Node("consumer", Input(ScopeFreeResult("producer", optional: true)))
        })));

        Assert.Contains(result.Findings, finding => finding.Type == "ValueFlow/ScopeBoundary");
    }

    [Fact]
    public async Task A_result_binding_between_two_activities_inside_one_slot_is_admitted()
    {
        using var provider = BulkProvider();
        var steps = Sequence(Node("producer"), Node("consumer", Input(ScopeFreeResult("producer")))) with { NodeId = "steps" };

        var result = await Analyzer(provider).AnalyzeAsync(Document(Bulk(new() { ["childCompleted"] = steps })));

        Assert.Empty(result.Findings);
    }

    private static ActivityNode Bulk(Dictionary<string, ActivityNode> slots) =>
        new("bulk", "bulk", [], [], new(BulkDispatchWorkflowsConstants.StructureKind, BulkDispatchWorkflowsConstants.StructureSchemaVersion,
            JsonSerializer.SerializeToElement(slots, Web)));

    private static ServiceProvider BulkProvider() => ActivityDataSelectionTestSupport.Provider(Catalog().Add("bulk"), configure: services =>
    {
        // Only the structure handler is needed; the feature's other services need stores these tests do not have.
        var feature = new ServiceCollection();
        new DispatchWorkflowDesignFeature().ConfigureServices(feature);
        foreach (var handler in feature.Where(descriptor => descriptor.ServiceType == typeof(IActivityStructureHandler)))
            services.Add(handler);
    });
}
