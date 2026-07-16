using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed class TypedActivityAuthoringContractTests
{
    [Fact]
    public async Task Typed_activity_receives_only_identity_and_cancellation_and_returns_atomic_result()
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        using var cancellation = new CancellationTokenSource();
        var activity = new GreetingActivity { Id = "invocation-1", NodeId = "node-1", Recipient = "Ada" };
        var legacyContext = new SimpleActivityExecutionContext(services, activity, cancellation.Token);

        var transition = await ((IActivity)activity).ExecuteAsync(legacyContext);
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
}
