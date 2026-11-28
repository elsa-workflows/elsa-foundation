using System.Net;
using System.Text.Json.Nodes;
using Elsa3.Activities.Design.Import.Integration.Tests.Support;
using Xunit;

namespace Elsa3.Activities.Design.Import.Integration.Tests;

public sealed class DispatchWorkflowCallTests
{
    [Theory]
    [InlineData("dispatch-caller")]
    [InlineData("execute-caller")]
    public async Task An_imported_child_call_can_resolve_its_target_but_the_actor_host_refuses_its_classic_cancellation_policy(string callerFixture)
    {
        await using var host = await Elsa3ImportHost.StartAsync();
        var import = await host.ImportSelectionAsync($"import-{callerFixture}", ["dispatch-target", callerFixture]);
        Assert.True(import.Apply.StatusCode == HttpStatusCode.Created, import.Apply.Body.ToJsonString());
        Assert.Equal("dispatch-target", Elsa3ImportHost.Required(import.Item("dispatch-target-v1"), "workflowDefinitionId"));

        // The target is published first: publishing the caller pins the target's live published artifact.
        await host.PublishWorkflowAsync(Elsa3ImportHost.Required(import.Item("dispatch-target-v1"), "workflowVersionId"));
        var callerVersion = Elsa3ImportHost.Required(import.Item($"{callerFixture}-v1"), "workflowVersionId");
        using var published = await host.Send(HttpMethod.Post,
            $"publishing/workflows/{Uri.EscapeDataString(callerVersion)}/publish", new JsonObject());
        var problem = await published.Content.ReadAsStringAsync();

        Assert.True(published.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.BadRequest, problem);
        Assert.Contains("CancelChildOnParentCancellation", problem, StringComparison.Ordinal);
        Assert.Null(await host.ActivePublicationAsync(callerFixture));
    }
}
