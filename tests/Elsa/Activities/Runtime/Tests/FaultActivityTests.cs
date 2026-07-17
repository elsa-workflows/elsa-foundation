using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// Focused unit coverage for the <c>Fault</c> leaf activity's deliberate returned transition.
/// </summary>
public sealed class FaultActivityTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider = new ServiceCollection().BuildServiceProvider();

    [Fact]
    public async Task Execute_ReturnsTypedFault_WithConfiguredMessage()
    {
        var fault = new Fault { Message = "custom fault" };
        var context = NewContext(fault);

        var transition = await ((IActivity)fault).ExecuteAsync(context.ToActivityExecutionContext());

        var returnedFault = Assert.IsAssignableFrom<IActivityFaultTransition>(transition).Fault;
        Assert.Equal("workflow.fault", returnedFault.Code);
        Assert.Equal("custom fault", returnedFault.Message);
    }

    [Fact]
    public async Task Execute_ReturnsTypedFault_WithDefaultMessage_WhenMessageIsUnset()
    {
        var fault = new Fault();
        var context = NewContext(fault);

        var transition = await ((IActivity)fault).ExecuteAsync(context.ToActivityExecutionContext());

        var returnedFault = Assert.IsAssignableFrom<IActivityFaultTransition>(transition).Fault;
        Assert.Equal("The workflow faulted.", returnedFault.Message);
    }

    public void Dispose() => _serviceProvider.Dispose();

    private SimpleActivityExecutionContext NewContext(IActivity activity)
    {
        return new SimpleActivityExecutionContext(
            activity,
            CancellationToken.None,
            invocationId: "invocation-1",
            executableNodeId: "node-1");
    }
}
