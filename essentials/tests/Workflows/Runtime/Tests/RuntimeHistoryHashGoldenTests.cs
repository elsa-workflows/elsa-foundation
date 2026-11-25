using System.Buffers.Binary;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeHistoryHashGoldenTests
{
    public static IEnumerable<object[]> Vectors()
    {
        using var source = typeof(RuntimeHistoryHashGoldenTests).Assembly.GetManifestResourceStream("history-hash-vectors.json")!;
        using var document = JsonDocument.Parse(source);
        return document.RootElement.EnumerateArray().Select(item => new object[] { item.Clone() }).ToArray();
    }

    [Theory]
    [MemberData(nameof(Vectors))]
    public void Replay_identity_pins_independent_canonical_preimages(JsonElement vector)
    {
        var family = vector.GetProperty("family").GetString();
        var fields = vector.TryGetProperty("fields", out var encoded) ? encoded.EnumerateArray().Select(Decode).ToArray() : [];
        string actual;
        if (family == "id") actual = RuntimeHistoryIdentity.CreateId(fields[0]!, fields[1]!, fields[2]!);
        else if (family == "binding") actual = RuntimeHistoryIdentity.CreateBinding(fields);
        else
        {
            var raw = vector.TryGetProperty("payload", out var payload) ? payload.GetString() : null;
            using var document = raw is null ? null : JsonDocument.Parse(raw);
            var candidate = family == "candidate";
            var e = Event(document?.RootElement, candidate,
                vector.TryGetProperty("ancestry", out var ancestry) ? ancestry.Deserialize<bool?>(Elsa.Serialization.Core.JsonBaseline.Options) : null,
                vector.TryGetProperty("occurrence", out var occurrence) ? occurrence.Deserialize<bool?>(Elsa.Serialization.Core.JsonBaseline.Options) : null);
            actual = candidate
                ? RuntimeHistoryIdentity.Compute(new RuntimeHistoryCandidate(e, 0, new(
                    new(new("bookmark", "run", "activity", "resume"), "Signal", "hash", DateTimeOffset.UnixEpoch, null),
                    Enum.Parse<RuntimeHistoryBookmarkStatus>(vector.GetProperty("status").GetString()!), "selected")))
                : RuntimeHistoryIdentity.Compute(e, 0);
        }
        var expected = (family == "id" ? "history:" : "") + vector.GetProperty("sha256").GetString();
        Assert.True(actual == expected, family + "/" + vector.GetProperty("name").GetString() +
            ": First-stable hash freeze: changes require an explicit identity/migration decision.");
    }

    [Fact]
    public void Replay_payloads_are_canonical_json_with_distinct_missing_null_large_numbers_and_array_order()
    {
        static string Hash(string? raw)
        {
            using var document = raw is null ? null : JsonDocument.Parse(raw);
            return RuntimeHistoryIdentity.Compute(Event(document?.RootElement), 0);
        }
        Assert.Equal(Hash("{\"n\":1.0,\"OwnerKey\":\"\\u0061\"}"), Hash("{\"OwnerKey\":\"a\",\"n\":1e0}"));
        Assert.NotEqual(Hash(null), Hash("null"));
        Assert.NotEqual(Hash("{\"n\":9007199254740993}"), Hash("{\"n\":9007199254740992}"));
        Assert.NotEqual(Hash("[1,2]"), Hash("[2,1]"));
        Assert.NotEqual(Hash("{\"OwnerKey\":1}"), Hash("{\"ownerKey\":1}"));
    }

    private static RuntimeHistoryEvent Event(JsonElement? payload, bool bookmark = false, bool? ancestry = null, bool? occurrence = null) =>
        new("event", "run", 0, bookmark ? RuntimeHistoryEventCategory.BookmarkLifecycle : RuntimeHistoryEventCategory.ActivityLifecycle,
            bookmark ? "BookmarkConsumed" : "ActivityStarted", DateTimeOffset.UnixEpoch, "activity", "node", "checkpoint", payload: payload,
            metadata: new Dictionary<string, string> { ["OwnerKey"] = "René" })
        {
            Authority = RuntimeHistoryAuthority.CommittedTransition, BookmarkId = bookmark ? "bookmark" : null,
            Causation = new("cause"), ActivityAncestryKnown = ancestry, OccurredAtKnown = occurrence
        };

    private static string? Decode(JsonElement encoded)
    {
        if (encoded.ValueKind == JsonValueKind.Null) return null;
        var bytes = Convert.FromBase64String(encoded.GetString()!);
        var chars = new char[bytes.Length / 2];
        for (var i = 0; i < chars.Length; i++) chars[i] = (char)BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(i * 2, 2));
        return new string(chars);
    }
}
