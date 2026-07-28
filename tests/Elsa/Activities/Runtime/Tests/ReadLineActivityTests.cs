using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// Focused unit coverage for the <c>ReadLine</c> leaf activity: a line read from stdin is returned in one
/// atomic result, and an exhausted/closed stream yields a null projection (the documented headless behavior)
/// rather than blocking or faulting.
/// </summary>
// Console.SetIn is process-global; share the capture collection so these don't interleave with the
// Console.Out-capturing tests.
[Collection("ConsoleCapture")]
public sealed class ReadLineActivityTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider = new ServiceCollection().BuildServiceProvider();

    [Fact]
    public async Task Execute_SetsResult_ToLineReadFromInput()
    {
        var result = await RunAsync("hello world\nignored second line");

        Assert.Equal("hello world", result);
    }

    [Fact]
    public async Task Execute_SetsNullResult_WhenInputIsExhausted()
    {
        var result = await RunAsync(string.Empty);

        Assert.Null(result);
    }

    public void Dispose() => _serviceProvider.Dispose();

    private async Task<string?> RunAsync(string stdin)
    {
        var readLine = new ReadLine();
        var context = NewContext(readLine);

        var original = Console.In;
        Console.SetIn(new StringReader(stdin));
        try
        {
            var transition = await ((IActivity)readLine).ExecuteAsync(context.ToActivityExecutionContext());
            return Assert.IsAssignableFrom<IActivityCompletionTransition<ReadLineResult>>(transition).Result.Line;
        }
        finally
        {
            Console.SetIn(original);
        }
    }

    private SimpleActivityExecutionContext NewContext(IActivity activity)
    {
        return new(
            activity,
            CancellationToken.None,
            invocationId: "invocation-read-line",
            executableNodeId: "node-read-line");
    }
}
