using System.Text.Json;
using Elsa3.Mapping.Services;
using Elsa3.Mapping.Tests.Support;
using Elsa3.Models;
using Xunit;
using static Elsa3.Mapping.Tests.Elsa3BuiltInConversionTests;

namespace Elsa3.Mapping.Tests;

/// <summary>
/// #4898, extending spec 1228 User Story 6 (FR-023, FR-024, research R-5) to every untyped producer: a loop's current
/// item, an event's payload, HTTP request data, and a script result captured into a variable. Elsa 3 coerced such a value
/// into a boolean, string or number input whatever it was; Elsa 4 converts only a value of the matching kind. The import
/// keeps the binding only where the kind is known to match and refuses it otherwise with <c>ELS3-UNTYPED-RESULT</c>.
/// </summary>
public sealed class Elsa3UntypedResultIntoTypedInputTests
{
    private static readonly Elsa3Variable Item = new() { Id = "item", Name = "item", TypeName = "Object" };

    [Fact]
    public async Task A_loop_item_read_into_a_string_input_is_refused_with_guidance()
    {
        // The issue's example: a ForEach over ["1","2"] or [1,2] feeding a typed input faults in Elsa 4 where Elsa 3 ran.
        var loop = ForEach(Activity("inside", "Elsa.WriteLine", ("text", Captured("item"))));

        var result = await ConvertAsync(Sequence("root", loop), Item);

        var diagnostic = Assert.Single(result.Diagnostics, item => item.Code == Elsa3ImportDiagnosticCodes.UntypedResultIntoTypedInput);
        Assert.Contains("'text'", diagnostic.Message);
        Assert.Contains("current item of loop 'loop'", diagnostic.Message);
        Assert.Contains("String(…)", diagnostic.Guidance);
    }

    [Fact]
    public async Task A_loop_item_read_directly_into_a_typed_input_is_refused()
    {
        // The item read through the loop's own memory slot, not a declared variable.
        var loop = Activity("loop", "Elsa.ForEach", ("items", Literal("[1,2]", "Object")), ("currentValue", Captured("item-slot")));
        loop.AdditionalProperties["body"] = Port(Activity("decide", "Elsa.FlowDecision", ("condition", Captured("item-slot", "Boolean"))));

        var result = await ConvertAsync(Sequence("root", loop));

        var diagnostic = Assert.Single(result.Diagnostics, item => item.Code == Elsa3ImportDiagnosticCodes.UntypedResultIntoTypedInput);
        Assert.Contains("current item of loop 'loop'", diagnostic.Message);
        Assert.Contains("Boolean(…)", diagnostic.Guidance);
    }

    [Fact]
    public async Task A_loop_item_read_into_a_dynamic_input_still_converts()
    {
        var loop = ForEach(Activity("inside", "Elsa.PublishEvent", ("eventName", Literal("e")), ("payload", Captured("item"))));

        var result = await ConvertAsync(Sequence("root", loop), Item);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
    }

    [Fact]
    public async Task An_event_payload_read_into_a_boolean_input_is_refused()
    {
        var root = Sequence("root",
            Activity("wait", "Elsa.Event", ("eventName", Literal("Approved")), ("result", Captured("payload"))),
            Activity("decide", "Elsa.FlowDecision", ("condition", Captured("payload", "Boolean"))));

        var result = await ConvertAsync(root, new Elsa3Variable { Id = "payload", Name = "payload", TypeName = "Object" });

        var diagnostic = Assert.Single(result.Diagnostics, item => item.Code == Elsa3ImportDiagnosticCodes.UntypedResultIntoTypedInput);
        Assert.Contains("payload of event 'wait'", diagnostic.Message);
        Assert.Contains("Boolean(…)", diagnostic.Guidance);
    }

