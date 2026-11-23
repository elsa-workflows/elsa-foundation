using System.Net;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa3.Activities.Design.Import.Integration.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Elsa3.Activities.Design.Import.Integration.Tests;

/// <summary>
/// Spec 1231 User Story 6 (T040), #4899: an Elsa 3 RunJavaScript that sets variables with <c>setVariable</c> and a
/// generated setter, and changes a member of a variable's value through <c>getVariable</c>, imports, publishes and writes
/// the variables with its step. The real path: import, publish and the Jint engine over the step's variable session.
/// </summary>
public sealed class RunJavaScriptSetsVariablesRunTests(ITestOutputHelper output)
{
    [Fact]
    public async Task An_imported_script_that_sets_variables_publishes_and_writes_them_with_the_step()
    {
        await using var host = await Elsa3ImportHost.StartAsync();
        var import = await host.ImportSelectionAsync("import-script-sets-variable", ["script-sets-variable"]);
        Assert.True(import.Apply.StatusCode == HttpStatusCode.Created, import.Apply.Body.ToJsonString());
        var workflow = await host.PublishWorkflowAsync(Elsa3ImportHost.Required(import.Item("script-sets-variable-v1"), "workflowVersionId"));

        await using (var scope = await host.Shell.ServiceProvider.GetRequiredService<IPersistenceOperationScopeFactory>()
                         .CreateAsync(new PersistenceScope(Elsa3ImportHost.TenantId)))
        {
            var executable = await scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>().FindAsync(workflow.ArtifactId);
            Assert.NotNull(executable);
            var seed = Assert.Single(executable.NodesById.Values, node => node.AuthoredActivityId == "seed");
            Assert.Equal("true", seed.Metadata[RuntimeMetadataKeys.MayWriteEveryVisibleVariable]);
        }

        var run = await host.RunAsync(workflow);
        output.WriteLine(run.Describe());

        Assert.True(run.Workflow?.Status == WorkflowExecutionStatus.Completed, run.Describe());
        Assert.Empty(run.Incidents);
        Assert.All(run.Activities, state => Assert.Equal(ActivityExecutionStatus.Completed, state.Status));
        Assert.Equal("11", Variable(run, "total"));
        Assert.Equal("5", Variable(run, "count"));
        Assert.Equal("7", Variable(run, "dynamic"));
        // The second step changed one member of the value the first step set; the other member keeps its data.
        Assert.Equal("""{"Status":"Paid","Lines":2}""", Variable(run, "order"));
    }

    private static string Variable(RunState run, string key)
    {
        var frame = run.Workflow?.RootVariableFrame ?? throw new Xunit.Sdk.XunitException($"The run has no root variable frame ({run.Describe()}).");
        Assert.True(frame.Values.TryGetValue(key, out var envelope), $"The root frame has no '{key}': {string.Join(", ", frame.Values.Keys)}.");
        return envelope.InlineValue?.GetRawText() ?? $"<{envelope.Presence}>";
    }
}
