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
/// Focused unit coverage (§2.23) for the data leaves <c>SetName</c> and <c>SetOutput</c>: each records its
/// intent through <see cref="IRuntimeActivityExecutionContext"/> and throws when executed outside a runtime
/// context. End-to-end engine-drain assertions live in <see cref="SetDataLeafExecutionTests"/>; durability of
/// <c>SetVariable</c> over the keystone write-back lives in <see cref="SetVariableDurabilityExecutionTests"/>.
/// </summary>
public sealed class SetDataLeafActivityTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider = new ServiceCollection().BuildServiceProvider();

    [Fact]
    public async Task SetName_RequestsInstanceName_FromBoundInput()
    {
        var nameInput = new InputArgument<string>(new Variable("Name", "order-flow"));
        var setName = new SetName { Name = nameInput };
        var context = NewContext(setName);
        context.Set(nameInput.MemoryBlockReference(), "order-flow");

        await ((IActivity)setName).ExecuteAsync(context);

        Assert.True(context.InstanceNameAssignmentRequested);
        Assert.Equal("order-flow", context.RequestedInstanceName);
    }

    [Fact]
    public async Task SetName_RequestsInstanceNameClear_WhenInputIsUnset()
    {
        var setName = new SetName();
        var context = NewContext(setName);

        await ((IActivity)setName).ExecuteAsync(context);

        Assert.True(context.InstanceNameAssignmentRequested);
        Assert.Null(context.RequestedInstanceName);
    }

    [Fact]
    public async Task SetName_Throws_WhenContextIsNotRuntimeContext()
    {
        var setName = new SetName();
        var context = new NonRuntimeActivityExecutionContext();

        await Assert.ThrowsAsync<SetNameActivityException>(() => ((IActivity)setName).ExecuteAsync(context).AsTask());
    }

    [Fact]
    public async Task SetOutput_RequestsWorkflowOutput_FromBoundInputs()
    {
        var nameInput = new InputArgument<string>(new Variable("OutputName", "Result"));
        var valueInput = new InputArgument<object>(new Variable("OutputValue", "ok"));
        var setOutput = new SetOutput { OutputName = nameInput, OutputValue = valueInput };
        var context = NewContext(setOutput);
        context.Set(nameInput.MemoryBlockReference(), "Result");
        context.Set(valueInput.MemoryBlockReference(), "ok");

        await ((IActivity)setOutput).ExecuteAsync(context);

        Assert.True(context.WorkflowOutputAssignmentRequested);
        Assert.Equal("ok", Assert.Contains("Result", context.RequestedWorkflowOutputs));
    }

    [Fact]
    public async Task SetOutput_IsNoOp_WhenNameIsBlank()
    {
        var setOutput = new SetOutput();
        var context = NewContext(setOutput);

        await ((IActivity)setOutput).ExecuteAsync(context);

        Assert.False(context.WorkflowOutputAssignmentRequested);
        Assert.Empty(context.RequestedWorkflowOutputs);
    }

    [Fact]
    public async Task SetOutput_Throws_WhenContextIsNotRuntimeContext()
    {
        var setOutput = new SetOutput();
        var context = new NonRuntimeActivityExecutionContext();

        await Assert.ThrowsAsync<SetOutputActivityException>(() => ((IActivity)setOutput).ExecuteAsync(context).AsTask());
    }

    public void Dispose() => _serviceProvider.Dispose();

    private SimpleActivityExecutionContext NewContext(IActivity activity) =>
        new(_serviceProvider, activity, CancellationToken.None);
}
