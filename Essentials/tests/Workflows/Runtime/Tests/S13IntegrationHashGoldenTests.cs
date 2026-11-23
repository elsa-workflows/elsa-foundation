using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executables;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class S13IntegrationHashGoldenTests
{
    public static IEnumerable<object[]> Vectors()
    {
        using var stream = typeof(S13IntegrationHashGoldenTests).Assembly.GetManifestResourceStream("s13-integration-hash-vectors.json")!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.EnumerateArray().Select(item => new object[] { item.Clone() }).ToArray();
    }

    [Theory]
    [MemberData(nameof(Vectors))]
    public void Actual_checkpoint_contract_node_and_full_artifact_consumers_match_independent_pins(JsonElement vector)
    {
        var family = vector.GetProperty("family").GetString();
        if (family == "capability")
        {
            var mask = vector.GetProperty("mask").GetInt32();
            var contract = new ActivityContract("test.activity", "1", "test", Json("{\"x\":1.0}"), [],
                new ActivityResultContract(new ValueTypeDescriptor("Int32"), false, ActivityValuePolicy.Default, []), [],
                new ActivityActivationRequirement("test", "execute"), SideEffectProfile.External,
                (mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0);
            var node = new ExecutableNode("node", "authored", "test.activity", "1", "test", Json("{\"x\":1.0}"),
                new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(), activityContract: contract);
            var hasher = new WorkflowExecutableHasher();
            Pin("sha256:" + vector.GetProperty("contract").GetProperty("sha256").GetString(), contract.SchemaFingerprint);
            Pin("sha256:" + vector.GetProperty("nodeSha256").GetString(), hasher.ComputeHash(node));
            Pin("sha256:" + vector.GetProperty("sha256").GetString(), hasher.ComputeHash(node, new(1, []), []));
        }
        else if (family == "flag")
        {
            var metadata = new Dictionary<string, string>();
            if (vector.GetProperty("value").GetString() is { } flag) metadata[vector.GetProperty("key").GetString()!] = flag;
            var node = new ExecutableNode("node", "authored", "test.activity", "1", "test", Json("{\"x\":1.0}"),
                new Dictionary<string, RuntimeInputBinding>(), metadata);
            var hasher = new WorkflowExecutableHasher();
            Pin("sha256:" + vector.GetProperty("nodeSha256").GetString(), hasher.ComputeHash(node));
            Pin("sha256:" + vector.GetProperty("sha256").GetString(), hasher.ComputeHash(node, new(1, []), []));
        }
        else if (family == "activity-metadata")
        {
            var metadata = new Dictionary<string, string> { ["identity\ud800"] = "value\ud801" };
            if (vector.GetProperty("payload").GetString() is { } raw) metadata[vector.GetProperty("key").GetString()!] = raw;
            var state = new ActivityExecutionState(new("activity", "workflow", "node", "authored", "test.activity", "1"),
                ActivityExecutionStatus.Completed, null, 1, DateTimeOffset.UnixEpoch, null, null, null, null, null, null,
                ActivitySchedulingProvenance.Empty, null, [], [], 0, 0, metadata);
            var type = typeof(WorkflowExecutableHasher).Assembly.GetType("Elsa.Workflows.Runtime.Services.Alterations.WorkflowAlterationCapturedConcurrencyValidator", throwOnError: true)!;
            var capture = type.GetMethod("CaptureActivities")!.CreateDelegate<Func<IReadOnlyCollection<ActivityExecutionState>, IReadOnlyDictionary<string, string>>>();
            Pin(vector.GetProperty("revision").GetProperty("sha256").GetString(), capture([state])["activity"]);
            var commit = new RuntimeCheckpointCommit("commit", new("checkpoint", "test", "workflow", DateTimeOffset.UnixEpoch, [], new Dictionary<string, string>()),
                new(null, null, [new("activity", RuntimeStateChangeOperation.Upsert, state, new Dictionary<string, string>())], [], [], [], []), [], new Dictionary<string, string>());
            Pin(vector.GetProperty("sha256").GetString(), RuntimeCheckpointCommitFingerprint.Compute(commit));
        }
        else
        {
            var name = vector.GetProperty("name").GetString()!;
            var raw = vector.GetProperty("payload").GetString();
            var value = new DurableValueState("durable", "workflow", "output:Value", new("Any", "Single", null),
                name == "none" ? DurableValueLifecycle.None : DurableValueLifecycle.Instance,
                name == "none" ? DurableValueStorage.None : name == "external" ? DurableValueStorage.External : DurableValueStorage.Inline,
                raw is null ? null : Json(raw),
                name == "external" ? new("store", "locator", new Dictionary<string, string> { ["region"] = "west" }) : null,
                null, DateTimeOffset.UnixEpoch, name.StartsWith("opaque-", StringComparison.Ordinal)
                    ? new Dictionary<string, string> { ["tenant\ud800"] = name == "opaque-a" ? "value\ud800" : "value\ud801" } : new Dictionary<string, string>());
            var commit = new RuntimeCheckpointCommit("commit", new("checkpoint", "test", "workflow", DateTimeOffset.UnixEpoch, [], new Dictionary<string, string>()),
                new(null, null, [], [], [new("durable", RuntimeStateChangeOperation.Upsert, value, new Dictionary<string, string>())], [], []),
                [], new Dictionary<string, string>());
            Pin(vector.GetProperty("sha256").GetString(), RuntimeCheckpointCommitFingerprint.Compute(commit));
        }
    }

    private static void Pin(string? expected, string actual) => Assert.True(expected == actual,
        $"First-stable hash freeze: an identity change requires an explicit migration decision. Expected={expected}; actual={actual}");

    private static JsonElement Json(string raw)
    {
        using var document = JsonDocument.Parse(raw);
        return document.RootElement.Clone();
    }
}
