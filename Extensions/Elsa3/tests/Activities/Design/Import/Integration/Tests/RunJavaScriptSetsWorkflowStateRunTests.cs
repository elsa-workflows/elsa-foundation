using System.Net;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa3.Activities.Design.Import.Integration.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace Elsa3.Activities.Design.Import.Integration.Tests;

public sealed class RunJavaScriptSetsWorkflowStateRunTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Imported_body_publishes_runs_and_commits_outputs_identity_and_variable_together()
    {
        await using var host = await Elsa3ImportHost.StartAsync();
        var import = await host.ImportSelectionAsync("import-script-state", ["script-workflow-state"]);
        Assert.True(import.Apply.StatusCode == HttpStatusCode.Created, import.Apply.Body.ToJsonString());
        var workflow = await host.PublishWorkflowAsync(Elsa3ImportHost.Required(import.Item("script-workflow-state-v1"), "workflowVersionId"));
        var run = await host.RunAsync(workflow);
        output.WriteLine(run.Describe());
        Assert.True(run.Workflow?.Status == WorkflowExecutionStatus.Completed, run.Describe());
        Assert.Empty(run.Incidents);
        Assert.Equal("new", run.Workflow.CorrelationId);
        Assert.Equal("Ready", run.Workflow.SystemMetadata[RuntimeMetadataKeys.InstanceName]);
        Assert.Equal("15", run.Workflow.RootVariableFrame!.Values["total"].InlineValue!.Value.GetRawText());
        Assert.Contains(run.Activities, state => state.Metadata.ContainsKey("workflowStateEffects"));
    }
}
