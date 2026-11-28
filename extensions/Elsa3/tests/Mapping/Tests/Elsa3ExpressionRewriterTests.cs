using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Primitives.Models;
using Elsa3.Mapping.Services;
using Xunit;

namespace Elsa3.Mapping.Tests;

public sealed class Elsa3ExpressionRewriterTests
{
    private readonly Elsa3ExpressionRewriter _sut = new(Support.ConversionServices.DotNetUsageAnalyzer());

    [Fact]
    public void JavaScript_rewrites_only_parser_proven_reads_and_preserves_literals_comments_and_property_names()
    {
        const string source = "\"getVariable('total')\" + getVariable(\"total\") + Math.variables.total /* getVariable('total') */";

        var result = _sut.Rewrite("JavaScript", source, new TypeReference("String"), Resolve);

        Assert.True(result.Succeeded);
        Assert.Equal(
            "\"getVariable('total')\" + args.total + Math.variables.total /* getVariable('total') */",
            result.Definition!.Source);
        Assert.IsType<VariableExpressionParameterBinding>(result.Definition.Parameters["total"]);
    }

    [Fact]
    public void Liquid_rewrites_syntax_tree_member_but_not_comment_text()
    {
        const string source = "{% comment %}{{ Variables.customer.name }}{% endcomment %}{{ Variables.customer.name }}";

        var result = _sut.Rewrite("Liquid", source, new TypeReference("String"), Resolve);

        Assert.True(result.Succeeded);
        Assert.Equal(
            "{% comment %}{{ Variables.customer.name }}{% endcomment %}{{ customer.name }}",
            result.Definition!.Source);
        Assert.IsType<VariableExpressionParameterBinding>(result.Definition.Parameters["customer"]);
    }

