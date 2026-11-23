using System.Diagnostics.Metrics;
using System.Text.Json;
using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Expressions.JavaScript.Jint.Contracts;
using Elsa.Expressions.JavaScript.Jint.Options;
using Elsa.Expressions.JavaScript.Jint.Services;
using Jint;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Expressions.JavaScript.Jint.Tests;

/// <summary>
/// The default script value bridge, row by row of the "Out of JavaScript" table in
/// <c>specs/1228-value-model-conversion/contracts/javascript-adapter.md</c> (T008, T014a, T027 at bridge level).
/// </summary>
public sealed class CopyComparePatchValueBridgeTests
{
    private const string Order = """{"id":9007199254740993,"price":10.50,"Name":"Ann","items":[1,2],"meta":{"tag":"a","n":2.0}}""";

    [Fact]
    public void A_read_writes_nothing()
    {
        var change = Single("const total = variables.order.price * 2; JSON.stringify(variables.order);", Order);

        Assert.Equal(ScriptValueChangeKind.Unchanged, change.Kind);
        Assert.Null(change.Value);
    }

    [Fact]
    public void A_member_change_keeps_every_other_member_as_stored()
    {
        var change = Single("variables.order.price = 20;", Order);

        Assert.Equal(ScriptValueChangeKind.MemberChange, change.Kind);
        Assert.Equal(["variables.order.price"], change.ChangedPaths);
        Assert.Equal("""{"id":9007199254740993,"price":20,"Name":"Ann","items":[1,2],"meta":{"tag":"a","n":2.0}}""", change.Value!.Value.GetRawText());
        Assert.Empty(change.Losses);
    }

    [Fact]
    public void A_nested_member_change_keeps_its_siblings_text()
    {
        var change = Single("variables.order.meta.tag = 'b';", Order);

        Assert.Equal(["variables.order.meta.tag"], change.ChangedPaths);
        Assert.Equal("""{"id":9007199254740993,"price":10.50,"Name":"Ann","items":[1,2],"meta":{"tag":"b","n":2.0}}""", change.Value!.Value.GetRawText());
    }

    [Fact]
    public void A_whole_assignment_replaces()
    {
        var change = Single("variables.order = { total: 20 };", Order);

        Assert.Equal(ScriptValueChangeKind.WholeAssignment, change.Kind);
        Assert.Equal("""{"total":20}""", change.Value!.Value.GetRawText());
    }

    [Fact]
    public void A_changed_array_is_written_whole()
    {
        var change = Single("variables.list.push(3);", "[1,2]");

        Assert.Equal(ScriptValueChangeKind.WholeAssignment, change.Kind);
        Assert.Equal("[1,2,3]", change.Value!.Value.GetRawText());
    }

    [Fact]
    public void A_changed_array_inside_an_object_is_a_member_written_whole()
    {
        var change = Single("variables.order.items.push(3);", Order);

        Assert.Equal(ScriptValueChangeKind.MemberChange, change.Kind);
        Assert.Equal(["variables.order.items"], change.ChangedPaths);
        Assert.Contains("\"items\":[1,2,3]", change.Value!.Value.GetRawText());
        Assert.Contains("9007199254740993", change.Value!.Value.GetRawText());
    }

    [Fact]
    public void A_member_set_to_an_equal_JavaScript_value_is_unchanged()
    {
        // ADR 0090 C2: …992 equals the stored …993 as a JavaScript value, so nothing is written (the accepted gap).
        var change = Single("variables.order.id = 9007199254740992; variables.order.price = 10.5;", Order);

        Assert.Equal(ScriptValueChangeKind.Unchanged, change.Kind);
    }

    [Fact]
    public void A_string_forces_the_write()
    {
        var change = Single("variables.order.id = '9007199254740993';", Order);

        Assert.Equal(ScriptValueChangeKind.MemberChange, change.Kind);
        Assert.StartsWith("""{"id":"9007199254740993","price":10.50""", change.Value!.Value.GetRawText());
    }

    [Fact]
    public void Deleting_a_member_is_a_member_change()
    {
        var change = Single("delete variables.order.Name;", Order);

        Assert.Equal(["variables.order.Name"], change.ChangedPaths);
        Assert.DoesNotContain("Name", change.Value!.Value.GetRawText());
    }

    [Fact]
    public void Key_order_and_casing_are_kept()
    {
        var change = Single("variables.order.a = 3; variables.order.C = 4;", """{"B":1,"a":2}""");

        Assert.Equal("""{"B":1,"a":3,"C":4}""", change.Value!.Value.GetRawText());
    }

    [Fact]
    public void Changing_a_local_copy_is_no_change()
    {
        var change = Single("const copy = { ...variables.order }; copy.price = 1; copy.meta = {};", Order);

        Assert.Equal(ScriptValueChangeKind.Unchanged, change.Kind);
    }

