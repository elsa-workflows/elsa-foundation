using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.JsonConverters;
using Elsa.Serialization.SystemText.Services;
using Xunit;

namespace Elsa.Serialization.Tests.Unit;

/// <summary>
/// Spec 086 (ADR 0034 D3/D8): the shared <see cref="IPayloadSerializer"/> must be deterministic — equal
/// object graphs serialize to byte-identical JSON regardless of dictionary insertion order or reflection
/// member order, and remain round-trip lossless. These tests are the regression guard demanded by FR-006
/// (byte-identity for equal-but-differently-ordered inputs; stable digest across process runs/hosts).
/// </summary>
public sealed class DeterministicSerializationTests
{
    /// <summary>
    /// Canonical SHA-256 (hex) of <see cref="BuildFixture"/>'s serialization. Because string hashing is
    /// seeded per process, any residual dependence on dictionary iteration order would change this digest
    /// from one run/host to the next — so its constancy across every CI run <em>is</em> the cross-process
    /// determinism assertion (SC-002 / FR-003). If a deliberate wire-shape change lands, recompute it.
    /// </summary>
    private const string CanonicalDigest = "1ea56144563e03c373da7df075bdaf8f8e5b10dc61f69f237847699d1fadcaa2";

    private readonly JsonPayloadSerializer _serializer = CreateSerializer();

    [Fact]
    public void EqualGraphs_WithDifferentDictionaryOrder_SerializeByteIdentically()
    {
        var forward = _serializer.Serialize(BuildFixture(reversed: false));
        var reversed = _serializer.Serialize(BuildFixture(reversed: true));

        Assert.Equal(forward, reversed);
    }

    [Fact]
    public void Serialization_IsRepeatable_ForTheSameGraph()
    {
        var fixture = BuildFixture(reversed: false);

        Assert.Equal(_serializer.Serialize(fixture), _serializer.Serialize(fixture));
    }

    [Fact]
    public void CanonicalDigest_IsStable()
    {
        var json = _serializer.Serialize(BuildFixture(reversed: false));
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();

        Assert.Equal(CanonicalDigest, digest);
    }

    [Fact]
    public void PolymorphicDiscriminator_IsFixedAndLast()
    {
        // A typed value boxed as object is wrapped with a trailing "_type" discriminator; its position must
        // be deterministic (FR-002). Two equal-but-differently-ordered maps must agree byte-for-byte.
        var a = new Dictionary<string, object?> { ["b"] = "2", ["a"] = "1", ["map"] = Reverse(new Dictionary<string, string> { ["y"] = "1", ["x"] = "2" }) };
        var b = new Dictionary<string, object?> { ["a"] = "1", ["map"] = new Dictionary<string, string> { ["x"] = "2", ["y"] = "1" }, ["b"] = "2" };

        var json = _serializer.Serialize(a);

        Assert.Equal(json, _serializer.Serialize(b));
        Assert.EndsWith("\"}}", json);                              // the wrapped map ends with its _type
        Assert.Equal(1, CountOccurrences(json, "\"_type\""));       // exactly one discriminator, fixed placement
    }

    [Fact]
    public void TypedDictionary_RoundTripsLosslessly_ThroughTheSortingConverter()
    {
        // The sorting rewrites byte order only, never semantics (FR-004): a typed dictionary serialized in
        // sorted-key order deserializes back to an equal map.
        var original = Reverse(new Dictionary<string, string> { ["x"] = "1", ["y"] = "2", ["z"] = "3" });
        var json = _serializer.Serialize(original);
        var roundTripped = (Dictionary<string, string>)_serializer.Deserialize(json, typeof(Dictionary<string, string>));

        Assert.Equal(original.OrderBy(e => e.Key), roundTripped.OrderBy(e => e.Key));
    }

    [Fact]
    public void ObjectGraph_RoundTripsLosslessly()
    {
        IDictionary<string, object?> nestedInput = new System.Dynamic.ExpandoObject();
        nestedInput["y"] = "2";
        nestedInput["x"] = "1";
        IDictionary<string, object?> graph = new Dictionary<string, object?>
        {
            ["zebra"] = "z",
            ["alpha"] = "a",
            ["count"] = 42,
            ["flag"] = true,
            ["nested"] = nestedInput,
        };

        var json = _serializer.Serialize(graph);
        var roundTripped = Assert.IsAssignableFrom<IDictionary<string, object>>(_serializer.Deserialize<object>(json));

        Assert.Equal("a", roundTripped["alpha"]);
        Assert.Equal(42L, roundTripped["count"]);
        Assert.Equal(true, roundTripped["flag"]);
        var nested = Assert.IsAssignableFrom<IDictionary<string, object>>(roundTripped["nested"]);
        Assert.Equal("1", nested["x"]);
        Assert.Equal("2", nested["y"]);
    }

