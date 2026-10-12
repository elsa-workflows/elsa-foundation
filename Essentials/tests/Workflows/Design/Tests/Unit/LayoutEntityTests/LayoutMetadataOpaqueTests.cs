using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.JsonConverters;
using Elsa.Serialization.SystemText.Services;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit.LayoutEntityTests;

/// <summary>
/// Spec 088 / ADR 0035 D3 (extended beyond StateSource): a layout record's
/// <see cref="DesignMetadataRecord.AdditionalProperties"/> is opaque, Studio-authored metadata held as a
/// verbatim <see cref="JsonElement"/>. It MUST survive serialization byte-for-byte in the author's key order —
/// never key-sorted/canonicalized, never round-tripped through a CLR <c>Dictionary&lt;string,object&gt;</c> —
/// so the layout bytes are stored as-received and stay safe to hash/diff. These are the regression guards.
/// </summary>
public sealed class LayoutMetadataOpaqueTests
{
    // A bag whose keys are deliberately NOT in sorted order — "z","a","m". Alphabetical sorting would reorder
    // it to "a","m","z"; verbatim preservation keeps it as authored.
    private const string UnsortedBagJson = """{"z":1,"a":2,"m":3,"nested":{"y":true,"x":false}}""";

    // The deterministic payload serializer (spec 086) — the one that sorts every string-keyed dictionary,
    // including object-valued ones. A JsonElement is the only shape it leaves verbatim, which is exactly why
    // the opaque bag is stored as a JsonElement rather than a Dictionary.
    private readonly JsonPayloadSerializer _serializer = CreateSerializer();

    private static DesignMetadataRecord RecordWith(string bagJson) =>
        new("node-1", 10, 20, AdditionalProperties: JsonSerializer.Deserialize<JsonElement>(bagJson));

    [Fact]
    public void AdditionalProperties_IsPreservedVerbatim_NotKeySorted()
    {
        var json = _serializer.Serialize(RecordWith(UnsortedBagJson));

        // The bag re-emits in the author's order, nested object included — NOT canonicalized to sorted keys.
        Assert.Contains("""{"z":1,"a":2,"m":3,"nested":{"y":true,"x":false}}""", json);
    }

    [Fact]
    public void AdditionalProperties_RoundTripsByteIdentically()
    {
        var original = RecordWith(UnsortedBagJson);

        var roundTripped = _serializer.Deserialize<DesignMetadataRecord>(_serializer.Serialize(original));

        Assert.NotNull(roundTripped.AdditionalProperties);
        // Raw text is identical (including key order) after a full serialize → deserialize cycle.
        Assert.Equal(UnsortedBagJson, roundTripped.AdditionalProperties!.Value.GetRawText());
    }

    [Fact]
    public void Serialization_IsDeterministic_ForEqualBags()
    {
        // Same record serializes byte-identically every time, and two records built from the same bag agree —
        // determinism holds without the bag being reordered (FR: deterministic + verbatim are not in tension).
        var first = _serializer.Serialize(RecordWith(UnsortedBagJson));
        var second = _serializer.Serialize(RecordWith(UnsortedBagJson));

        Assert.Equal(first, second);
    }

    [Fact]
    public void ContrastGuard_ADictionaryBagWouldHaveBeenSorted_ByTheSameSerializer()
    {
        // Documents *why* the bag must be a JsonElement: routed through the same deterministic serializer, an
        // equivalent CLR dictionary IS key-sorted to "a","m","z". Storing the bag opaquely is what avoids
        // mutating the author's bytes (ADR 0035 D3).
        var dictionaryBag = new Dictionary<string, object?> { ["z"] = 1, ["a"] = 2, ["m"] = 3 };

        var json = _serializer.Serialize(dictionaryBag);

        Assert.True(
            json.IndexOf("\"a\"", StringComparison.Ordinal) < json.IndexOf("\"z\"", StringComparison.Ordinal),
            $"Expected the dictionary to be key-sorted, but was: {json}");
    }

    private static JsonPayloadSerializer CreateSerializer()
    {
        var registry = new JsonPayloadConverterRegistry();
        registry.RegisterAll(new JsonConverter[] { new JsonStringEnumConverter() });
        return new JsonPayloadSerializer(registry);
    }
}
