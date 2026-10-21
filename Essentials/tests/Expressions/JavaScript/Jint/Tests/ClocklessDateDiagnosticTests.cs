using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript.Core.Exceptions;
using Xunit;

namespace Elsa.Expressions.JavaScript.Jint.Tests;

/// <summary>
/// ADR 0088 D4 / spec 262 FR-017: every Date form binding-pure-v2 removes fails with a message that names it, says
/// why, and gives the rewrite. The texts are the contract's, verbatim.
/// </summary>
public sealed class ClocklessDateDiagnosticTests
{
    [Theory]
    [InlineData("new Date()", "new Date() with no argument reads the clock, which binding-pure-v2 does not allow. Declare a Clock parameter and write new Date(args.now).")]
    [InlineData("Date()", "Date() called as a function reads the clock, which binding-pure-v2 does not allow. Declare a Clock parameter and write new Date(args.now).toString().")]
    [InlineData("Date.now()", "Date.now() reads the clock, which binding-pure-v2 does not allow. Declare a Clock parameter and write Date.parse(args.now).")]
    [InlineData("new Date(0).toLocaleString()", "toLocaleString() depends on a locale, which binding-pure-v2 does not provide. Use toISOString(), or format the parts with the UTC getters.")]
    [InlineData("new Date(0).toLocaleDateString()", "toLocaleDateString() depends on a locale, which binding-pure-v2 does not provide. Use toISOString().slice(0, 10), or format the parts with the UTC getters.")]
    [InlineData("new Date(0).toLocaleTimeString()", "toLocaleTimeString() depends on a locale, which binding-pure-v2 does not provide. Use toISOString().slice(11, 23), or format the parts with the UTC getters.")]
    [InlineData("new Date('10/20/2026').toISOString()", "Invalid date: binding-pure-v2 parses only ISO 8601 date-time strings such as 2026-10-20T08:30:00.000Z.")]
    public async Task A_removed_date_form_fails_with_its_guidance(string source, string message)
    {
        var exception = await Assert.ThrowsAsync<JavaScriptBindingEvaluationException>(
            () => ClocklessDateEvaluation.EvaluateAsync(source));

        Assert.Equal(message, exception.Message);
        Assert.NotNull(exception.InnerException);
    }

    [Fact]
    public async Task The_same_date_source_fails_under_v1_and_evaluates_under_v2()
    {
        const string source = "new Date('2026-10-20T08:30:00.000Z').toISOString()";

        var v1 = await Assert.ThrowsAsync<JavaScriptBindingEvaluationException>(
            () => ClocklessDateEvaluation.EvaluateAsync(source, profile: ExpressionCapabilityProfiles.BindingPureV1));
        var v2 = await ClocklessDateEvaluation.EvaluateAsync(source);

        Assert.Contains("deterministic binding sandbox", v1.Message, StringComparison.Ordinal);
        Assert.Contains("binding-pure-v1", v1.Message, StringComparison.Ordinal);
        Assert.Equal("2026-10-20T08:30:00.000Z", v2.GetString());
    }
}
