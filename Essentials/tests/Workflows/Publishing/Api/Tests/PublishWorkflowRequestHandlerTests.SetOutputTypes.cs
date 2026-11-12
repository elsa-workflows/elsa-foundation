using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Publishing.Core.Requests;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using ArgumentValue = Elsa.Expressions.Core.Models.ArgumentValue;
using WorkflowArgumentState = Elsa.Workflows.Design.Core.Models.ArgumentState;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// Studio authors SetOutput with the dynamic <c>Elsa.Any</c> value type. The declared workflow output it names owns
/// the assignment contract, as a variable target does for Set, so the value publishes as the output's declared type
/// and a run reports the output with that type rather than <c>Elsa.Any</c> (#4069).
/// </summary>
public sealed partial class PublishWorkflowRequestHandlerTests
{
    private static readonly OutputDefinition CountOutput =
        new("count-ref", "Count", new TypeReference("Int32"), null, "Count", null, IsNullable: false);

    [Fact]
    public async Task A_dynamic_SetOutput_value_publishes_as_the_declared_outputs_type()
    {
        var binding = await PublishSetOutputValueAsync("Count", new TypeReference("Elsa.Any"));

        Assert.Equal("Int32", binding.TargetType.Alias);
    }

    [Fact]
    public async Task An_explicitly_typed_SetOutput_value_keeps_its_authored_type()
    {
        var binding = await PublishSetOutputValueAsync("Count", new TypeReference("String"), value: "7");

        Assert.Equal("String", binding.TargetType.Alias);
    }

    [Fact]
    public async Task A_SetOutput_naming_no_declared_output_keeps_its_authored_type()
    {
        var binding = await PublishSetOutputValueAsync("Renamed", new TypeReference("Elsa.Any"));

        Assert.Equal("Elsa.Any", binding.TargetType.Alias);
    }

    private async Task<RuntimeInputBinding> PublishSetOutputValueAsync(string outputName, TypeReference authoredType, object? value = null)
    {
        var setOutput = new ActivityNode("set-output", "elsa.intrinsic.set-output@1",
        [
            new WorkflowArgumentState(WorkflowIntrinsicInputKeys.Name, new ArgumentValue(JsonSerializer.SerializeToElement(outputName), "Literal"), null, null, null, null),
            new WorkflowArgumentState(WorkflowIntrinsicInputKeys.Value, new ArgumentValue(JsonSerializer.SerializeToElement(value ?? 7), "Literal"), null, null, null, null)
        ], [])
        {
            Intrinsic = new AuthoredWorkflowIntrinsic(AuthoredWorkflowIntrinsicKind.SetOutput, authoredType, null)
        };
        var version = new WorkflowDefinitionVersion("definition-1", "1.0.0")
        {
            Id = "version-1",
            Definition = new WorkflowDefinition { Id = "definition-1", Name = "Demo" },
            State = new WorkflowDefinitionState([], SequenceNode("sequence", [setOutput]), [], [CountOutput], null)
        };

        var publication = await Handler(version, _writeLineActivity, _sequenceActivity).Handle(new PublishWorkflow(version.Id), CancellationToken.None);

        var executable = Assert.IsType<WorkflowExecutable>(await _store.FindAsync(publication.ArtifactId));
        return Assert.Single(executable.RootActivity.DescendantsAndSelf(), node => node.IntrinsicKind == WorkflowIntrinsicKind.SetOutput)
            .InputBindings[WorkflowIntrinsicInputKeys.Value];
    }
}
