using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Serialization.Core;
using Xunit;

namespace Elsa.Serialization.Tests.Unit;

/// <summary>
/// #5803: a member's <see cref="JsonConverterAttribute"/> reads and writes that member's whole value at an input boundary
/// (<see cref="JsonConversionContract.For"/> with the member), just as System.Text.Json applies it to a nested property.
/// </summary>
public sealed class MemberJsonConverterContractTests
{
    [Fact]
    public void A_member_converter_reads_the_members_whole_value()
    {
        var contract = JsonConversionContract.For(typeof(string[]), Member(nameof(Holder.Tags)));

        Assert.Equal(["one"], (string[])contract.Read(Element("\"one\""), typeof(string[]))!);
        Assert.Equal(["one", "two"], (string[])contract.Read(Element("[\"one\",\"two\"]"), typeof(string[]))!);
    }

    [Fact]
    public void A_member_converter_writes_the_members_whole_value()
    {
        var contract = JsonConversionContract.For(typeof(string[]), Member(nameof(Holder.Tags)));

        Assert.Equal("[\"one\"]", contract.Write(new[] { "one" }, typeof(string[])).GetRawText());
    }

    [Fact]
    public void A_member_converter_also_applies_to_a_scalar_member()
    {
        var contract = JsonConversionContract.For(typeof(string), Member(nameof(Holder.Code)));

        Assert.Equal("CODE", contract.Read(Element("\"code\""), typeof(string)));
    }

    [Fact]
    public void The_type_contract_and_other_members_of_the_same_type_are_unchanged()
    {
        Assert.Throws<JsonException>(() => JsonConversionContract.For(typeof(string[])).Read(Element("\"one\""), typeof(string[])));
        Assert.Throws<JsonException>(() =>
            JsonConversionContract.For(typeof(string[]), Member(nameof(Holder.Plain))).Read(Element("\"one\""), typeof(string[])));
    }

    [Fact]
    public void A_member_converter_keeps_the_members_conversion_selection()
    {
        var contract = JsonConversionContract.For(typeof(string[]), Member(nameof(Holder.SelectedTags)));

        Assert.Equal(["one"], (string[])contract.Read(Element("\"one\""), typeof(string[]))!);
        Assert.True(contract.RefuseUnmatchedMembers);
    }

    [Fact]
    public void A_member_converter_that_cannot_convert_the_type_is_refused()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => JsonConversionContract.For(typeof(int), Member(nameof(Holder.Tags))));

        Assert.Contains(nameof(SingleOrArrayConverter), exception.Message);
    }

    private static System.Reflection.PropertyInfo Member(string name) => typeof(Holder).GetProperty(name)!;

    private static JsonElement Element(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    private sealed class Holder
    {
        [JsonConverter(typeof(SingleOrArrayConverter))]
        public string[]? Tags { get; set; }

        [JsonConverter(typeof(SingleOrArrayConverter))]
        [JsonConversion(RefuseUnmatchedMembers = true)]
        public string[]? SelectedTags { get; set; }

        [JsonConverter(typeof(UpperCaseConverter))]
        public string? Code { get; set; }

        public string[]? Plain { get; set; }
    }

    private sealed class SingleOrArrayConverter : JsonConverter<string[]>
    {
        public override string[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String) return [reader.GetString()!];
            if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Expected a string or an array.");
            var items = new List<string>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray) items.Add(reader.GetString()!);
            return [.. items];
        }

        public override void Write(Utf8JsonWriter writer, string[] value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (var item in value) writer.WriteStringValue(item);
            writer.WriteEndArray();
        }
    }

    private sealed class UpperCaseConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetString()!.ToUpperInvariant();

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue(value);
    }
}
