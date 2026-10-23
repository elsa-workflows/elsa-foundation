using System.Net;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa3.Activities.Design.Import.Integration.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace Elsa3.Activities.Design.Import.Integration.Tests;

/// <summary>
/// #3200 (spec 267): an imported Elsa 3 workflow whose activities carry <c>null</c> members, as every Elsa 3 export does
/// for members the author never set, publishes and runs as it did in Elsa 3. Each null used to become an untyped binding
/// that publication refused.
/// </summary>
public sealed class NullMemberPublishTests(ITestOutputHelper output)
{
    /// <summary>
    /// The JTest corpus case. Publishing is the step #3200 broke; running an imported RunJavaScript is blocked separately,
    /// by its <c>arguments</c> input (#3195), so this test stops at publishing.
    /// </summary>
    [Fact]
    public async Task A_RunJavaScript_with_null_possible_outcomes_and_result_publishes()
    {
        await using var host = await Elsa3ImportHost.StartAsync();
        var import = await host.ImportSelectionAsync("import-null-members-script", ["null-members-script"]);
        Assert.True(import.Apply.StatusCode == HttpStatusCode.Created, import.Apply.Body.ToJsonString());

        var published = await host.PublishWorkflowAsync(Elsa3ImportHost.Required(import.Item("null-members-script-v1"), "workflowVersionId"));

        Assert.False(string.IsNullOrEmpty(published.ArtifactId));
    }

    /// <summary>
    /// Null members of activities that run today: WriteLine writes an empty line, PublishEvent publishes with no
    /// correlation id and no payload, and Correlate and SetName clear the correlation id and the instance name an
    /// earlier Correlate and SetName set.
    /// </summary>
    [Fact]
    public async Task A_workflow_with_null_members_publishes_and_runs_as_in_Elsa_3()
    {
        await using var host = await Elsa3ImportHost.StartAsync();
        var import = await host.ImportSelectionAsync("import-null-members", ["null-members"]);
        Assert.True(import.Apply.StatusCode == HttpStatusCode.Created, import.Apply.Body.ToJsonString());
        var workflow = await host.PublishWorkflowAsync(Elsa3ImportHost.Required(import.Item("null-members-v1"), "workflowVersionId"));

        var run = await host.RunAsync(workflow);
        output.WriteLine(run.Describe());

        Assert.True(run.Workflow?.Status == WorkflowExecutionStatus.Completed, run.Describe());
        Assert.Empty(run.Incidents);
        Assert.Contains("name", run.ExecutedNodeIds);
        Assert.All(run.Activities, state => Assert.Equal(ActivityExecutionStatus.Completed, state.Status));
        // Elsa 3 set a null correlation id and instance name; Elsa 4 clears both on the empty string the null became.
        Assert.Null(run.Workflow!.CorrelationId);
        Assert.False(run.Workflow.SystemMetadata.ContainsKey(RuntimeMetadataKeys.InstanceName), run.Describe());
    }
}
