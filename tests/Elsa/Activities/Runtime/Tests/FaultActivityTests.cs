using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
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

    /// <summary>
    /// Elsa 3 parity (#1117): the fault can be coded and classified, not just messaged. Code lands on the
    /// returned fault; Category and FaultType ride as classification metadata onto the durable record.
    /// </summary>
    [Fact]
    public async Task Execute_CarriesCodeCategoryAndFaultType()
    {
        var fault = new Fault
        {
            Code = "order.rejected",
            Message = "Payment declined.",
            Category = "Payment",
            FaultType = "Business"
        };

        var returnedFault = await ExecuteAsync(fault);

        Assert.Equal("order.rejected", returnedFault.Code);
        Assert.Equal("Payment declined.", returnedFault.Message);
        Assert.Equal("Payment", returnedFault.Category);
        Assert.Equal("Business", returnedFault.FaultType);

        var normalized = returnedFault.ToNormalized();
        Assert.Equal("order.rejected", normalized.Code);
        Assert.Equal("Payment", normalized.Metadata[ActivityFault.CategoryMetadataKey]);
        Assert.Equal("Business", normalized.Metadata[ActivityFault.FaultTypeMetadataKey]);
    }

    /// <summary>An unclassified fault persists exactly the record shape it did before classification existed.</summary>
    [Fact]
    public async Task Execute_WithoutClassification_ProducesNoFaultMetadata()
    {
        var returnedFault = await ExecuteAsync(new Fault());

        Assert.Equal(Fault.DefaultCode, returnedFault.Code);
        Assert.Null(returnedFault.Category);
        Assert.Null(returnedFault.FaultType);
        Assert.Empty(returnedFault.ToNormalized().Metadata);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Execute_TreatsBlankClassificationAsUnset(string blank)
    {
        var returnedFault = await ExecuteAsync(new Fault { Code = blank, Category = blank, FaultType = blank });

        Assert.Equal(Fault.DefaultCode, returnedFault.Code);
        Assert.Null(returnedFault.Category);
        Assert.Null(returnedFault.FaultType);
    }

    public void Dispose() => _serviceProvider.Dispose();

    private async Task<ActivityFault> ExecuteAsync(Fault fault)
    {
        var transition = await ((IActivity)fault).ExecuteAsync(NewContext(fault).ToActivityExecutionContext());
        return Assert.IsAssignableFrom<IActivityFaultTransition>(transition).Fault;
    }

    private SimpleActivityExecutionContext NewContext(IActivity activity)
    {
        return new SimpleActivityExecutionContext(
            activity,
            CancellationToken.None,
            invocationId: "invocation-1",
            executableNodeId: "node-1");
    }
}
