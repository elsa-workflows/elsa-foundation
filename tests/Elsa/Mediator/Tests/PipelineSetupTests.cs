using Elsa.Mediator.Commands;
using Elsa.Mediator.Core.Contracts;
using Elsa.Mediator.Core.Models;
using Elsa.Pipelines.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Mediator.Tests;

/// <summary>
/// Pins the one documented pipeline Setup() semantic: REPLACE. Each Setup() call composes a fresh
/// pipeline from a new builder, discarding whatever was composed before.
/// </summary>
public class PipelineSetupTests
{
    [Fact]
    public async Task Setup_Replaces_PreviousComposition()
    {
        await using var provider = new ServiceCollection().BuildServiceProvider();
        var pipeline = new CommandPipeline(provider);

        var first = 0;
        var second = 0;

        pipeline.Setup(builder => builder.Use(next => ctx =>
        {
            first++;
            return next(ctx);
        }));

        // Second Setup must fully replace the first composition, not accumulate onto it.
        pipeline.Setup(builder => builder.Use(next => ctx =>
        {
            second++;
            return next(ctx);
        }));

        var context = new MessageContext<Unit>(new object(), typeof(ICommandHandler<,>), "command", provider, CancellationToken.None);
        await pipeline.Execute(context);

        Assert.Equal(0, first);
        Assert.Equal(1, second);
    }
}
