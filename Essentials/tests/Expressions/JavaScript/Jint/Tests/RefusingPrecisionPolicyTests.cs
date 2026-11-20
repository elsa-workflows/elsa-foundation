using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Expressions.JavaScript.Core.Services;
using Xunit;

namespace Elsa.Expressions.JavaScript.Jint.Tests;

/// <summary>Elsa's only precision policy refuses every lossy write (D-17, spec 1228 T018, US2 acceptance 1–3).</summary>
public sealed class RefusingPrecisionPolicyTests
{
    private static readonly JavaScriptLossyNumber LargeId = new("variables.order.id", "9007199254740993", "9007199254740992", 9007199254740992d, IsInteger: true);
    private static readonly JavaScriptLossyNumber LongDecimal = new("args.rate", "0.10000000000000000000000000001", "0.1", 0.1, IsInteger: false);

    [Fact]
    public void A_lossy_integer_is_refused_with_the_largest_safe_integer()
    {
        var decision = new RefusingPrecisionPolicy().Decide(new JavaScriptPrecisionContext("result", [new JavaScriptPrecisionLoss("result.id", LargeId, "9007199254740992")]));

        Assert.False(decision.Allowed);
        var diagnostic = Assert.Single(decision.Diagnostics);
        Assert.Equal(JavaScriptDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("result.id", diagnostic.Path);
        Assert.Equal("9007199254740993", diagnostic.Stored);
        Assert.Equal("9007199254740992", diagnostic.Written);
        Assert.Contains("9007199254740991", diagnostic.Reason);
        Assert.Contains("variables.order.id", diagnostic.Reason);
        Assert.Contains("string", diagnostic.Remedy);
    }

    [Fact]
    public void A_lossy_decimal_says_it_has_more_digits()
    {
        var decision = new RefusingPrecisionPolicy().Decide(new JavaScriptPrecisionContext("result", [new JavaScriptPrecisionLoss("result", LongDecimal, "0.1")]));

        Assert.Contains("more digits than a JavaScript number holds", Assert.Single(decision.Diagnostics).Reason);
    }

    [Fact]
    public void An_element_of_an_array_written_whole_names_the_array_and_the_remedy()
    {
        var decision = new RefusingPrecisionPolicy().Decide(new JavaScriptPrecisionContext(
            "variables.ids",
            [
                new JavaScriptPrecisionLoss("variables.ids[0]", LargeId, "9007199254740992", "variables.ids"),
                new JavaScriptPrecisionLoss("variables.ids[3]", LargeId with { Path = "variables.ids[3]" }, "9007199254740992", "variables.ids")
            ]));

        Assert.False(decision.Allowed);
        Assert.Equal(2, decision.Diagnostics.Count);
        Assert.All(decision.Diagnostics, diagnostic =>
        {
            Assert.Contains("'variables.ids' was written as a whole", diagnostic.Reason);
            Assert.Contains("do not change the array", diagnostic.Remedy);
        });
    }
}
