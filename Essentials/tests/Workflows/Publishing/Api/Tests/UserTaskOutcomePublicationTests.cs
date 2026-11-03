using System.Text.Json;
using Elsa.Activities.Primitives.Activities;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Models;
using Xunit;
using static Elsa.Workflows.Publishing.Api.Tests.SecretBindingCompilerFixture;
namespace Elsa.Workflows.Publishing.Api.Tests;
public sealed class UserTaskOutcomePublicationTests
{
    [Fact]
    public async Task Dynamic_action_names_are_refused_before_a_task_can_open()
    {
        var node = Node(typeof(CreateUserTask), new ArgumentState(nameof(CreateUserTask.Actions), new ArgumentValue(JsonSerializer.SerializeToElement("[\"Approve\"]"), "JavaScript"), null, null, null, null));
        var failure = await Assert.ThrowsAnyAsync<ArgumentException>(() => CompileAsync(node, [typeof(CreateUserTask)]));
        Assert.Contains("authored literal", failure.Message);
    }
    [Fact]
    public async Task Literal_action_names_are_pinned_to_the_executable_contract()
    {
        var node = Node(typeof(CreateUserTask), new ArgumentState(nameof(CreateUserTask.Actions), new ArgumentValue(JsonSerializer.SerializeToElement(new[] { "Approve", "Request More Info" }), "Literal"), null, null, null, null));
        var executable = await CompileAsync(node, [typeof(CreateUserTask)]);
        Assert.Contains("Approve", executable.RootActivity.ActivityContract!.Outcomes);
        Assert.Contains("Request More Info", executable.RootActivity.ActivityContract!.Outcomes);
    }
}
