using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Primitives.Exceptions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// Focused unit coverage for the <c>Fault</c> leaf activity. <c>Fault</c> signals a deliberate fault by
/// raising <see cref="FaultActivityException"/>; the runtime engine converts that into a recorded
/// incident (see <see cref="FaultIncidentExecutionTests"/> for the end-to-end incident assertion).
/// </summary>
public sealed class FaultActivityTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider = new ServiceCollection().BuildServiceProvider();

    [Fact]
    public async Task Execute_ThrowsFaultActivityException_WithConfiguredMessage()
    {
        var messageInput = new InputArgument<string>(new Variable("Message", "custom fault"));
        var fault = new Fault { Message = messageInput };
        var context = NewContext(fault);
        context.Set(messageInput.MemoryBlockReference(), "custom fault");

        var exception = await Assert.ThrowsAsync<FaultActivityException>(() => ((IActivity)fault).ExecuteAsync(context).AsTask());
        Assert.Equal("custom fault", exception.Message);
    }

    [Fact]
    public async Task Execute_ThrowsFaultActivityException_WithDefaultMessage_WhenMessageIsUnset()
    {
        var fault = new Fault();
        var context = NewContext(fault);

        var exception = await Assert.ThrowsAsync<FaultActivityException>(() => ((IActivity)fault).ExecuteAsync(context).AsTask());
        Assert.Equal("The workflow faulted.", exception.Message);
    }

    public void Dispose() => _serviceProvider.Dispose();

    private SimpleActivityExecutionContext NewContext(IActivity activity) =>
        new(_serviceProvider, activity, CancellationToken.None);
}
