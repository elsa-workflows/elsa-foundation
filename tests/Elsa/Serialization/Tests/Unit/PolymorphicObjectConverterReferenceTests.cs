using Elsa.Serialization.SystemText.JsonConverters;
using Elsa.Serialization.SystemText.Services;
using System.Dynamic;
using System.Text.Json;
using Xunit;

namespace Elsa.Serialization.Tests.Unit;

/// <summary>
/// #409: the converter's $ref/$id resolution machinery depended on CrossScopedReferenceHandler, which was
/// never instantiated or assigned anywhere — every cast yielded null, so reference metadata was never
/// reconstructed. The dead machinery has been removed; these tests pin the wire shape and the observable
/// read behavior so the removal is provably behavior-preserving (constitution §E6: `$id`/`$ref`/`$values`/
/// `_type`/`_items` are frozen wire identifiers; cyclic/shared references are NOT reconstructed).
///
/// The type discriminator is a registry alias resolved through <see cref="TypeJsonConverter"/> — the
/// assembly-qualified-name + Type.GetType fallback was removed (ADR 0035 D1/D4), so these options register a
/// <see cref="TypeJsonConverter"/> over a registry seeded with the aliases the wire shape references.
/// </summary>
public sealed class PolymorphicObjectConverterReferenceTests
{
    private readonly JsonSerializerOptions _options;

    public PolymorphicObjectConverterReferenceTests()
    {
        var typeRegistry = new WellKnownTypeRegistry();
        typeRegistry.RegisterType(typeof(string), "String");

        _options = new JsonSerializerOptions
        {
            Converters =
            {
                new PolymorphicObjectConverterFactory(typeRegistry),
                new TypeJsonConverter(typeRegistry),
            }
        };
    }

    [Fact]
    public void Write_RepresentativeGraph_ProducesStableWireShape()
    {
        IDictionary<string, object?> child = new ExpandoObject();
        child["city"] = "Amsterdam";

        IDictionary<string, object?> root = new ExpandoObject();
        root["name"] = "Order-1";
        root["quantity"] = 42;
        root["tags"] = new List<string> { "a", "b" };
        root["child"] = child;

        var json = JsonSerializer.Serialize((object)root, _options);

        // Members are emitted in ordinal-by-name order (child, name, quantity, tags), not insertion order:
        // the serializer is deterministic (spec 086; ADR 0034 D3/D8), so equal graphs serialize identically.
        Assert.Equal(
            """{"child":{"city":"Amsterdam"},"name":"Order-1","quantity":42,"tags":{"_items":["a","b"],"_type":"List\u003CString\u003E"}}""",
            json);

        // And the graph must round-trip through the read path.
        var roundTripped = Assert.IsAssignableFrom<IDictionary<string, object>>(JsonSerializer.Deserialize<object>(json, _options));
        Assert.Equal("Order-1", roundTripped["name"]);
        Assert.Equal(42L, roundTripped["quantity"]);
        Assert.Equal(["a", "b"], Assert.IsType<List<string>>(roundTripped["tags"]));
        Assert.Equal("Amsterdam", Assert.IsAssignableFrom<IDictionary<string, object>>(roundTripped["child"])["city"]);
    }

    [Fact]
    public void RefOnlyObject_DeserializesToRawReferenceId()
    {
        // Without a wired reference resolver, a {"$ref": ...} object resolves to the raw id string.
        var result = JsonSerializer.Deserialize<object>("""{"$ref":"1"}""", _options);

        Assert.Equal("1", result);
    }

    [Fact]
    public void IdProperty_IsPreservedAsOrdinaryData()
    {
        var result = JsonSerializer.Deserialize<object>("""{"$id":"1","name":"a"}""", _options);

        var dict = Assert.IsAssignableFrom<IDictionary<string, object>>(result);
        Assert.Equal("1", dict["$id"]);
        Assert.Equal("a", dict["name"]);
    }

    [Fact]
    public void TypedCollectionRefWrapper_DeserializesToNull()
    {
        // A typed collection wrapper (resolved via its registry alias) carrying only $ref cannot be
        // reconstructed and yields null.
        var json = """{"_type":"List<String>","$ref":"1"}""";

        var result = JsonSerializer.Deserialize<object>(json, _options);

        Assert.Null(result);
    }
}
