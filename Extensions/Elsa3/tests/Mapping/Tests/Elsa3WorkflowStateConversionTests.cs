using Elsa3.Mapping.Services;
using Elsa3.Mapping.Tests.Support;
using Elsa3.Models;
using Xunit;
using static Elsa3.Mapping.Tests.Elsa3BuiltInConversionTests;

namespace Elsa3.Mapping.Tests;

public sealed class Elsa3WorkflowStateConversionTests
{
    [Theory]
    [InlineData("setOutput('Answer', 42)", "return workflowState.setOutput('Answer', 42)")]
    [InlineData("setCorrelationId('key'); return getCorrelationId();", "workflowState.setCorrelationId(workflowState.requireExactIdentity(('key'))); return workflowState.getCorrelationId();")]
    [InlineData("setWorkflowInstanceName(null); return getWorkflowInstanceName();", "workflowState.setInstanceName(workflowState.requireExactIdentity((null))); return workflowState.getInstanceName();")]
    [InlineData("const key = 'Answer'; setOutput(key, 42); return 1;", "const key = 'Answer'; workflowState.setOutput(key, 42); return 1;")]
    [InlineData("function setOutput(x, y) { return x + y; } return setOutput(1, 2);", "function setOutput(x, y) { return x + y; } return setOutput(1, 2);")]
    [InlineData("false && setCorrelationId('new'); return getCorrelationId();", "false && workflowState.setCorrelationId(workflowState.requireExactIdentity(('new'))); return workflowState.getCorrelationId();")]
    [InlineData("setCorrelationId(getCorrelationId()); return 1;", "workflowState.setCorrelationId(workflowState.requireExactIdentity((workflowState.getCorrelationId()))); return 1;")]
    [InlineData("setWorkflowInstanceName(getWorkflowInstanceName()); return 1;", "workflowState.setInstanceName(workflowState.requireExactIdentity((workflowState.getInstanceName()))); return 1;")]
    [InlineData("class Local { setCorrelationId(value) { return value; } } return new Local().setCorrelationId('local');", "class Local { setCorrelationId(value) { return value; } } return new Local().setCorrelationId('local');")]
    [InlineData("class Local { getCorrelationId = 1; } return new Local().getCorrelationId;", "class Local { getCorrelationId = 1; } return new Local().getCorrelationId;")]
    [InlineData("setOutput: { break setOutput; } return 1;", "setOutput: { break setOutput; } return 1;")]
    public async Task Direct_body_calls_lower_without_reordering_or_changing_local_helpers(string source, string expected)
    {
        var converted = await ConvertAsync(source);
        Assert.True(converted.Succeeded, Describe(converted.Diagnostics));
        Assert.Equal(expected, Text(Input(Node(converted.Value!.State, "node"), "script")));
    }

    [Theory]
    [InlineData("setCorrelationId('')")]
    [InlineData("setCorrelationId(' \t')")]
    [InlineData("setWorkflowInstanceName(' ')")]
    public async Task Blank_literal_identities_refuse_instead_of_silently_normalizing(string source)
    {
        var result = await ConvertAsync(source);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.IdentityNormalization);
    }

    [Theory]
    [InlineData("const set = setCorrelationId; set('new');")]
    [InlineData("const workflowState = {}; setOutput('x', 1);")]
    [InlineData("setOutput(...['x', 1]);")]
    [InlineData("setOutput('x');")]
    [InlineData("setCorrelationId('a', 'b');")]
    [InlineData("getWorkflowInstanceName(1);")]
    [InlineData("setInput('x', 1);")]
    [InlineData("setResult(1);")]
    [InlineData("class Local { [setCorrelationId]() {} }")]
    public async Task Unsupported_alias_collision_arity_and_retained_helpers_refuse(string source)
    {
        var result = await ConvertAsync(source);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.IsError);
    }

    private static Task<Elsa3MigrationResult<Conversion.Elsa3ConvertedDefinition>> ConvertAsync(string source) =>
        ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(
            Sequence("root", Activity("node", "Elsa.RunJavaScript", ("script", Literal(source))))));
}
