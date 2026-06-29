using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Primitives.Exceptions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// Focused unit coverage for the <c>Finish</c> leaf control activity. <c>Finish</c> requests that the whole
/// workflow run end early by calling <see cref="IRuntimeActivityExecutionContext.FinishWorkflow"/>; the
/// runtime engine drains the request and commits a terminal checkpoint (see <see cref="FinishCorrelateExecutionTests"/>
/// for the end-to-end assertion).
/// </summary>
public sealed class FinishActivityTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider = new ServiceCollection().BuildServiceProvider();

    [Fact]
    public async Task Execute_RequestsWorkflowCompletion_WithConfiguredOutcome()
    {
        var outcomeInput = new InputArgument<string>(new Variable("OutcomeName", "Aborted"));
        var finish = new Finish { OutcomeName = outcomeInput };
        var context = NewContext(finish);
        context.Set(outcomeInput.MemoryBlockReference(), "Aborted");

        await ((IActivity)finish).ExecuteAsync(context);

        Assert.True(context.FinishWorkflowRequested);
        Assert.Equal(["Aborted"], context.FinishWorkflowOutcomeNames);
    }

    [Fact]
    public async Task Execute_RequestsWorkflowCompletion_WithDoneOutcome_WhenOutcomeIsUnset()
    {
        var finish = new Finish();
        var context = NewContext(finish);

        await ((IActivity)finish).ExecuteAsync(context);

        Assert.True(context.FinishWorkflowRequested);
        Assert.Equal([ActivityOutcomes.Done], context.FinishWorkflowOutcomeNames);
    }

    [Fact]
    public async Task Execute_Throws_WhenContextIsNotRuntimeContext()
    {
        var finish = new Finish();
        var context = new NonRuntimeActivityExecutionContext();

        await Assert.ThrowsAsync<FinishActivityException>(() => ((IActivity)finish).ExecuteAsync(context).AsTask());
    }

    public void Dispose() => _serviceProvider.Dispose();

    private SimpleActivityExecutionContext NewContext(IActivity activity) =>
        new(_serviceProvider, activity, CancellationToken.None);
}
