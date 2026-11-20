using Elsa.Expressions.JavaScript.Jint.Services;
using Elsa.Expressions.JavaScript.Jint.Tests.Fixtures;
using Xunit;

namespace Elsa.Expressions.JavaScript.Jint.Tests;

/// <summary>The exact lossy-number check of ADR 0046 V9 (spec 1228 T003, research R-2).</summary>
public sealed class JavaScriptNumberPrecisionTests
{
    [Theory]
    [MemberData(nameof(PrecisionCases.All), MemberType = typeof(PrecisionCases))]
    public void A_number_is_lossy_only_when_its_JavaScript_JSON_differs_in_value(string text, bool exact, bool isInteger)
    {
        Assert.Equal(exact, JavaScriptNumberPrecision.IsExact(text, out _, out var integer));
        Assert.Equal(isInteger, integer);
    }

    [Fact]
    public void The_held_value_is_the_double_a_script_computes_with()
    {
        Assert.False(JavaScriptNumberPrecision.IsExact(PrecisionCases.LargeId, out var held, out _));
        Assert.Equal(PrecisionCases.LargeIdHeld, JavaScriptNumberPrecision.JavaScriptText(held));
        Assert.True(JavaScriptNumberPrecision.IsExact("1e21", out held, out _));
        Assert.Equal("1e+21", JavaScriptNumberPrecision.JavaScriptText(held));
    }
}