    [Fact]
    public void EmbeddedOpaqueJson_IsPreservedVerbatim_NotReordered()
    {
        // ADR 0035 D3: opaque embedded JSON (e.g. ActivityNode.Structure.Payload, a JsonElement) is stored
        // verbatim and NEVER rewritten — a JsonElement re-emits in parse order with no hashing, so it is
        // already byte-stable across processes; reordering it would mutate the author's bytes. This pins that
        // decision so a future change can't silently re-introduce embedded-JSON canonicalization.
        var payload = JsonSerializer.Deserialize<JsonElement>("""{"z":1,"a":{"y":2,"x":3}}""");

        var json = _serializer.Serialize(new OpaqueJsonHolder(payload));

        Assert.Contains("\"payload\":{\"z\":1,\"a\":{\"y\":2,\"x\":3}}", json); // verbatim, NOT sorted
    }

    private sealed record OpaqueJsonHolder(JsonElement Payload);

    /// <summary>
    /// Builds the same logical graph either forward or reversed. The two must serialize identically: every
    /// dictionary is filled in the opposite insertion order, so any enumeration-order dependence surfaces.
    /// </summary>
    private static Dictionary<string, object?> BuildFixture(bool reversed)
    {
        var nested = new Dictionary<string, string> { ["x"] = "1", ["y"] = "2", ["z"] = "3" };
        var typed = new Dictionary<string, int> { ["one"] = 1, ["two"] = 2, ["three"] = 3 };

        var root = new Dictionary<string, object?>
        {
            ["zebra"] = "z",
            ["alpha"] = "a",
            ["count"] = 42,
            ["flag"] = true,
            ["absent"] = null,
            ["nested"] = reversed ? Reverse(nested) : nested,
            ["typed"] = reversed ? Reverse(typed) : typed,
        };

        return reversed ? Reverse(root) : root;
    }

    // Rebuilds a dictionary with keys inserted in reverse order, so its enumeration order differs from the original.
    private static Dictionary<string, TValue> Reverse<TValue>(Dictionary<string, TValue> source)
    {
        var reversed = new Dictionary<string, TValue>();
        foreach (var entry in source.Reverse())
            reversed[entry.Key] = entry.Value;
        return reversed;
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        for (var index = haystack.IndexOf(needle, StringComparison.Ordinal); index >= 0; index = haystack.IndexOf(needle, index + needle.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    [Theory]
    [InlineData(typeof(DictionaryHolder))]
    [InlineData(typeof(IDictionaryHolder))]
    [InlineData(typeof(IReadOnlyDictionaryHolder))]
    public void StringKeyedDictionaryShapes_SortAndRoundTrip(Type holderType)
    {
        // The sorting factory must handle every string-keyed dictionary shape it claims — Dictionary<>,
        // IDictionary<>, IReadOnlyDictionary<> — not just the concrete Dictionary<> (guards the STJ
        // converter-compatibility crash where the worker's declared type didn't match IDictionary<>).
        var forward = MakeHolder(holderType, [("b", "2"), ("a", "1"), ("c", "3")]);
        var reversed = MakeHolder(holderType, [("c", "3"), ("a", "1"), ("b", "2")]);

        var json = _serializer.Serialize(forward);
        Assert.Equal(json, _serializer.Serialize(reversed));                       // byte-identical regardless of order
        Assert.Contains("\"map\":{\"a\":\"1\",\"b\":\"2\",\"c\":\"3\"}", json);   // sorted
        Assert.NotNull(_serializer.Deserialize(json, holderType));                 // and round-trips (no crash)
    }

    private static object MakeHolder(Type holderType, (string Key, string Value)[] entries)
    {
        var map = new Dictionary<string, string>();
        foreach (var (key, value) in entries)
            map[key] = value;

        if (holderType == typeof(DictionaryHolder)) return new DictionaryHolder(map);
        if (holderType == typeof(IDictionaryHolder)) return new IDictionaryHolder(map);
        return new IReadOnlyDictionaryHolder(map);
    }

    private sealed record DictionaryHolder(Dictionary<string, string> Map);

    private sealed record IDictionaryHolder(IDictionary<string, string> Map);

    private sealed record IReadOnlyDictionaryHolder(IReadOnlyDictionary<string, string> Map);

    private static JsonPayloadSerializer CreateSerializer()
    {
        var wellKnownTypeRegistry = new WellKnownTypeRegistry();
        var registry = new JsonPayloadConverterRegistry();
        registry.RegisterAll(new JsonConverter[]
        {
            new JsonStringEnumConverter(),
            new PolymorphicObjectConverterFactory(wellKnownTypeRegistry),
            new TypeJsonConverter(wellKnownTypeRegistry),
        });

        return new JsonPayloadSerializer(registry);
    }
}
