using System.Text.Json;
using Elsa.Activities.Sequence.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Activities.Sequence.Tests;

public sealed class SequenceChildResultFactsTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IActivityStructureService _structures;

    public SequenceChildResultFactsTests()
    {
        var services = new ServiceCollection();
        new ActivitiesSequenceFeature().ConfigureServices(services);
        services.AddSingleton<IActivityStructureService, DefaultActivityStructureService>();
        _provider = services.BuildServiceProvider();
        _structures = _provider.GetRequiredService<IActivityStructureService>();
    }

    public void Dispose() => _provider.Dispose();

    [Theory]
    [InlineData("first", "last", ActivityChildResultAvailability.Definite)]
    [InlineData("last", "first", ActivityChildResultAvailability.Unavailable)]
    [InlineData("first", "first", ActivityChildResultAvailability.Unavailable)]
    public void Result_availability_follows_authored_child_order(string producer, string consumer, ActivityChildResultAvailability expected)
    {
        var facts = _structures.AssessChildResult(Sequence("first", "middle", "last"), producer, consumer);

        Assert.Equal(expected, facts.Availability);
        Assert.False(facts.IsCyclicBackEdge);
        Assert.False(facts.MayRunConcurrently);
        Assert.False(facts.AreMutuallyExclusive);
        // A Sequence runs each child once per run, so a result binding that names only the producer is unambiguous.
        Assert.False(facts.EarlierProducerRunCanReachConsumer);
    }

    [Fact]
    public void Prior_results_are_ranked_nearest_first()
    {
        var sequence = Sequence("first", "middle", "last");

        Assert.Equal(["middle", "first"], _structures.ChildrenCompletedBefore(sequence, "last"));
        Assert.Empty(_structures.ChildrenCompletedBefore(sequence, "first")!);
        Assert.Null(_structures.ChildrenCompletedBefore(sequence, "missing"));
    }

    [Theory]
    [InlineData("missing", "last")]
    [InlineData("first", "missing")]
    [InlineData("FIRST", "last")]
    public void An_unknown_direct_child_cannot_supply_a_result(string producer, string consumer)
    {
        var facts = _structures.AssessChildResult(Sequence("first", "last"), producer, consumer);

        Assert.Equal(ActivityChildResultAvailability.Unavailable, facts.Availability);
        Assert.Null(facts.MayRunConcurrently);
    }

    private static ActivityNode Sequence(params string[] children) =>
        new("sequence", "sequence@1", [], [], new ActivityNodeStructure(
            SequenceActivity.StructureKind,
            SequenceActivity.StructureSchemaVersion,
            JsonSerializer.SerializeToElement(new SequenceAuthoredStructure(
                children.Select(id => new ActivityNode(id, "test@1", [], [])).ToArray()))));
}
