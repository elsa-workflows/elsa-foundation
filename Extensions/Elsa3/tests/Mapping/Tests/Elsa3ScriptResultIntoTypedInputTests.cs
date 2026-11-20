using Elsa3.Mapping.Services;
using Elsa3.Models;
using Elsa3.Mapping.Tests.Support;
using Xunit;
using static Elsa3.Mapping.Tests.Elsa3BuiltInConversionTests;

namespace Elsa3.Mapping.Tests;

/// <summary>
/// Spec 1228 User Story 6 (T038, FR-023, FR-024, research R-5): an Elsa 3 script's untyped result bound to a boolean,
/// string or number input is kept only when the script shows it returns the matching kind, so Elsa 4 gives the value
/// Elsa 3 did; any other or unknown kind is refused at import with <c>ELS3-UNTYPED-RESULT</c>.
/// </summary>
public sealed class Elsa3ScriptResultIntoTypedInputTests
{
    [Theory]
    [InlineData("true")]
    [InlineData("1 > 2")]
    [InlineData("!flag")]
    [InlineData("a === b && c < d")]
    [InlineData("Boolean(x)")]
    [InlineData("var ok = x > 1; return ok ? true : false;")]
    [InlineData("if (x) { return true; }\nreturn false;")]
    public void A_script_that_shows_a_boolean_fits_a_boolean_input(string script) =>
        Assert.True(Elsa3ScriptResultKinds.Fits(Elsa3ScriptResultKinds.Infer(script), Elsa3ScalarTarget.Boolean));

    [Theory]
    [InlineData("'true'")]
    [InlineData("1")]
    [InlineData("x")]
    [InlineData("getVariable('flag')")]
    [InlineData("null")]
    [InlineData("({ ok: true })")]
    [InlineData("if (x) { return true; }\nreturn 'no';")]
    public void A_script_that_shows_another_or_no_kind_does_not_fit_a_boolean_input(string script) =>
        Assert.False(Elsa3ScriptResultKinds.Fits(Elsa3ScriptResultKinds.Infer(script), Elsa3ScalarTarget.Boolean));

    [Theory]
    [InlineData("'a' + x", "String", true)]
    [InlineData("`total: ${x}`", "String", true)]
    [InlineData("String(x)", "String", true)]
    [InlineData("42", "String", false)]
    [InlineData("42", "Integer", true)]
    [InlineData("Math.round(x * 1.5)", "Integer", true)]
    [InlineData("x / 2", "Integer", false)]
    [InlineData("2.5", "Integer", false)]
    [InlineData("x / 2", "Number", true)]
    [InlineData("'42'", "Number", false)]
    [InlineData("0.1 + 0.2", "Decimal", false)]
    [InlineData("42", "Decimal", true)]
    public void Each_kind_fits_only_the_target_elsa_4_converts_it_into(string script, string target, bool fits) =>
        Assert.Equal(fits, Elsa3ScriptResultKinds.Fits(Elsa3ScriptResultKinds.Infer(script), Enum.Parse<Elsa3ScalarTarget>(target)));

    [Fact]
    public async Task A_boolean_script_result_into_a_flow_decision_imports()
    {
        var result = await ConvertAsync("x > 1");

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
    }

    [Theory]
    [InlineData("'true'", "a string")]
    [InlineData("1", "an integer")]
    [InlineData("getVariable('flag')", "a value whose kind the script does not show")]
    public async Task Another_or_an_unknown_kind_into_a_flow_decision_is_refused_at_import(string script, string returns)
    {
        var result = await ConvertAsync(script);

        var diagnostic = Assert.Single(result.Diagnostics, item => item.Code == Elsa3ImportDiagnosticCodes.UntypedResultIntoTypedInput);
        Assert.Contains("'decide'", diagnostic.Message);
        Assert.Contains("'calc'", diagnostic.Message);
        Assert.Contains(returns, diagnostic.Message);
        Assert.Contains("Boolean(…)", diagnostic.Guidance);
    }

    private static Task<Elsa3MigrationResult<Conversion.Elsa3ConvertedDefinition>> ConvertAsync(string script)
    {
        var calc = Activity("calc", "Elsa.RunJavaScript", ("script", Literal(script)), ("result", Captured("calc-result")));
        var decide = Activity("decide", "Elsa.FlowDecision", ("condition", Captured("calc-result", "Boolean")));
        return ConversionServices.ConvertAsync(RealActivityCatalog.Create(),
            ConversionServices.Definition(Sequence("root", calc, decide), variables: [new Elsa3.Models.Elsa3Variable { Id = "flag", Name = "flag", TypeName = "Boolean" }]));
    }

    [Fact]
    public async Task A_json_input_declared_with_the_elsa_3_object_type_still_converts()
    {
        // Spec 1228 FR-018: Elsa 3's Object is the dynamic kind Any, so a JSON-only input declared Object is JSON.
        var publish = Activity("publish", "Elsa.PublishEvent",
            ("eventName", Literal("e")), ("payload", Expression("JavaScript", "({ items: [1, 2] })", "Object")));

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(Sequence("root", publish)));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
    }
}