    [Fact]
    public async Task Http_request_data_read_into_a_string_input_is_refused()
    {
        var endpoint = Activity("endpoint", "Elsa.HttpEndpoint", ("path", Literal("/orders")), ("parsedContent", Captured("body")));
        endpoint.CustomProperties = new Elsa3ActivityCustomProperties { CanStartWorkflow = true };
        var root = Sequence("root", endpoint, Activity("write", "Elsa.WriteLine", ("text", Captured("body"))));

        var result = await ConvertAsync(root, new Elsa3Variable { Id = "body", Name = "body", TypeName = "Object" });

        var diagnostic = Assert.Single(result.Diagnostics, item => item.Code == Elsa3ImportDiagnosticCodes.UntypedResultIntoTypedInput);
        Assert.Contains("HTTP endpoint 'endpoint'", diagnostic.Message);
    }

    [Fact]
    public async Task A_typed_variable_is_not_an_untyped_result()
    {
        // Elsa 3 converted the payload when it wrote the typed variable, and Elsa 4 reads that variable.
        var root = Sequence("root",
            Activity("wait", "Elsa.Event", ("eventName", Literal("Approved")), ("result", Captured("payload"))),
            Activity("write", "Elsa.WriteLine", ("text", Captured("payload"))));

        var result = await ConvertAsync(root, new Elsa3Variable { Id = "payload", Name = "payload", TypeName = "String" });

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
    }

    [Fact]
    public async Task A_loop_item_captured_into_a_typed_variable_is_still_refused()
    {
        // Elsa 4 reads the loop's own currentItem, not the typed variable, so Elsa 3's conversion on capture is lost.
        var loop = ForEach(Activity("inside", "Elsa.WriteLine", ("text", Captured("item"))));

        var result = await ConvertAsync(Sequence("root", loop), new Elsa3Variable { Id = "item", Name = "item", TypeName = "String" });

        Assert.Contains(result.Diagnostics, item => item.Code == Elsa3ImportDiagnosticCodes.UntypedResultIntoTypedInput);
    }

    [Fact]
    public async Task A_variable_with_no_type_is_untyped()
    {
        var root = Sequence("root",
            Activity("wait", "Elsa.Event", ("eventName", Literal("Approved")), ("result", Captured("payload"))),
            Activity("decide", "Elsa.FlowDecision", ("condition", Captured("payload", "Boolean"))));

        var result = await ConvertAsync(root, new Elsa3Variable { Id = "payload", Name = "payload", TypeName = "" });

        Assert.Contains(result.Diagnostics, item => item.Code == Elsa3ImportDiagnosticCodes.UntypedResultIntoTypedInput);
    }

    [Fact]
    public async Task A_loop_slot_read_after_its_loop_into_a_typed_input_is_refused()
    {
        var loop = Activity("loop", "Elsa.ForEach", ("items", Literal("[1,2]", "Object")), ("currentValue", Captured("item-slot")));
        var after = Activity("after", "Elsa.WriteLine", ("text", Captured("item-slot")));

        var result = await ConvertAsync(Sequence("root", loop, after));

        Assert.Contains(result.Diagnostics, item => item.Code == Elsa3ImportDiagnosticCodes.UntypedResultIntoTypedInput);
    }

    [Fact]
    public async Task A_typed_result_captured_into_an_untyped_variable_is_not_checked()
    {
        // ReadLine's result is a string in Elsa 3 and Elsa 4; the Object variable carries it as it is.
        var root = Sequence("root",
            Activity("read", "Elsa.ReadLine", ("result", Captured("line"))),
            Activity("write", "Elsa.WriteLine", ("text", Captured("line"))));

        var result = await ConvertAsync(root, new Elsa3Variable { Id = "line", Name = "line", TypeName = "Object" });

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
    }

