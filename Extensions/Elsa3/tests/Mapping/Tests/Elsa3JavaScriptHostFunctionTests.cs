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
        Assert.Equal($"(args.{parameter}) ?? null", converted.Source);
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

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("a")]
    [InlineData(" a ")]
    [InlineData("\t\r\n")]
    [InlineData("\u0085")]
    [InlineData("  　")]
    [InlineData("﻿")]
    [InlineData("\u001C")]
    public async Task String_helpers_return_what_dotnet_returned(string? value)
    {
        var result = await ConvertTextAsync(
            "return { empty: isNullOrEmpty(getVariable('s')), blank: isNullOrWhiteSpace(getVariable('s')) }",
            new Elsa3Variable { Id = "s", Name = "s", TypeName = "String" });

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var evaluated = await PortableJavaScript.EvaluateAsync(ReadExpression(result), new Dictionary<string, JsonElement>
        {
            ["s"] = JsonSerializer.SerializeToElement(value),
        });
        Assert.Equal(string.IsNullOrEmpty(value), evaluated.GetProperty("empty").GetBoolean());
        Assert.Equal(string.IsNullOrWhiteSpace(value), evaluated.GetProperty("blank").GetBoolean());
    }

    [Fact]
    public async Task A_string_helper_in_a_single_expression_is_wrapped_with_its_prelude()
    {
        var result = await ConvertTextAsync("isNullOrEmpty('') && isNullOrEmpty('')");

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var source = ReadExpression(result).Source;
        Assert.StartsWith("(function () {\nfunction __elsa3IsNullOrEmpty(v)", source, StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(source, "function __elsa3IsNullOrEmpty"));
        Assert.True((await PortableJavaScript.EvaluateAsync(ReadExpression(result))).GetBoolean());
    }

    [Fact]
    public async Task A_string_helper_evaluates_its_argument_once()
    {
        var result = await ConvertTextAsync("var calls = []; isNullOrWhiteSpace((calls.push(1), ' ')); calls.length");

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Equal(1, (await PortableJavaScript.EvaluateAsync(ReadExpression(result))).GetInt32());
    }

    [Fact]
    public async Task A_string_helper_in_a_script_gets_its_prelude()
    {
        var root = Sequence("root", Activity("node", "Elsa.RunJavaScript", ("script", Literal("isNullOrEmpty('')"))));

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(root));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Equal(
            "function __elsa3IsNullOrEmpty(v) { return v == null || v === \"\"; }\nreturn __elsa3IsNullOrEmpty('')",
            Text(Input(Node(result.Value!.State, "node"), "script")));
    }

    [Theory]
    [InlineData("isNullOrEmpty()")]
    [InlineData("isNullOrEmpty('a', 'b')")]
    public async Task A_string_helper_with_other_than_one_argument_is_refused(string expression)
    {
        var result = await ConvertTextAsync(expression);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.ForbiddenCapability);
    }

    [Fact]
    public async Task A_script_declaring_the_prelude_name_is_ambiguous()
    {
        var result = await ConvertTextAsync("var __elsa3IsNullOrEmpty = 1; isNullOrEmpty('')");

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.AmbiguousReference);
    }

    [Theory]
    [InlineData("newGuid()", "newGuid", "getWorkflowInstanceId()")]
    [InlineData("newGuidString()", "newGuidString", "getWorkflowInstanceId()")]
    [InlineData("newShortGuid()", "newShortGuid", "getWorkflowInstanceId()")]
    [InlineData("getGuidString()", "getGuidString", "getWorkflowInstanceId()")]
    [InlineData("new Date()", "Date", "Delay activity")]
    [InlineData("new Date('2026-01-01').toISOString()", "Date", "Delay activity")]
    [InlineData("Date.now()", "Date.now", "Delay activity")]
    [InlineData("DateTime.Now", "DateTime", "Delay activity")]
    [InlineData("Math.random()", "Math.random", "getWorkflowInstanceId()")]
    [InlineData("Guid.NewGuid()", "Guid", "getWorkflowInstanceId()")]
    [InlineData("toJson({ a: 1 })", "toJson", "JSON.stringify")]
    [InlineData("parseGuid('00000000-0000-0000-0000-000000000000')", "parseGuid", "as a string")]
    [InlineData("bytesToString(x)", "bytesToString", "activity")]
    [InlineData("streamToBytes(x)", "streamToBytes", "activity")]
    [InlineData("stringToBase64('a')", "stringToBase64", "activity")]
    [InlineData("getConfig('Key')", "getConfig", "workflow input")]
    [InlineData("getWorkflowDefinitionId()", "getWorkflowDefinitionId", "correlation ID and the instance ID")]
    [InlineData("setCorrelationId('x')", "setCorrelationId", "Correlate activity")]
    [InlineData("setWorkflowInstanceName('x')", "setWorkflowInstanceName", "Set Name activity")]
    [InlineData("getLastResult()", "getLastResult", "variable")]
    public async Task A_refused_construct_names_itself_and_the_elsa3_rewrite(string expression, string named, string guidance)
    {
        var result = await ConvertTextAsync(expression);

        Assert.False(result.Succeeded);
        var diagnostic = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.ForbiddenCapability);
        Assert.Contains($"'{named}'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains(guidance, diagnostic.Guidance, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("setOutcome('Approved')")]
    [InlineData("setOutcomes(['Approved', 'Logged'])")]
    public async Task A_script_setting_an_outcome_refuses_the_whole_definition(string script)
    {
        var root = Sequence("root",
            Activity("before", "Elsa.WriteLine", ("text", Literal("a"))),
            Activity("node", "Elsa.RunJavaScript", ("script", Literal(script))));

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(root));

        Assert.False(result.Succeeded);
        Assert.Null(result.Value);
        var diagnostic = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.ForbiddenCapability);
        Assert.Contains("Decision or Switch", diagnostic.Guidance, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_host_function_keeps_the_generic_refusal()
    {
        var result = await ConvertTextAsync("getConfiguration('Key')");

        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == Elsa3ImportDiagnosticCodes.ForbiddenCapability &&
            diagnostic.Message.Contains("unknown or unapproved host function 'getConfiguration'", StringComparison.Ordinal));
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
