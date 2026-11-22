using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Serialization.Core;
using Xunit;

namespace Elsa.Serialization.Tests.Unit;

public sealed class JsonOpaqueHashProjectionTests
{
    private static object? NoAdapter(object _) => null;
    private static JsonElement Element(string json) { using var document = JsonDocument.Parse(json); return document.RootElement.Clone(); }
    private static string Hash<T>(T value) => JsonHashInput.Hash(JsonHashInput.ProjectOpaqueClr(value, NoAdapter));

    [Fact]
    public void Opaque_code_units_keys_null_and_empty_are_distinct()
    {
        var values = new[] { "\ud800", "\ud801", "\ufffd", "", null };
        Assert.Equal(values.Length, values.Select(value => Hash(new { value })).Distinct().Count());
        Assert.Equal(3, values.Take(3).Select(value => Hash(new Dictionary<string, string?> { [value!] = "value" })).Distinct().Count());
        Assert.Equal("ANg=", JsonHashInput.OpaqueString("\ud800"));
    }

    [Fact]
    public void Raw_JSON_is_projected_once_and_uses_exact_semantic_equality()
    {
        var first = Element("{ \"name\":\"Ren\\u00e9\", \"total\":1.0 }");
        var second = Element("{\"total\":1,\"name\":\"René\"}");
        var raw = first.GetRawText();
        Assert.Equal(Hash(new { payload = first }), Hash(new { payload = second }));
        Assert.Equal(raw, JsonHashInput.ProjectOpaqueClr(new { payload = first }, NoAdapter).GetProperty("payload").GetRawText());
        Assert.NotEqual(Hash(new { payload = Element("{}") }), Hash(new { payload = Element("{\"x\":null}") }));
        var once = JsonHashInput.ProjectOpaqueClr(new { payload = first }, NoAdapter);
        Assert.Equal(once.GetRawText(), JsonHashInput.ProjectOpaqueClr(new { payload = once }, NoAdapter).GetProperty("payload").GetRawText());
    }

    [Fact]
    public void CLR_object_slots_cannot_alias_raw_JSON_that_looks_like_the_encoding()
    {
        Assert.NotEqual(Hash(new Holder { Value = "secret" }), Hash(new Holder { Value = Element(JsonSerializer.Serialize(JsonHashInput.OpaqueString("secret"), JsonBaseline.Options)) }));
    }

    [Fact]
    public void Effective_names_omissions_ignored_conduits_and_polymorphic_discriminators_are_retained()
    {
        var first = new Projection { Name = "a", Hidden = "one" };
        var second = new Projection { Name = "a", Hidden = "two" };
        var projected = JsonHashInput.ProjectOpaqueClr(first, NoAdapter);
        Assert.True(projected.TryGetProperty("wireName", out _));
        Assert.False(projected.TryGetProperty("omitted", out _));
        Assert.False(projected.TryGetProperty("hidden", out _));
        Assert.Equal(Hash(first), Hash(second));
        var polymorphic = JsonHashInput.ProjectOpaqueClr(new Envelope { Value = new Derived { Name = "a", Count = 2 } }, NoAdapter);
        Assert.Equal("derived", polymorphic.GetProperty("value").GetProperty("kind").GetString());
        Assert.Equal(2, polymorphic.GetProperty("value").GetProperty("count").GetInt32());
    }

    [Fact]
    public void Unknown_converter_shapes_and_cycles_are_refused_until_the_owner_projects_them()
    {
        Assert.ThrowsAny<JsonException>(() => Hash(new Custom("\ud800")));
        var projected = JsonHashInput.ProjectOpaqueClr(new Custom("\ud800"), value => value is Custom custom ? new { Version = 2, custom.Name, ExplicitNull = (string?)null } : null);
        Assert.Equal(2, projected.GetProperty("version").GetInt32());
        Assert.Equal(JsonValueKind.Null, projected.GetProperty("explicitNull").ValueKind);
        var cycle = new Holder(); cycle.Value = cycle;
        Assert.ThrowsAny<JsonException>(() => Hash(cycle));
    }

    private sealed class Holder { public object? Value { get; set; } }
    private sealed class Projection
    {
        [JsonPropertyName("wireName")] public string? Name { get; init; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Omitted { get; init; }
        [JsonIgnore] public string? Hidden { get; init; }
    }
    private sealed class Envelope { public Base? Value { get; init; } }
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind"), JsonDerivedType(typeof(Derived), "derived")]
    private class Base { public string? Name { get; init; } }
    private sealed class Derived : Base { public int Count { get; init; } }
    [JsonConverter(typeof(CustomConverter))] private sealed record Custom(string Name);
    private sealed class CustomConverter : JsonConverter<Custom>
    {
        public override Custom? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new NotSupportedException();
        public override void Write(Utf8JsonWriter writer, Custom value, JsonSerializerOptions options) => throw new NotSupportedException();
    }
}
