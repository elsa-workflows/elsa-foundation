using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa3.Mapping.Services;
using Elsa3.Mapping.Tests.Support;
using Elsa3.Models;
using Xunit;
using static Elsa3.Mapping.Tests.Elsa3BuiltInConversionTests;

namespace Elsa3.Mapping.Tests;

/// <summary>
/// Spec 247: Elsa 3 host functions that Elsa 4 runs the same convert; the rest are refused with guidance naming the
/// Elsa 3 rewrite. Converted sources are run in the Elsa 4 evaluator.
/// </summary>
public sealed class Elsa3JavaScriptHostFunctionTests
{
    [Theory]
    [InlineData("getCorrelationId()", "elsa3CorrelationId", WorkflowInstanceExpressionParameterBinding.Members.CorrelationId)]
    [InlineData("getWorkflowInstanceId()", "elsa3WorkflowInstanceId", WorkflowInstanceExpressionParameterBinding.Members.InstanceId)]
    public async Task Instance_metadata_reads_become_a_workflow_instance_parameter(string expression, string parameter, string member)
    {
        var result = await ConvertTextAsync(expression);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var converted = ReadExpression(result);
        Assert.Equal($"args.{parameter}", converted.Source);
        Assert.Equal(member, Assert.IsType<WorkflowInstanceExpressionParameterBinding>(converted.Parameters[parameter]).Member);
    }

    [Fact]
    public async Task A_converted_correlation_id_read_returns_the_instance_value()
    {
        var result = await ConvertTextAsync("return { correlationId: getCorrelationId(), again: getCorrelationId() }");

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var value = await PortableJavaScript.EvaluateAsync(ReadExpression(result), new Dictionary<string, JsonElement>
        {
            ["elsa3CorrelationId"] = JsonSerializer.SerializeToElement("c-1"),
        });
        Assert.Equal("c-1", value.GetProperty("correlationId").GetString());
        Assert.Equal("c-1", value.GetProperty("again").GetString());
    }

    [Theory]
    [InlineData("CorrelationId")]
    [InlineData("WorkflowInstanceId")]
    public async Task A_variable_whose_accessor_has_a_built_in_name_is_ambiguous(string variable)
    {
        var result = await ConvertTextAsync($"get{variable}()", new Elsa3Variable { Id = "v", Name = variable, TypeName = "String" });

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.AmbiguousReference);
    }

    [Fact]
    public async Task A_built_in_called_with_arguments_is_refused()
    {
        var result = await ConvertTextAsync("getCorrelationId('x')");

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.ForbiddenCapability);
    }

    [Fact]
    public async Task A_function_the_script_declares_with_a_built_in_name_is_a_local_call()
    {
        var result = await ConvertTextAsync("(() => { function getCorrelationId() { return 'local'; } return getCorrelationId(); })()");

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Empty(ReadExpression(result).Parameters);
    }

    private static Task<Elsa3MigrationResult<Conversion.Elsa3ConvertedDefinition>> ConvertTextAsync(string expression, params Elsa3Variable[] variables) =>
        ConversionServices.ConvertAsync(
            RealActivityCatalog.Create(),
            ConversionServices.Definition(Sequence("root", Activity("node", "Elsa.WriteLine", ("text", Expression("JavaScript", expression)))), variables: variables));

    private static ExpressionDefinition ReadExpression(Elsa3MigrationResult<Conversion.Elsa3ConvertedDefinition> result)
    {
        var argument = Input(Node(result.Value!.State, "node"), "text");
        return (argument.Value.Value is JsonElement element ? element : JsonSerializer.SerializeToElement(argument.Value.Value))
            .Deserialize<ExpressionDefinition>()!;
    }
}
