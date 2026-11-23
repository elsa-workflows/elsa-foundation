using System.Text.Json;
using Xunit;

namespace Elsa.Expressions.JavaScript.Jint.Tests;

public sealed partial class ScriptValueAdapterTests
{
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
