using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa3.Mapping.Services;
using Xunit;

namespace Elsa3.Mapping.Tests;

public sealed class Elsa3ExpressionRewriterTests
{
    private readonly Elsa3ExpressionRewriter _sut = new();

    [Fact]
    public void JavaScript_rewrites_only_parser_proven_reads_and_preserves_literals_comments_and_property_names()
    {
        const string source = "\"getVariable('total')\" + getVariable(\"total\") + model.variables.total /* getVariable('total') */";

        var result = _sut.Rewrite("JavaScript", source, new TypeReference("String"), Resolve);

        Assert.True(result.Succeeded);
        Assert.Equal(
            "\"getVariable('total')\" + args.total + model.variables.total /* getVariable('total') */",
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
    [InlineData("Math.random()")]
    public void JavaScript_rejects_mutation_and_nondeterminism(string source)
    {
        var result = _sut.Rewrite("JavaScript", source, new TypeReference("String"), Resolve);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Issues, issue => issue.Code == Elsa3ImportDiagnosticCodes.ForbiddenCapability);
    }

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
