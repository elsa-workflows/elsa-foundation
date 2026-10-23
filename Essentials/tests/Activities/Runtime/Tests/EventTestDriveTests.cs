using System.Text.Json;
using Elsa.Activities.Primitives;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed class EventTestDriveTests : IDisposable
{
    private readonly ServiceProvider _provider;

    public EventTestDriveTests()
    {
        var services = new ServiceCollection();
        new ActivitiesPrimitivesFeature().ConfigureServices(services);
        _provider = services.BuildServiceProvider();
    }

    public void Dispose() => _provider.Dispose();

    [Fact]
    public async Task WaitDriver_MatchesTheNodeScopedResumeTarget()
    {
        var driver = Assert.Single(_provider.GetServices<ITestRunWaitDriver>());
        var trigger = TestRunTriggerFixture.Create("Elsa.Event", "EventName", "ApprovalReceived");
        var wait = new BookmarkState("wait", "execution", "activity", trigger.Node.ExecutableNodeId,
            WorkflowExecutableResumeTarget.ComposeScopedId(trigger.Node.ExecutableNodeId, "resume-target:event"),
            "Event", "hash", null, new Dictionary<string, string>(), TestRunTriggerFixture.Now, null);
        var context = new TestRunWaitContext(trigger.Executable, trigger.Node, wait);

        Assert.True(driver.Supports(context));
        Assert.False(driver.Supports(context with { Wait = wait with { ResumeTargetId = "another-node:resume-target:event" } }));
        var payload = JsonSerializer.SerializeToElement(new { payload = new { approved = true } });
        Assert.True(driver.Validate(context, "event", payload).IsValid);
        var stimulus = await driver.PrepareAsync(new(context, "event", payload));
        Assert.True(stimulus!.Value.GetProperty("approved").GetBoolean());
    }

    [Fact]
    public async Task ModuleDriver_DescribesFixedEvent_AndReturnsSeparateStimulus()
    {
        var driver = Assert.Single(_provider.GetServices<ITestRunTriggerDriver>());
        var trigger = TestRunTriggerFixture.Create("Elsa.Event", "EventName", "OrderImported");
        var mode = Assert.Single(driver.Describe(trigger));
        Assert.Equal("OrderImported", mode.Configuration!.Value.GetProperty("eventName").GetString());
        var data = JsonSerializer.SerializeToElement(new { payload = new { orderId = "order-42" } });
        Assert.True(driver.Validate(trigger, "event", data).IsValid);
        var preparation = await driver.PrepareAsync(TestRunTriggerFixture.Prepare(trigger, "event", data));
        Assert.Equal(TestRunPreparationKind.Immediate, preparation.Kind);
        Assert.Equal("order-42", preparation.StimulusInput!.Value.GetProperty("orderId").GetString());
        Assert.False(driver.Validate(trigger, "unknown", data).IsValid);
    }

    /// <summary>A delivery with no payload or a scalar one becomes the stimulus input, which the Event's trigger accepts (#3107).</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("""{"payload":"text"}""")]
    public async Task WaitDriver_DeliversInputTheEventTriggerAccepts(string? dataJson)
    {
        var driver = Assert.Single(_provider.GetServices<ITestRunWaitDriver>());
        var trigger = TestRunTriggerFixture.Create("Elsa.Event", "EventName", "ApprovalReceived");
        var wait = new BookmarkState("wait", "execution", "activity", trigger.Node.ExecutableNodeId,
            WorkflowExecutableResumeTarget.ComposeScopedId(trigger.Node.ExecutableNodeId, "resume-target:event"),
            "Event", "hash", null, new Dictionary<string, string>(), TestRunTriggerFixture.Now, null);
        var context = new TestRunWaitContext(trigger.Executable, trigger.Node, wait);
        var data = dataJson is null ? (JsonElement?)null : JsonDocument.Parse(dataJson).RootElement.Clone();

        var stimulus = await driver.PrepareAsync(new(context, "event", data));

        var input = stimulus ?? JsonSerializer.SerializeToElement<object?>(null);
        var received = input.Deserialize<EventReceived>();
        Assert.NotNull(received);
        Assert.Equal(stimulus?.GetRawText(), received.Payload?.GetRawText());
    }
}
