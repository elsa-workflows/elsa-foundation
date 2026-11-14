using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Publishing.Core.Requests;
using Elsa.Workflows.Publishing.Core.Models;
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_dynamic_portable_SetOutput_preserves_its_result_contract_and_converts_to_the_declared_output(bool serialized)
    {
        var expression = SetOutputExpression(new TypeReference("Elsa.Any"));
        var argument = new ArgumentValue(serialized ? JsonSerializer.SerializeToElement(expression) : expression, "JavaScript");

        var binding = await PublishSetOutputValueAsync("Count", new TypeReference("Elsa.Any"), argument: argument);

        Assert.Equal(RuntimeInputBindingSource.Expression, binding.Source);
        Assert.Equal("Elsa.Any", binding.Expression!.ResultType!.Id);
        Assert.Equal(expression.Source, binding.Expression.Expression);
        Assert.Equal(expression.CapabilityProfile, binding.Expression.CapabilityProfile);
        Assert.Equal("Int32", binding.TargetType.Alias);
        var plan = Assert.IsType<ValueConversionPlan>(binding.ConversionPlan);
        Assert.Equal("Elsa.Any", plan.SourceType.Alias);
        Assert.Equal("Int32", plan.TargetType.Alias);
        Assert.Equal(ValueConversionOperation.Profile, plan.Operation);
        Assert.Equal("elsa.json", plan.Profile!.Id);
    }

    [Theory]
    [InlineData("Int32", CollectionKind.Single)]
    [InlineData("Elsa.Any", CollectionKind.List)]
    public async Task A_portable_SetOutput_still_refuses_a_result_contract_that_differs_from_its_authored_value_type(string alias, CollectionKind collection)
    {
        var expression = SetOutputExpression(new TypeReference(alias, collection));

        var exception = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() =>
            PublishSetOutputValueAsync("Count", new TypeReference("Elsa.Any"), argument: new ArgumentValue(expression, "JavaScript")));

        Assert.Contains("portable expression declares result type", exception.ToString(), StringComparison.Ordinal);
    }

    private static ExpressionDefinition SetOutputExpression(TypeReference resultType) =>
        new("JavaScript", "7", resultType, new Dictionary<string, ExpressionParameterBinding>(),
            JsonSerializer.SerializeToElement(new { }), ExpressionCapabilityProfiles.BindingPureV1);

    private async Task<RuntimeInputBinding> PublishSetOutputValueAsync(string outputName, TypeReference authoredType, object? value = null, ArgumentValue? argument = null)
    {
        var setOutput = new ActivityNode("set-output", "elsa.intrinsic.set-output@1",
        [
            new WorkflowArgumentState(WorkflowIntrinsicInputKeys.Name, new ArgumentValue(JsonSerializer.SerializeToElement(outputName), "Literal"), null, null, null, null),
            new WorkflowArgumentState(WorkflowIntrinsicInputKeys.Value, argument ?? new ArgumentValue(JsonSerializer.SerializeToElement(value ?? 7), "Literal"), null, null, null, null)
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
