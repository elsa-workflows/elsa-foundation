using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeCheckpointHashGoldenTests
{
    public static IEnumerable<object[]> Vectors()
    {
        using var source = typeof(RuntimeCheckpointHashGoldenTests).Assembly.GetManifestResourceStream("checkpoint-hash-vectors.json")!;
        using var document = JsonDocument.Parse(source);
        return document.RootElement.EnumerateArray().Select(item => new object[] { item.Clone() }).ToArray();
    }

    [Theory]
    [MemberData(nameof(Vectors))]
    public void Checkpoint_input_and_intent_hashes_pin_independent_preimages(JsonElement vector)
    {
        var raw = vector.GetProperty("payload").GetString();
        using var document = raw is null ? null : JsonDocument.Parse(raw);
        var actual = vector.GetProperty("family").GetString() == "intent"
            ? RuntimeCheckpointCommitFingerprint.ComputeIntent(new("send", "publisher", "publish",
                new DateTimeOffset(2026, 10, 26, 0, 0, 0, TimeSpan.Zero), "activity", "key", document?.RootElement,
                new Dictionary<string, string> { ["routing"] = "saved" }))
            : RuntimeCheckpointCommitFingerprint.ComputeModelInput(new(new("artifact", "definition", "version", "1.0.0", "hash"),
                "incarnation", "input", "start", document!.RootElement,
                new("system", "initiator", new Dictionary<string, string> { ["tenant"] = "tenant-a" }), "tenant-a"));
        Assert.True(actual == vector.GetProperty("sha256").GetString(),
            vector.GetProperty("family").GetString() + "/" + vector.GetProperty("name").GetString() +
            ": First-stable hash freeze: changes require an explicit identity/migration decision.");
    }
}
