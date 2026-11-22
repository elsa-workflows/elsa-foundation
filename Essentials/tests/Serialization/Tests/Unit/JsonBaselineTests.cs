using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Serialization.Core;
using Xunit;

namespace Elsa.Serialization.Tests.Unit;

public sealed class JsonBaselineTests
{
    [Fact]
    public void Baseline_is_immutable_and_copies_are_independent()
    {
        Assert.True(JsonBaseline.Options.IsReadOnly);
        Assert.Throws<InvalidOperationException>(() => JsonBaseline.Options.PropertyNameCaseInsensitive = true);
        var copy = JsonBaseline.CreateCopy();
        copy.PropertyNameCaseInsensitive = true;
        Assert.False(JsonBaseline.Options.PropertyNameCaseInsensitive);
        Assert.False(JsonBaseline.CreateCopy().PropertyNameCaseInsensitive);
    }

    [Fact]
    public void Own_contracts_use_exact_camel_case_keep_nulls_and_dictionary_names()
    {
        var value = new Example { Text = "René <+>", Values = new() { ["OrderTotal"] = null } };
        Assert.Equal("{\"text\":\"René <+>\",\"values\":{\"OrderTotal\":null}}", JsonSerializer.Serialize(value, JsonBaseline.Options));
        Assert.Null(JsonSerializer.Deserialize<Example>("{\"Text\":\"ignored\"}", JsonBaseline.Options)!.Text);
        Assert.Equal("kept", JsonSerializer.Deserialize<Example>("{\"text\":\"kept\"}", JsonBaseline.Options)!.Text);
        Assert.Equal(JsonNumberHandling.Strict, JsonBaseline.Options.NumberHandling);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<int>("\"42\"", JsonBaseline.Options));
    }

    [Theory]
    [InlineData("{ \"Name\" : \"Ren\\u00e9\", \"N\" : 1.0, \"Big\":9007199254740993, \"Path\":\"a\\/b\" }")]
    [InlineData("[ 1e400, 0.100, null, { \"Z\" : \"<+>\" } ]")]
    [InlineData("null")]
    public void Carried_values_keep_their_exact_inner_text_even_in_indented_documents(string raw)
    {
        var value = JsonSerializer.Deserialize<JsonElement>(raw, JsonBaseline.Options);
        Assert.Equal(raw, JsonSerializer.Serialize(value, JsonBaseline.Options));
        var options = JsonBaseline.CreateCopy();
        options.WriteIndented = true;
        var written = JsonSerializer.Serialize(new { Value = value }, options);
        Assert.Contains(raw, written, StringComparison.Ordinal);
        Assert.Equal(raw, JsonSerializer.Deserialize<JsonElement>(written, options).GetProperty("value").GetRawText());
    }

    private sealed class Example
    {
        public string? Text { get; set; }
        public Dictionary<string, string?>? Values { get; set; }
    }
}