    [Fact]
    public async Task A_for_index_read_into_a_string_input_is_refused()
    {
        // Elsa 3 wrote "0"; Elsa 4's index is an Int32, which has no conversion into text.
        var loop = For(Activity("inside", "Elsa.WriteLine", ("text", Captured("i"))));

        var result = await ConvertAsync(Sequence("root", loop), new Elsa3Variable { Id = "i", Name = "i", TypeName = "Object" });

        var diagnostic = Assert.Single(result.Diagnostics, item => item.Code == Elsa3ImportDiagnosticCodes.UntypedResultIntoTypedInput);
        Assert.Contains("current index of loop 'loop'", diagnostic.Message);
        Assert.Contains("String(…)", diagnostic.Guidance);
    }

    [Fact]
    public async Task A_for_index_read_into_an_integer_input_converts()
    {
        var inner = Activity("inner", "Elsa.For", ("start", Literal("0", "Int32")), ("end", Captured("i", "Int32")));
        var loop = For(inner);

        var result = await ConvertAsync(Sequence("root", loop), new Elsa3Variable { Id = "i", Name = "i", TypeName = "Object" });

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
    }

    [Theory]
    [InlineData("Object")]
    [InlineData("String")]
    public async Task A_for_index_read_by_a_Variable_expression_into_text_is_refused(string variableType)
    {
        var loop = For(Activity("inside", "Elsa.WriteLine", ("text", VariableExpression("i", "String"))));

        var result = await ConvertAsync(Sequence("root", loop), new Elsa3Variable { Id = "i", Name = "i", TypeName = variableType });

        var diagnostic = Assert.Single(result.Diagnostics, item => item.Code == Elsa3ImportDiagnosticCodes.UntypedResultIntoTypedInput);
        Assert.Contains("current index of loop 'loop'", diagnostic.Message);
        Assert.Contains("String(…)", diagnostic.Guidance);
        Assert.EndsWith(".text.expression.value.id", diagnostic.Path, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("'true'", false)]
    [InlineData("getVariable('flag')", false)]
    public async Task A_captured_result_read_by_a_Variable_expression_keeps_the_producer_kind_check(string script, bool converts)
    {
        var root = Sequence("root",
            Activity("calc", "Elsa.RunJavaScript", ("script", Literal(script)), ("result", Captured("ok"))),
            Activity("decide", "Elsa.FlowDecision", ("condition", VariableExpression("ok", "Boolean"))));

        var result = await ConvertAsync(root,
            new Elsa3Variable { Id = "ok", Name = "ok", TypeName = "Object" },
            new Elsa3Variable { Id = "flag", Name = "flag", TypeName = "Boolean" });

        Assert.Equal(converts, result.Succeeded);
        Assert.Equal(!converts, result.Diagnostics.Any(item => item.Code == Elsa3ImportDiagnosticCodes.UntypedResultIntoTypedInput));
    }

    [Fact]
    public async Task A_for_index_read_by_a_Variable_expression_into_an_integer_input_converts()
    {
        var inner = Activity("inner", "Elsa.For", ("start", Literal("0", "Int32")), ("end", VariableExpression("i", "Int32")));

        var result = await ConvertAsync(Sequence("root", For(inner)), new Elsa3Variable { Id = "i", Name = "i", TypeName = "Object" });

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
    }

    [Theory]
    [InlineData("x > 1", true)]
    [InlineData("getVariable('flag')", false)]
    public async Task A_script_result_captured_into_a_variable_is_checked_like_a_direct_read(string script, bool converts)
    {
        var root = Sequence("root",
            Activity("calc", "Elsa.RunJavaScript", ("script", Literal(script)), ("result", Captured("ok"))),
            Activity("decide", "Elsa.FlowDecision", ("condition", Captured("ok", "Boolean"))));

        var result = await ConvertAsync(root,
            new Elsa3Variable { Id = "ok", Name = "ok", TypeName = "Object" },
            new Elsa3Variable { Id = "flag", Name = "flag", TypeName = "Boolean" });

        Assert.Equal(!converts, result.Diagnostics.Any(item => item.Code == Elsa3ImportDiagnosticCodes.UntypedResultIntoTypedInput));
    }

    [Fact]
    public void Non_script_guidance_states_the_kind_in_an_expression_and_never_advises_rounding_a_decimal()
    {
        Assert.Contains("JavaScript expression", Elsa3ScriptResultKinds.ReadGuidance(Elsa3ScalarTarget.String));
        Assert.Contains("Math.round", Elsa3ScriptResultKinds.ReadGuidance(Elsa3ScalarTarget.Integer));
        Assert.DoesNotContain("Math.round", Elsa3ScriptResultKinds.ReadGuidance(Elsa3ScalarTarget.Decimal));
    }

    private static Elsa3Activity ForEach(Elsa3Activity body)
    {
        var loop = Activity("loop", "Elsa.ForEach", ("items", Literal("[1,2]", "Object")), ("currentValue", Captured("item")));
        loop.AdditionalProperties["body"] = Port(body);
        return loop;
    }

    private static Elsa3Activity For(Elsa3Activity body)
    {
        var loop = Activity("loop", "Elsa.For", ("start", Literal("0", "Int32")), ("end", Literal("2", "Int32")), ("currentValue", Captured("i")));
        loop.AdditionalProperties["body"] = Port(body);
        return loop;
    }

    private static Task<Elsa3MigrationResult<Conversion.Elsa3ConvertedDefinition>> ConvertAsync(Elsa3Activity root, params Elsa3Variable[] variables) =>
        ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(root, variables: variables));

