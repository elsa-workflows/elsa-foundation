using Xunit;

namespace Elsa.Activities.Bpmn.Hosting.Tests;

public sealed class OwnedWorkbenchTests
{
    [Fact]
    public async Task Actual_native_execution_and_pin_survive_an_owned_host_restart()
    {
        await using var host = await OwnedWorkbench.StartAsync();
        await host.LoginAsync();
        var catalog = await host.GetAsync("/design/activities/definitions?search=Elsa.Activities.Primitives.Activities.WriteLine");
        var activity = Assert.Single(catalog["items"]!.AsArray(), item =>
            item!["definition"]!["activityTypeKey"]!.GetValue<string>() == "Elsa.Activities.Primitives.Activities.WriteLine");
        Assert.Equal("Elsa.Activities.Primitives.Activities.WriteLine", activity!["definition"]!["activityTypeKey"]!.GetValue<string>());
        var definition = await host.PostAsync("/design/workflows/definitions/submit", new
        {
            name = "Owned BPMN hosting native baseline",
            state = new { variables = Array.Empty<object>(), inputs = Array.Empty<object>(), outputs = Array.Empty<object>(),
                rootActivity = new { nodeId = "native-baseline", activityVersionId = activity["definition"]!["headVersionId"]!.GetValue<string>(), outputs = Array.Empty<object>(),
                    inputs = new[] { new { referenceKey = "text", value = new { value = "Owned native hosting baseline.", expressionType = "Literal" } } } } }
        });
        var publication = await host.PostAsync($"/publishing/workflows/{definition["version"]!["id"]!.GetValue<string>()}/publish", new { });
        var artifactId = publication["artifactId"]!.GetValue<string>();
        var started = await host.PostAsync($"/runtime/workflows/executables/{artifactId}/execute", new { sourceReferenceId = publication["sourceReferenceId"]!.GetValue<string>() });
        var executionId = started["workflowExecutionId"]!.GetValue<string>();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (true)
        {
            var view = await host.GetAsync($"/runtime/workflows/instances/{executionId}");
            if (view["instance"]!["status"]!.GetValue<string>() == "Completed") break;
            Assert.True(DateTimeOffset.UtcNow < deadline, "The real native execution did not complete.");
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
        Assert.True(File.Exists(host.RuntimeDatabase));
        Assert.True(File.Exists(host.DesignDatabase));
        var previousProcess = host.ProcessId;
        await host.RestartAsync();
        Assert.NotEqual(previousProcess, host.ProcessId);
        await host.LoginAsync();
        var recovered = await host.GetAsync($"/runtime/workflows/instances/{executionId}");
        Assert.Equal("Completed", recovered["instance"]!["status"]!.GetValue<string>());
        Assert.Equal(artifactId, recovered["instance"]!["artifactId"]!.GetValue<string>());
        Assert.Equal("Completed", Assert.Single(recovered["activities"]!.AsArray())!["status"]!.GetValue<string>());
    }
}
