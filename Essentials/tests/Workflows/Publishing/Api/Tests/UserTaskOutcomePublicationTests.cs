using System.Text.Json;
using System.Reflection;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Scripting.Activities;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Models;
using Xunit;
using static Elsa.Workflows.Publishing.Api.Tests.SecretBindingCompilerFixture;
namespace Elsa.Workflows.Publishing.Api.Tests;
public sealed class UserTaskOutcomePublicationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Optional_value_outcomes_preserve_existing_done_path(bool script, bool explicitNull)
    {
        var activityType = script ? typeof(RunJavaScript) : typeof(SendHttpRequest);
        var key = script ? "script" : nameof(SendHttpRequest.Url);
        var value = script ? "return 1;" : "https://example.invalid/";
        var inputs = new List<ArgumentState> { new(key, new ArgumentValue(JsonSerializer.SerializeToElement(value), "Literal"), null, null, null, null) };
        if (explicitNull) inputs.Add(new(script ? RunJavaScript.PossibleOutcomesInputKey : nameof(SendHttpRequest.ExpectedStatusCodes), new ArgumentValue(JsonSerializer.SerializeToElement((object?)null), "Literal"), null, null, null, null));
        var executable = await CompileAsync(Node(activityType, inputs.ToArray()), [activityType], includeDeclaredInputDefaults: true);
        Assert.Contains("Done", executable.RootActivity.ActivityContract!.Outcomes);
    }

    [Fact]
    public async Task Unedited_catalog_defaults_pin_arrays_and_an_object_instead_of_JSON_text()
    {
        // These are the raw string defaults advertised by CLR reconciliation and saved by the real property panel.
        var authoredDefaults = new[] { nameof(CreateUserTask.Actions), nameof(CreateUserTask.OptionalFormActions), nameof(CreateUserTask.Prefill) }
            .Select(key => {
                var declaration = typeof(CreateUserTask).GetProperty(key)!.GetCustomAttribute<ActivityInputAttribute>()!;
                return new ArgumentState(key, new ArgumentValue(JsonSerializer.SerializeToElement(declaration.DefaultValue), declaration.DefaultSyntax!), null, null, null, null);
            }).ToArray();
        var executable = await CompileAsync(Node(typeof(CreateUserTask), authoredDefaults), [typeof(CreateUserTask)], includeDeclaredInputDefaults: true);
        var bindings = executable.RootActivity.InputBindings;
        Assert.Equal(JsonValueKind.Array, bindings[nameof(CreateUserTask.Actions)].LiteralValue!.Value.ValueKind);
        Assert.Equal(2, bindings[nameof(CreateUserTask.Actions)].LiteralValue!.Value.GetArrayLength());
        Assert.Equal(0, bindings[nameof(CreateUserTask.OptionalFormActions)].LiteralValue!.Value.GetArrayLength());
        Assert.Equal(JsonValueKind.Object, bindings[nameof(CreateUserTask.Prefill)].LiteralValue!.Value.ValueKind);
    }

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
