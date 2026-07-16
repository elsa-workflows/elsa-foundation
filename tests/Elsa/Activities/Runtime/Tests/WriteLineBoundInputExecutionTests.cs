using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

[Collection("ConsoleCapture")]
public sealed class WriteLineBoundInputExecutionTests
{
    [Fact]
    public async Task WriteLine_hydrates_plain_text_without_an_argument_or_memory_wrapper()
    {
        await using var serviceProvider = new ServiceCollection().BuildServiceProvider();
        await using var activation = await TypedActivityTestActivation.ActivateAsync<WriteLine>(
            serviceProvider,
            new Dictionary<string, object?> { ["text"] = "Hello World!" });
        var writeLine = Assert.IsType<WriteLine>(activation.Activity);
        var context = new Elsa.Workflows.Runtime.Core.Services.SimpleActivityExecutionContext(
            writeLine,
            CancellationToken.None);

        var output = await ConsoleCapture.RunAsync(async () => await ((IActivity)writeLine).ExecuteAsync(context));

        Assert.Equal("Hello World!", output.Trim());
        Assert.Equal("Hello World!", writeLine.Text);
    }
}
