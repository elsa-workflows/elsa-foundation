using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Primitives.Exceptions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// Focused unit coverage for the <c>Correlate</c> leaf control activity. <c>Correlate</c> records the new
/// correlation id through <see cref="IRuntimeActivityExecutionContext.SetCorrelationId"/>; the runtime engine
/// drains the request and persists it on the workflow instance (see <see cref="FinishCorrelateExecutionTests"/>
/// for the end-to-end assertion).
/// </summary>
public sealed class CorrelateActivityTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider = new ServiceCollection().BuildServiceProvider();

    [Fact]
    public async Task Execute_RequestsCorrelationId_FromBoundInput()
    {
        var correlationInput = new InputArgument<string>(new Variable("CorrelationId", "order-42"));
        var correlate = new Correlate { CorrelationId = correlationInput };
        var context = NewContext(correlate);
        context.Set(correlationInput.MemoryBlockReference(), "order-42");

        await ((IActivity)correlate).ExecuteAsync(context);

        Assert.True(context.CorrelationIdAssignmentRequested);
        Assert.Equal("order-42", context.RequestedCorrelationId);
    }

    [Fact]
    public async Task Execute_RequestsCorrelationClear_WhenInputIsUnset()
    {
        var correlate = new Correlate();
        var context = NewContext(correlate);

        await ((IActivity)correlate).ExecuteAsync(context);

        Assert.True(context.CorrelationIdAssignmentRequested);
        Assert.Null(context.RequestedCorrelationId);
    }

    [Fact]
    public async Task Execute_Throws_WhenContextIsNotRuntimeContext()
    {
        var correlate = new Correlate();
        var context = new NonRuntimeActivityExecutionContext();

        await Assert.ThrowsAsync<CorrelateActivityException>(() => ((IActivity)correlate).ExecuteAsync(context).AsTask());
    }

    public void Dispose() => _serviceProvider.Dispose();

    private SimpleActivityExecutionContext NewContext(IActivity activity) =>
        new(_serviceProvider, activity, CancellationToken.None);
}
