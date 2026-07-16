using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// Focused unit coverage for the <c>WriteLines</c> leaf activity: every element of the bound collection
/// is written on its own line (console-parity with <c>WriteLine</c>), and the null/empty branches write
/// nothing rather than faulting.
/// </summary>
// Console.SetOut is process-global; share the capture collection so these don't interleave with the
// other Console.Out-capturing tests.
[Collection("ConsoleCapture")]
public sealed class WriteLinesActivityTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider = new ServiceCollection().BuildServiceProvider();

    [Fact]
    public async Task Execute_WritesEachLine_InOrder()
    {
        var lines = new List<string> { "first", "second", "third" };
        var output = await RunAsync(lines);

        Assert.Equal(lines, SplitLines(output));
    }

    [Fact]
    public async Task Execute_WritesNothing_WhenCollectionIsEmpty()
    {
        var output = await RunAsync([]);

        Assert.Equal(string.Empty, output);
    }

    [Fact]
    public async Task Execute_WritesNothing_WhenInputIsUnset()
    {
        var writeLines = new WriteLines();
        var context = NewContext(writeLines);

        ActivityTransition? transition = null;
        var output = await ConsoleCapture.RunAsync(async () => transition = await ((IActivity)writeLines).ExecuteAsync(context));

        Assert.Equal(string.Empty, output);
        Assert.Equal(ActivityUnit.Value, Assert.IsAssignableFrom<IActivityCompletionTransition<ActivityUnit>>(transition).Result);
    }

    public void Dispose() => _serviceProvider.Dispose();

    private async Task<string> RunAsync(List<string> lines)
    {
        await using var activation = await TypedActivityTestActivation.ActivateAsync<WriteLines>(
            _serviceProvider,
            new Dictionary<string, object?> { [nameof(WriteLines.Lines)] = lines });
        var writeLines = Assert.IsType<WriteLines>(activation.Activity);
        var context = NewContext(writeLines);
        ActivityTransition? transition = null;

        var output = await ConsoleCapture.RunAsync(async () => transition = await ((IActivity)writeLines).ExecuteAsync(context));
        Assert.Equal(ActivityUnit.Value, Assert.IsAssignableFrom<IActivityCompletionTransition<ActivityUnit>>(transition).Result);
        Assert.Equal(lines, writeLines.Lines);
        return output;
    }

    private static string[] SplitLines(string output) =>
        output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

    private SimpleActivityExecutionContext NewContext(IActivity activity)
    {
        activity.Id = "invocation-write-lines";
        activity.NodeId = "node-write-lines";
        return new(activity, CancellationToken.None);
    }
}
