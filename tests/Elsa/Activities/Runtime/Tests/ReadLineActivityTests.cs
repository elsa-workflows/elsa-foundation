using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// Focused unit coverage for the <c>ReadLine</c> leaf activity: a line read from stdin is surfaced on the
/// activity <see cref="Elsa.Activities.Runtime.Core.Abstractions.CodeActivity{T}.Result"/>, and an
/// exhausted/closed stream yields a null result (the documented headless behavior) rather than blocking
/// or faulting.
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
        var resultOutput = new OutputArgument<string>(new Variable("Result"));
        var readLine = new ReadLine { Result = resultOutput };
        var context = NewContext(readLine);

        var original = Console.In;
        Console.SetIn(new StringReader(stdin));
        try
        {
            await ((IActivity)readLine).ExecuteAsync(context);
        }
        finally
        {
            Console.SetIn(original);
        }

        return context.Get<string>(resultOutput.MemoryBlockReference());
    }

    private SimpleActivityExecutionContext NewContext(IActivity activity) =>
        new(_serviceProvider, activity, CancellationToken.None);
}
