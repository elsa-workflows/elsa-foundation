using System.Text.Json;
using Xunit;

namespace Elsa.Expressions.JavaScript.Jint.Tests;

public sealed partial class ScriptValueAdapterTests
{
    [Fact]
    public async Task An_inherited_boxed_value_does_not_replace_or_coerce_a_sparse_array_hole()
    {
        var sink = new RecordingWarningSink();
        var value = await ExpressionAsync("(() => { const n = new Number(1); n.valueOf = () => { throw new Error('coerced hole'); }; Array.prototype[0] = n; const a = [,]; a.toJSON = () => a; return a; })()", sink: sink);
        Assert.Equal("[null]", value.GetRawText());
        Assert.Equal("VF-UNDEF-003", Assert.Single(sink.Warnings).Code);
    }

    [Fact]
    public async Task Undefined_as_null_retains_warnings_from_lossy_reads()
    {
        var sink = new RecordingWarningSink();
        var value = await ExpressionAsync("const id = variables.order.id; undefined;", sink: sink, undefinedAsNull: true);
        Assert.Equal(JsonValueKind.Null, value.ValueKind);
        Assert.Contains(sink.Warnings, warning => warning.Code == "VF-PREC-002");
    }

    [Fact]
    public async Task A_root_toJSON_that_produces_undefined_obeys_the_same_result_policy()
    {
        const string source = "({toJSON(){return undefined;}})";
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => ExpressionAsync(source));
        Assert.Contains("VF-UNDEF-001", exception.Message);
        Assert.Equal(JsonValueKind.Null, (await ExpressionAsync(source, undefinedAsNull: true)).ValueKind);
    }

    [Theory]
    [InlineData("({n:new Number(NaN)})")]
    [InlineData("new Number(Infinity)")]
    [InlineData("new Number(-Infinity)")]
    public async Task Boxed_non_finite_numbers_are_diagnosed_after_unboxing(string source)
    {
        var sink = new RecordingWarningSink();
        await ExpressionAsync(source, sink: sink);
        Assert.Equal("VF-NUM-001", Assert.Single(sink.Warnings).Code);
    }

    [Theory]
    [InlineData(false, "{\"b\":null,\"c\":[1,null,null]}", 4)]
    [InlineData(true, "{\"a\":null,\"b\":null,\"c\":[1,null,null]}", 3)]
    public async Task Boundary_policies_keep_sparse_positions_and_diagnose_every_loss(bool policy, string expected, int count)
    {
        var sink = new RecordingWarningSink();
        var value = await ExpressionAsync("({ a: undefined, b: 0/0, c: [1, , undefined] })", sink: sink, undefinedAsNull: policy);
        Assert.Equal(expected, value.GetRawText());
        Assert.Equal(count, sink.Warnings.Count);
        Assert.Equal(2, sink.Warnings.Count(warning => warning.Code == "VF-UNDEF-003"));
        Assert.Single(sink.Warnings, warning => warning.Code == "VF-NUM-001");
        Assert.Equal(!policy, sink.Warnings.Any(warning => warning.Code == "VF-UNDEF-002"));
        Assert.Contains(sink.Warnings, warning => warning.Message.Contains("result.c[1]", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("0/0")]
    [InlineData("1/0")]
    [InlineData("-1/0")]
    public async Task A_top_level_computed_non_finite_number_is_not_silently_written(string source)
    {
        var sink = new RecordingWarningSink();
        var value = await ExpressionAsync(source, sink: sink);
        Assert.Equal(JsonValueKind.Null, value.ValueKind);
        Assert.Equal("VF-NUM-001", Assert.Single(sink.Warnings).Code);
    }

    [Theory]
    [InlineData("undefined")]
    [InlineData("let unused = 1;")]
    public async Task Undefined_binding_results_follow_the_workflow_policy(string source)
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => ExpressionAsync(source));
        Assert.Contains("VF-UNDEF-001", exception.Message);
        Assert.Equal(JsonValueKind.Null, (await ExpressionAsync(source, undefinedAsNull: true)).ValueKind);
    }

    [Fact]
    public async Task A_binding_completion_value_does_not_need_return() =>
        Assert.Equal(42, (await ExpressionAsync("const answer = 42; answer;")).GetInt32());

    [Fact]
    public async Task A_string_produced_by_JSON_stringify_is_not_reinterpreted()
    {
        var sink = new RecordingWarningSink();
        var value = await ExpressionAsync("JSON.stringify({a: undefined, b: NaN})", sink: sink);
        Assert.Equal(JsonValueKind.String, value.ValueKind);
        Assert.Equal("{\"b\":null}", value.GetString());
        Assert.Empty(sink.Warnings);
    }

    [Fact]
    public async Task Deep_undefined_has_a_precise_path_and_a_root_only_protected_form()
    {
        var sink = new RecordingWarningSink();
        await ExpressionAsync("({ customer: { privateKey: { value: undefined } } })", sink: sink);
        var warning = Assert.Single(sink.Warnings);
        Assert.Contains("result.customer.privateKey.value", warning.Message);
        Assert.NotNull(warning.ProtectedForm);
        Assert.DoesNotContain("customer", warning.ProtectedForm.Message);
        Assert.Contains("result", warning.ProtectedForm.Message);
    }
}