    [Fact]
    public void A_change_changed_back_is_no_change()
    {
        var change = Single("variables.order.price = 1; variables.order.price = 10.5;", Order);

        Assert.Equal(ScriptValueChangeKind.Unchanged, change.Kind);
    }

    [Fact]
    public void Every_read_path_gives_the_same_value()
    {
        var change = Single("getVariable('order').price = 1; variables['order'].Name = 'Bo'; variables.order.price;", Order);

        Assert.Equal(["variables.order.price", "variables.order.Name"], change.ChangedPaths);
    }

    [Fact]
    public void A_variable_assigned_but_never_read_is_a_whole_assignment()
    {
        using var run = Run("variables.fresh = 1;", ("order", Order));

        var (name, change) = Assert.Single(run.Changes);
        Assert.Equal("fresh", name);
        Assert.Equal(ScriptValueChangeKind.WholeAssignment, change.Kind);
        Assert.Equal("1", change.Value!.Value.GetRawText());
    }

    [Fact]
    public void A_variable_never_read_is_not_touched()
    {
        using var run = Run("variables.order.price;", ("order", Order), ("other", "{\"x\":1}"));

        Assert.Equal(["order"], run.Changes.Select(change => change.Name));
    }

    [Fact]
    public void A_value_returned_unchanged_is_its_original_JSON_and_carries_no_loss()
    {
        using var run = Run("variables.order", ("order", Order));

        var write = run.Values.Write(run.Returned, "result");
        Assert.Equal(Order, write.Value!.Value.GetRawText());
        Assert.Empty(write.Losses);
    }

    [Fact]
    public void An_unchanged_value_placed_inside_a_new_value_keeps_its_original_JSON()
    {
        using var run = Run("({ wrapped: variables.order, n: 1 })", ("order", Order));

        var write = run.Values.Write(run.Returned, "result");
        Assert.Equal($$"""{"wrapped":{{Order}},"n":1}""", write.Value!.Value.GetRawText());
        Assert.Empty(write.Losses);
    }

    [Fact]
    public void A_copied_large_id_carries_its_loss()
    {
        using var run = Run("({ id: variables.order.id })", ("order", Order));

        var write = run.Values.Write(run.Returned, "result");
        var loss = Assert.Single(write.Losses);
        Assert.Equal("result.id", loss.Path);
        Assert.Equal("variables.order.id", loss.Entered.Path);
        Assert.Equal("9007199254740993", loss.Entered.Stored);
        Assert.Equal("9007199254740992", loss.Written);
        Assert.Null(loss.WholeArrayPath);
    }

    [Fact]
    public void A_lossy_element_of_an_array_written_whole_names_the_array()
    {
        using var run = Run("variables.ids.push(1); variables.ids", ("ids", "[9007199254740993, 5]"));

        var write = run.Values.Write(run.Returned, "result");
        var loss = Assert.Single(write.Losses);
        Assert.Equal("result[0]", loss.Path);
        Assert.Equal("result", loss.WholeArrayPath);

        var (_, change) = Assert.Single(run.Changes);
        Assert.Equal(ScriptValueChangeKind.WholeAssignment, change.Kind);
        Assert.Equal("variables.ids", Assert.Single(change.Losses).WholeArrayPath);
    }

    [Fact]
    public void A_decimal_written_in_another_notation_is_no_loss()
    {
        using var run = Run("variables.order.price", ("order", Order));

        var write = run.Values.Write(run.Returned, "result");
        Assert.Equal("10.5", write.Value!.Value.GetRawText());
        Assert.Empty(write.Losses);
    }

    [Fact]
    public void A_large_number_held_as_a_string_is_never_a_loss()
    {
        using var run = Run("({ id: variables.order.id })", ("order", """{"id":"9007199254740993"}"""));

        var write = run.Values.Write(run.Returned, "result");
        Assert.Equal("""{"id":"9007199254740993"}""", write.Value!.Value.GetRawText());
        Assert.Empty(write.Losses);
        Assert.Empty(run.Values.LossyNumbers);
    }

    [Fact]
    public void A_lossy_number_is_recorded_when_it_enters_and_found_by_value()
    {
        using var run = Run("variables.order.price", ("order", Order));

        var lossy = Assert.Single(run.Values.LossyNumbers);
        Assert.Equal("variables.order.id", lossy.Path);
        Assert.True(lossy.IsInteger);
        Assert.True(run.Values.TryFindLoss(9007199254740992d, out var found));
        Assert.Same(lossy, found);
        Assert.False(run.Values.TryFindLoss(10.5, out _));
    }

    [Fact]
    public void A_value_holding_a_number_too_large_for_a_double_passes_through_unchanged_and_exactly()
    {
        // FR-011: 1e400 becomes Infinity, which JSON writes as null. Unchanged, it is still written from its original JSON.
        using var run = Run("variables.order.b;", ("order", """{"big":1e400,"b":1}"""));

        Assert.Equal(ScriptValueChangeKind.Unchanged, Assert.Single(run.Changes).Change.Kind);
        var write = run.Values.Write(run.Values.Enter("probe", JsonSerializer.Deserialize<JsonElement>("""{"big":-1e400}""")), "result");
        Assert.Equal("""{"big":-1e400}""", write.Value!.Value.GetRawText());
        Assert.Empty(write.Losses);
    }

