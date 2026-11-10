using System.Text.Json;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Tests.Infrastructure;
using Xunit;

namespace Elsa.Workflows.Design.Tests;

public sealed class ActivityChildResultFactsTests
{
    [Fact]
    public void A_legacy_handler_without_order_facts_remains_unknown()
    {
        var structure = TestActivityStructureHandler.CreateStructure([
            new ActivityNode("producer", "test@1", [], []),
            new ActivityNode("consumer", "test@1", [], [])]);
        var service = new DefaultActivityStructureService([new TestActivityStructureHandler()]);

        var facts = service.AssessChildResult(new ActivityNode("root", "container@1", [], [], structure), "producer", "consumer");

        Assert.Equal(ActivityChildResultAvailability.Unknown, facts.Availability);
        Assert.Null(facts.IsCyclicBackEdge);
        Assert.Null(facts.MayRunConcurrently);
        Assert.Null(facts.AreMutuallyExclusive);
    }

    [Fact]
    public void An_omitted_structure_module_supplies_unknown_facts()
    {
        var node = new ActivityNode("container", "custom@1", [], [],
            new ActivityNodeStructure("custom.structure", "1.0.0", JsonSerializer.SerializeToElement(new { })));
        var service = new DefaultActivityStructureService([]);

        Assert.Equal(ActivityChildResultAvailability.Unknown, service.AssessChildResult(node, "producer", "consumer").Availability);
    }
}