    [Theory]
    [InlineData("setVariable(\"total\", 42)")]
    [InlineData("variables.total = 42")]
    public void JavaScript_rejects_mutation(string source)
    {
        var result = _sut.Rewrite("JavaScript", source, new TypeReference("String"), Resolve);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Issues, issue => issue.Code == Elsa3ImportDiagnosticCodes.ForbiddenCapability);
    }

    /// <summary>
    /// The lowerer's write pre-pass agrees with the conversion (#5664): a host function receives its arguments as JSON,
    /// so passing a variable to a replaced global is no write, where passing it to an undeclared function may be.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Passing_a_variable_to_a_replaced_global_is_no_write(bool registered, bool mayWrite)
    {
        var lookup = new JavaScriptHostFunctionDeclaration("acme.lookupCustomer", _ => default);
        var rewriter = new Elsa3ExpressionRewriter(Support.ConversionServices.DotNetUsageAnalyzer(), hostFunctions: registered ? [lookup] : []);

        var result = rewriter.ScriptMayWriteVariables("lookupCustomer(getVariable('customer'))", reference => reference.Kind == Elsa3AmbientReferenceKind.Variable
            ? Elsa3ExpressionParameterResolution.SuccessVariable(new VariableExpressionParameterBinding(VariableReference.WorkflowScopeId, "variable-customer"), reference.Name)
            : Elsa3ExpressionParameterResolution.Failure(new Elsa3ExpressionRewriteIssue("x", "x", "x")));

        Assert.Equal(mayWrite, result);
    }

    /// <summary>
    /// #5894: an input expression that changes a value it read from a variable, through a local name or a member, is
    /// refused: Elsa 4 input expressions only read (spec 1228 FR-006a), so it would fault with VF-BIND-001. The first four
    /// are the JTest corpus patterns (array-methods, object-methods, logging-workflow, composition-execute-workflow-root).
    /// </summary>
    [Theory]
    [InlineData("const array = getArrayVariable();\r\nconst itemToAdd = getHttpRequestBody().unshift.itemToAdd\r\nconst newLength = array.unshift(itemToAdd);\r\n\r\nreturn {\r\n    unshiftedArray: array,\r\n    newLength: newLength,\r\n    arrayVariable: getArrayVariable(), correlationId: getCorrelationId()\r\n}\r\n")]
    [InlineData("var obj = getObjectVariable();\r\nconst propertyName = getHttpRequestBody().deleteProperty.name;\r\ndelete obj[propertyName];\r\n\r\nreturn {\r\n    result: obj,\r\n    objectVariable: getObjectVariable(), correlationId: getCorrelationId()\r\n}")]
    [InlineData("let responseBody = getHttpResponseBody();\r\nresponseBody.queryArguments = getQueryArguments();\r\nresponseBody.correlationId = getCorrelationId();\r\nreturn responseBody;")]
    [InlineData("var result = getHttpRequestBody().subworkflowInput;\r\nresult.MillisecondsToDelaySubworkflowExecutionTime = getHttpRequestBody().millisecondsToDelaySubworkflowExecutionTime;\r\nreturn result;")]
    [InlineData("getOrder().lines.push(1)")]
    [InlineData("delete getOrder().status")]
    [InlineData("Object.assign(getOrder(), { status: 'Paid' })")]
    [InlineData("var lines = getOrder().lines; lines.sort(); return lines;")]
    [InlineData("var order = getVariable('Order'); if (order.total > 10) { order.discount++; } return order;")]
    [InlineData("var copy = getOrder().lines.slice(); copy[0].quantity = 2; return copy;")]
    public void Input_expression_that_changes_a_value_it_read_is_refused(string source)
    {
        var result = _sut.Rewrite("JavaScript", source, new TypeReference("Object"), ResolveVariables);

        Assert.False(result.Succeeded);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(Elsa3ImportDiagnosticCodes.InputChangesVariable, issue.Code);
        Assert.Contains("VF-BIND-001", issue.Message, StringComparison.Ordinal);
        Assert.Contains("move it into a Run JavaScript step before this input", issue.Guidance, StringComparison.Ordinal);
        Assert.Contains("change a local copy", issue.Guidance, StringComparison.Ordinal);
    }

    /// <summary>
    /// #5894: a change to a local copy, a read alone, a new local object and an opaque call on a read value are no change
    /// of the variable, so they convert. A Run JavaScript body writes variables, so the same change converts there.
    /// </summary>
    [Theory]
    [InlineData("const items = getArrayVariable().slice(); items.push(1); return items;", Elsa3JavaScriptMode.Expression)]
    [InlineData("var order = Object.assign({}, getOrder()); order.status = 'Paid'; return order;", Elsa3JavaScriptMode.Expression)]
    [InlineData("var order = JSON.parse(JSON.stringify(getOrder())); order.lines[0].quantity = 2; return order;", Elsa3JavaScriptMode.Expression)]
    [InlineData("var total = getOrder().total; total++; return total;", Elsa3JavaScriptMode.Expression)]
    [InlineData("const name = getHttpRequestBody().name; var result = {}; result[name] = getHttpRequestBody().value; return result;", Elsa3JavaScriptMode.Expression)]
    [InlineData("getArrayVariable().map(item => item * 2)", Elsa3JavaScriptMode.Expression)]
    [InlineData("getOrder().total.toFixed(2)", Elsa3JavaScriptMode.Expression)]
    [InlineData("var order = getOrder(); getHttpRequestBody().hooks.register(function () { order.touched = true; }); return order.status;", Elsa3JavaScriptMode.Expression)]
    [InlineData("var order = getOrder(); return { status: order.status, lines: order.lines.length };", Elsa3JavaScriptMode.Expression)]
    [InlineData("var order = getOrder(); order.status = 'Paid'; return order;", Elsa3JavaScriptMode.Script)]
    public void Input_expression_that_changes_no_value_it_read_converts(string source, Elsa3JavaScriptMode mode)
    {
        var result = _sut.Rewrite("JavaScript", source, new TypeReference("Object"), ResolveVariables, mode);

        Assert.DoesNotContain(result.Issues, issue => issue.Code == Elsa3ImportDiagnosticCodes.InputChangesVariable);
        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Issues.Select(issue => $"{issue.Code}: {issue.Message}")));
    }

    /// <summary>
    /// Every name a test script reads, through getVariable or a generated getter, is a workflow variable, except the
    /// workflow instance's correlation id, which no variable shadows.
    /// </summary>
    private static Elsa3ExpressionParameterResolution ResolveVariables(Elsa3AmbientReference reference) => reference.Kind switch
    {
        Elsa3AmbientReferenceKind.GeneratedAccessor when reference.Name == "CorrelationId" => Elsa3ExpressionParameterResolution.Failure(
            new Elsa3ExpressionRewriteIssue(Elsa3ImportDiagnosticCodes.DanglingReference, "No variable.", "None.")),
        Elsa3AmbientReferenceKind.Variable or Elsa3AmbientReferenceKind.GeneratedAccessor => Elsa3ExpressionParameterResolution.SuccessVariable(
            new VariableExpressionParameterBinding(VariableReference.WorkflowScopeId, $"variable-{reference.Name}"), reference.Name),
        _ => Resolve(reference),
    };

    private static Elsa3ExpressionParameterResolution Resolve(Elsa3AmbientReference reference) => reference.Kind switch
    {
        Elsa3AmbientReferenceKind.Variable => Elsa3ExpressionParameterResolution.Success(
            new VariableExpressionParameterBinding(VariableReference.WorkflowScopeId, $"variable-{reference.Name}")),
        Elsa3AmbientReferenceKind.WorkflowRequest => Elsa3ExpressionParameterResolution.Success(
            new WorkflowRequestExpressionParameterBinding(reference.Name)),
        Elsa3AmbientReferenceKind.ActivityResult => Elsa3ExpressionParameterResolution.Success(
            new ActivityResultExpressionParameterBinding("producer", $"key:{reference.Name}")),
        _ => throw new ArgumentOutOfRangeException(nameof(reference)),
    };
}