    [Theory]
    [InlineData("variables.order.big")]
    [InlineData("({ v: variables.order.big })")]
    public void A_number_that_became_infinity_and_is_written_is_a_loss_not_a_silent_null(string script)
    {
        using var run = Run(script, ("order", """{"big":1e400,"b":1}"""));

        var write = run.Values.Write(run.Returned, "result");
        var loss = Assert.Single(write.Losses);
        Assert.Equal("1e400", loss.Entered.Stored);
        Assert.Equal("null", loss.Written);
    }

    [Theory]
    [InlineData("-variables.order.big")]
    [InlineData("-1/0")]
    [InlineData("({ v: -1/0 })")]
    public void An_infinity_no_entered_number_lost_to_is_written_as_null_not_as_its_marker(string script)
    {
        // Only +Infinity entered (1e400), so -Infinity carries no loss, yet JSON still writes it as null.
        using var run = Run(script, ("order", """{"big":1e400,"b":1}"""));

        var write = run.Values.Write(run.Returned, "result");

        Assert.Empty(write.Losses);
        Assert.DoesNotContain("inf", write.Value!.Value.GetRawText());
        Assert.Contains("null", write.Value!.Value.GetRawText());
    }

    [Theory]
    [InlineData("variables.order.items[0].id", "variables.order")]
    [InlineData("variables[\"a.b\"].x", "variables[\"a.b\"]")]
    [InlineData("variables[\"q\\\"]\"][1]", "variables[\"q\\\"]\"]")]
    [InlineData("variables.order", "variables.order")]
    [InlineData("args.balances[\"NL91\"]", "args")]
    [InlineData("result", "result")]
    public void A_value_free_path_names_the_root_only(string path, string root) =>
        Assert.Equal(root, Elsa.Expressions.JavaScript.Jint.Services.ValuePaths.Root(path));

    [Fact]
    public void A_value_made_unserializable_is_reported_changed_as_a_whole()
    {
        using var run = Run("variables.order.self = variables.order;", ("order", Order));

        var change = Assert.Single(run.Changes).Change;
        Assert.Equal(ScriptValueChangeKind.WholeAssignment, change.Kind);
        Assert.Equal(["variables.order"], change.ChangedPaths);
    }

    [Fact]
    public void The_bridge_records_its_cost_per_evaluation()
    {
        var measured = new List<string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == JavaScriptValueBridgeMetrics.MeterName)
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((instrument, _, _, _) => { lock (measured) measured.Add(instrument.Name); });
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) => { lock (measured) measured.Add(instrument.Name + ":" + value); });
        listener.Start();

        using (var run = Run("variables.order.price = 1;", ("order", Order)))
        {
            _ = run.Changes;
        }

        lock (measured)
        {
            Assert.Contains(JavaScriptValueBridgeMetrics.EnterDuration, measured);
            Assert.Contains(JavaScriptValueBridgeMetrics.CollectDuration, measured);
            Assert.Contains(measured, name => name.StartsWith(JavaScriptValueBridgeMetrics.BytesConverted + ":", StringComparison.Ordinal) && !name.EndsWith(":0", StringComparison.Ordinal));
        }
    }

    private static ScriptValueChange Single(string script, string json)
    {
        var name = json.StartsWith('[') ? "list" : "order";
        using var run = Run(script, (name, json));
        return Assert.Single(run.Changes).Change;
    }

    private static ScriptRun Run(string script, params (string Name, string Json)[] variables)
    {
        var options = new FeatureOptions();
        var engine = IsolatedJintEngine.Create(options, CancellationToken.None);
        var values = new CopyComparePatchValueBridge(Microsoft.Extensions.Options.Options.Create(options)).Begin(engine);
        var pinned = variables.ToDictionary(variable => variable.Name, variable => JsonSerializer.Deserialize<JsonElement>(variable.Json), StringComparer.Ordinal);
        var root = IsolatedJintEngine.SetVariables(engine, values, pinned, reader: null, options)!;
        var returned = engine.Evaluate("'use strict';\n" + script);
        return new ScriptRun(values, root, returned);
    }

    private sealed class ScriptRun(IScriptValueEvaluation values, IsolatedJintEngine.ScriptVariables root, global::Jint.Native.JsValue returned) : IDisposable
    {
        private IReadOnlyList<(string Name, ScriptValueChange Change)>? _changes;
        public IScriptValueEvaluation Values => values;
        public global::Jint.Native.JsValue Returned => returned;
        public IReadOnlyList<(string Name, ScriptValueChange Change)> Changes => _changes ??= root.CollectForRefusal();
        public void Dispose() => values.Dispose();
    }
}
