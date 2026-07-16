using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// Focused unit coverage for the <c>Inline</c> leaf activity. Inline rides the standard activity-input
/// expression path: the runtime evaluates and pins its <c>Expression</c> input before activation, so the
/// activity's job is to return that hydrated value atomically. These tests assert the value flows through
/// unchanged and that an unset input yields a null result.
/// </summary>
public sealed class InlineActivityTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider = new ServiceCollection().BuildServiceProvider();

    [Fact]
    public async Task Execute_SurfacesSeededExpressionValue_OnResult()
    {
        var result = await RunWithExpressionAsync(42);

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task Execute_SurfacesNull_WhenExpressionInputIsUnset()
    {
        var transition = await ExecuteAsync(new Inline());

        Assert.Null(transition.Result);
    }

    public void Dispose() => _serviceProvider.Dispose();

    private async Task<object?> RunWithExpressionAsync(object value)
    {
        var transition = await ExecuteAsync(new Inline { Expression = value });
        return transition.Result;
    }

    private async Task<IActivityCompletionTransition<object?>> ExecuteAsync(Inline inline)
    {
        var transition = await ((IActivity)inline).ExecuteAsync(NewContext(inline));
        return Assert.IsAssignableFrom<IActivityCompletionTransition<object?>>(transition);
    }

    private SimpleActivityExecutionContext NewContext(IActivity activity)
    {
        activity.Id = "invocation-1";
        activity.NodeId = "node-inline";
        return new SimpleActivityExecutionContext(activity, CancellationToken.None);
    }
}
