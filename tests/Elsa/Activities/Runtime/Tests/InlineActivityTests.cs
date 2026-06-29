using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// Focused unit coverage for the <c>Inline</c> leaf activity. Inline rides the standard activity-input
/// expression path: the runtime evaluates and seeds its <c>Expression</c> input before execution, so the
/// activity's job is to surface that already-evaluated value on its
/// <see cref="Elsa.Activities.Runtime.Core.Abstractions.CodeActivity{T}.Result"/>. These tests assert the
/// seeded value flows through unchanged, and that an unset input yields a null result.
/// </summary>
public sealed class InlineActivityTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider = new ServiceCollection().BuildServiceProvider();

    [Fact]
    public async Task Execute_SurfacesSeededExpressionValue_OnResult()
    {
        var result = await RunWithSeededExpressionAsync(42);

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task Execute_SurfacesNull_WhenExpressionInputIsUnset()
    {
        var resultOutput = new OutputArgument<object>(new Variable("Result"));
        var inline = new Inline { Result = resultOutput };
        var context = NewContext(inline);

        await ((IActivity)inline).ExecuteAsync(context);

        Assert.Null(context.Get<object>(resultOutput.MemoryBlockReference()));
    }

    public void Dispose() => _serviceProvider.Dispose();

    private async Task<object?> RunWithSeededExpressionAsync(object value)
    {
        var input = new InputArgument<object>(new Variable("Expression", value));
        var resultOutput = new OutputArgument<object>(new Variable("Result"));
        var inline = new Inline { Expression = input, Result = resultOutput };
        var context = NewContext(inline);
        context.Set(input.MemoryBlockReference(), value);

        await ((IActivity)inline).ExecuteAsync(context);

        return context.Get<object>(resultOutput.MemoryBlockReference());
    }

    private SimpleActivityExecutionContext NewContext(IActivity activity) =>
        new(_serviceProvider, activity, CancellationToken.None);
}
