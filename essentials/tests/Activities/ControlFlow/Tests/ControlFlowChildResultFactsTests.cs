using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.ControlFlow.Tests;

/// <summary>
/// If, Switch, Parallel and the loops schedule each of their children themselves, at most once per run or once per
/// pass, so no earlier run of a child is among the runs that led to another. They promise no order, so availability
/// stays unknown.
/// </summary>
public sealed class ControlFlowChildResultFactsTests
{
    [Fact]
    public void Every_control_flow_structure_states_that_no_earlier_run_of_a_child_leads_to_a_reader()
    {
        var services = new ServiceCollection();
        new ActivitiesControlFlowFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        var handlers = provider.GetServices<IActivityStructureHandler>().ToArray();
        Assert.Equal(8, handlers.Length);

        foreach (var handler in handlers)
        {
            var node = new ActivityNode("container", "container@1", [], [], null);
            foreach (var (producer, consumer) in new[] { ("first", "second"), ("body", "body") })
            {
                var facts = handler.AssessChildResult(node, producer, consumer);
                Assert.Equal(new ActivityChildResultFacts(ActivityChildResultAvailability.Unknown) { EarlierProducerRunCanReachConsumer = false }, facts);
            }
        }
    }
}
