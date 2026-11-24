using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeCheckpointHashGoldenTests
{
    internal static void AssertEmptyCommitPin(RuntimeCheckpointCommit commit)
    {
        var vector = (JsonElement)Vectors().Single(v => ((JsonElement)v[0]).GetProperty("family").GetString() == "empty-commit")[0];
        Assert.True(RuntimeCheckpointCommitFingerprint.Compute(commit) == vector.GetProperty("sha256").GetString(),
            "First-stable hash freeze: changes require an explicit identity/migration decision.");
    }
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
        var rich = vector.GetProperty("name").GetString() == "wait-delay";
        var at = new DateTimeOffset(2026, 10, 26, 0, 0, 0, TimeSpan.Zero);
        var actual = vector.GetProperty("family").GetString() == "empty-commit"
            ? RuntimeCheckpointCommitFingerprint.Compute(new("commit", new("checkpoint", "name", "waiter", at, [], new Dictionary<string, string>()),
                new(null, null, [], [], [], [], []), [], new Dictionary<string, string>()))
            : vector.GetProperty("family").GetString() == "intent"
            ? RuntimeCheckpointCommitFingerprint.ComputeIntent(new("send", "publisher", "publish",
                rich ? at.ToOffset(TimeSpan.FromHours(2)) : at, "activity", "key", document?.RootElement,
                rich ? new Dictionary<string, string> { ["z"] = "last", ["a"] = "first" } : new Dictionary<string, string> { ["routing"] = "saved" },
                dependsOnWaitRegistrationId: rich ? "wait" : null,
                waitFailurePolicy: rich ? RuntimeWaitDependentIntentFailurePolicy.KeepWaitingManualIntervention : null,
                notBefore: rich ? at.AddMinutes(1).ToOffset(TimeSpan.FromHours(2)) : null))
            : RuntimeCheckpointCommitFingerprint.ComputeModelInput(new(new("artifact", "definition", "version", "1.0.0", "hash"),
                "incarnation", "input", "start", document!.RootElement,
                new("system", "initiator", new Dictionary<string, string> { ["tenant"] = "tenant-a" }), "tenant-a"));
        Assert.True(actual == vector.GetProperty("sha256").GetString(),
            vector.GetProperty("family").GetString() + "/" + vector.GetProperty("name").GetString() +
            ": First-stable hash freeze: changes require an explicit identity/migration decision.");
    }
}
