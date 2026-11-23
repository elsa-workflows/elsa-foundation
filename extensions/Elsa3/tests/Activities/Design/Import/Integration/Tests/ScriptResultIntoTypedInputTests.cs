using Elsa.Workflows.Runtime.Core.Models;
using Elsa3.Activities.Design.Import.Integration.Tests.Support;
using Xunit;

namespace Elsa3.Activities.Design.Import.Integration.Tests;

/// <summary>
/// Spec 1228 User Story 6 (T039): an Elsa 3 script result bound to a Flow Decision's boolean condition imports, publishes
/// (never with <c>VF-COER-001</c>) and takes the branch Elsa 3 took, because the script shows it returns a boolean. The
/// per-kind import decisions (a matching kind converts; another or an unknown kind is refused at import) are covered in
/// the converter's <c>Elsa3ScriptResultIntoTypedInputTests</c>.
/// </summary>
public sealed class ScriptResultIntoTypedInputTests
{
    [Theory]
    [InlineData("reusable-reads-result", "Approved")]
    [InlineData("reusable-reads-result-false", "Rejected")]
    public async Task A_boolean_script_result_drives_the_flow_decision_as_in_elsa_3(string fixture, string outcome)
    {
        await using var host = await Elsa3ImportHost.StartAsync();
        var (_, items) = await host.ImportAsync($"import-{fixture}", fixture);
        var reusable = Assert.Single(items);
        var wrapper = await host.PublishWorkflowAsync(Elsa3ImportHost.Required(reusable, "workflowVersionId"));

        var run = await host.RunAsync(wrapper);

        Assert.True(run.Workflow?.Status == WorkflowExecutionStatus.Completed, run.Describe());
        Assert.Empty(run.Incidents);
        var activity = Assert.Single(run.Activities, state => state.Execution.ActivityType == Elsa3ImportHost.Required(reusable, "activityTypeKey"));
        Assert.Equal(new[] { outcome }, activity.Completion?.OutcomeNames);
    }
}