    private static JsonElement VariableExpression(string id, string typeName) => JsonSerializer.SerializeToElement(new
    {
        typeName,
        expression = new { type = "Variable", value = new { id } }
    });

    [Theory]
    [InlineData("typed")]
    [InlineData("unclassified")]
    [InlineData("explicit-write")]
    [InlineData("script-write")]
    public async Task A_captured_variable_needs_positive_compatible_kind_evidence(string scenario)
    {
        var producer = scenario == "typed"
            ? Activity("calc", "Elsa.ReadLine", ("result", Captured("ok")))
            : Activity("calc", "Elsa.RunJavaScript", ("script", Literal(scenario == "unclassified" ? "42" : "true")), ("result", Captured("ok")));
        var consumer = scenario == "unclassified"
            ? Activity("consume", "Elsa.Delay", ("timeSpan", VariableExpression("ok", "TimeSpan")))
            : Activity("consume", "Elsa.FlowDecision", ("condition", VariableExpression("ok", "Boolean")));
        var children = new List<Elsa3Activity> { producer };
        if (scenario == "explicit-write")
            children.Add(Activity("overwrite", "Elsa.SetVariable",
                ("variable", JsonSerializer.SerializeToElement(new { id = "ok" })), ("value", Literal("not boolean"))));
        if (scenario == "script-write")
            children.Add(Activity("overwrite", "Elsa.RunJavaScript", ("script", Literal("const name = 'ok'; setVariable(name, 'not boolean'); return 1;"))));
        children.Add(consumer);

        var result = await ConvertAsync(Sequence("root", children.ToArray()), new Elsa3Variable { Id = "ok", Name = "ok", TypeName = "Object" });

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, item => item.Code == Elsa3ImportDiagnosticCodes.IncompatibleValue);
    }

    [Fact]
    public async Task A_computed_script_write_to_a_loop_capture_does_not_become_a_read_of_the_unchanged_index()
    {
        var write = Activity("write", "Elsa.RunJavaScript", ("script", Literal("const name = 'i'; setVariable(name, 99); return 1;")));
        var read = Activity("read", "Elsa.PublishEvent", ("eventName", Literal("e")), ("payload", VariableExpression("i", "Object")));

        var result = await ConvertAsync(Sequence("root", For(Sequence("body", write, read))), new Elsa3Variable { Id = "i", Name = "i", TypeName = "Int32" });

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, item => item.Code == Elsa3ImportDiagnosticCodes.AmbiguousReference);
    }
}
