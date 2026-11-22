using System.Text.Json;
using Elsa.Activities.Sequence.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Activities.Sequence.Tests;

/// <summary>Spec 266 (FR-008): a Sequence runs its children in authored order, one at a time.</summary>
public sealed class SequenceChildTimeRelationTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IActivityStructureService _structures;

    public SequenceChildTimeRelationTests()
    {
        var services = new ServiceCollection();
        new ActivitiesSequenceFeature().ConfigureServices(services);
        services.AddSingleton<IActivityStructureService, DefaultActivityStructureService>();
        _provider = services.BuildServiceProvider();
        _structures = _provider.GetRequiredService<IActivityStructureService>();
    }

    public void Dispose() => _provider.Dispose();

    [Theory]
    [InlineData("first", "last", ActivityChildTimeRelation.FirstBeforeSecond)]
    [InlineData("last", "first", ActivityChildTimeRelation.SecondBeforeFirst)]
    [InlineData("middle", "middle", ActivityChildTimeRelation.FirstBeforeSecond)]
    [InlineData("first", "unknown", ActivityChildTimeRelation.Unspecified)]
    public void Children_relate_in_authored_order(string first, string second, ActivityChildTimeRelation expected)
    {
        var sequence = new ActivityNode("sequence", "sequence@1", [], [], new ActivityNodeStructure(
            SequenceActivity.StructureKind,
            SequenceActivity.StructureSchemaVersion,
            JsonSerializer.SerializeToElement(new SequenceAuthoredStructure(
                new[] { "first", "middle", "last" }.Select(id => new ActivityNode(id, "test@1", [], [])).ToArray()))));

        Assert.Equal(expected, _structures.RelateChildren(sequence, first, second));
        Assert.True(_structures.CompletesAfterItsChildren(sequence));
    }
}
