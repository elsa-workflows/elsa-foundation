using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using ForEachActivity = Elsa.Activities.ForEach.Activities.ForEach;

namespace Elsa.Activities.ForEach.Tests;

// RoutingStructureMaterializationDiagnostics is a process-wide counter, so this class runs alone.
[Collection(Name)]
public sealed class ForEachRoutingStructureMemoTests
{
    public const string Name = nameof(ForEachRoutingStructureMemoTests);

    [Fact]
    public async Task OnChildCompleted_BuildsLoopNavigatorOnce_AcrossIterations()
    {
        const int itemCount = 50;
        var context = ForEachActivityTests.NewContext(Enumerable.Range(0, itemCount).Select(i => $"item-{i}").ToArray());
        var activity = (ForEachActivity)context.Activity;
        RoutingStructureMaterializationDiagnostics.Reset();

        for (var iterationIndex = 0; iterationIndex < itemCount; iterationIndex++)
            await activity.OnChildCompletedAsync(ForEachActivityTests.NewCompletion(context, iterationIndex));

        Assert.Equal(1, RoutingStructureMaterializationDiagnostics.Count);
    }
}

[CollectionDefinition(ForEachRoutingStructureMemoTests.Name, DisableParallelization = true)]
public sealed class ForEachRoutingStructureMemoCollection;
