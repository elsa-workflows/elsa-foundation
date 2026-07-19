using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed class TypedActivityAuthoringContractTests
{
    [Fact]
    public void Activity_contract_exposes_only_atomic_execution()
    {
        var method = Assert.Single(typeof(IActivity).GetMethods());

        Assert.Equal(nameof(IActivity.ExecuteAsync), method.Name);
        Assert.Equal(typeof(ValueTask<ActivityTransition>), method.ReturnType);
        Assert.Equal([typeof(ActivityExecutionContext)], method.GetParameters().Select(x => x.ParameterType));
        Assert.Empty(typeof(IActivity).GetProperties());
    }

    [Fact]
    public void Activity_contract_has_no_legacy_no_op_authoring_base()
    {
        var assembly = typeof(IActivity).Assembly;

        Assert.Null(assembly.GetType("Elsa.Activities.Runtime.Core.Abstractions.ActivityBase"));
        Assert.Null(assembly.GetType("Elsa.Activities.Runtime.Core.Abstractions.CodeActivity"));
    }

    [Fact]
    public async Task Structural_activity_rejects_the_ordinary_activity_path()
    {
        var activity = new TestStructuralActivity();
        var context = new ActivityExecutionContext("workflow-1", "invocation-1", "attempt-1", "node-1", default);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await ((IActivity)activity).ExecuteAsync(context));

        Assert.Contains("runtime structural protocol", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Typed_activity_receives_only_identity_and_cancellation_and_returns_atomic_result()
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        using var cancellation = new CancellationTokenSource();
        var activity = new GreetingActivity { Recipient = "Ada" };
        var runtimeContext = new ActivityExecutionContext(
            workflowExecutionId: "workflow-1",
            invocationId: "invocation-1",
            attemptId: "attempt-1",
            executableNodeId: "node-1",
            cancellation.Token);

        var transition = await ((IActivity)activity).ExecuteAsync(runtimeContext);
        var completion = Assert.IsAssignableFrom<IActivityCompletionTransition<Greeting>>(transition);

        Assert.Equal(new Greeting("Hello Ada"), completion.Result);
        Assert.Equal("Greeted", completion.Outcome);
        Assert.Equal("invocation-1", activity.ObservedContext!.InvocationId);
        Assert.Equal("node-1", activity.ObservedContext.ExecutableNodeId);
        Assert.Equal(cancellation.Token, activity.ObservedContext.CancellationToken);
    }

    private sealed class GreetingActivity : Activity<Greeting>
    {
        public string Recipient { get; init; } = null!;
        public ActivityExecutionContext? ObservedContext { get; private set; }

        protected override ValueTask<ActivityTransition<Greeting>> ExecuteAsync(ActivityExecutionContext context)
        {
            ObservedContext = context;
            return ValueTask.FromResult(ActivityTransition.Complete(new Greeting($"Hello {Recipient}"), "Greeted"));
        }
    }

    private sealed record Greeting(string Message);

    private sealed class TestStructuralActivity : Elsa.Activities.Runtime.Core.Abstractions.StructuralActivity;
}
