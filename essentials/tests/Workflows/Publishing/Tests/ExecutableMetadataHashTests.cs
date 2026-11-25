using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Publishing.Core.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executables;
using Xunit;

namespace Elsa.Workflows.Publishing.Tests;

public sealed class ExecutableMetadataHashTests
{
    public static IEnumerable<object[]> Vectors()
    {
        using var stream = typeof(ExecutableMetadataHashTests).Assembly.GetManifestResourceStream("s13-integration-hash-vectors.json")!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.EnumerateArray().Where(row => row.GetProperty("family").GetString()!.StartsWith("template-", StringComparison.Ordinal))
            .Select(row => new object[] { row.Clone() }).ToArray();
    }

    [Theory]
    [MemberData(nameof(Vectors))]
    public void Actual_template_consumer_pins_independent_owner_preimages(JsonElement vector)
    {
        ExecutableNode root;
        if (vector.GetProperty("family").GetString() == "template-capability")
        {
            var mask = vector.GetProperty("mask").GetInt32();
            var contract = new ActivityContract("test.activity", "1", "test", Json("{\"x\":1.0}"), [],
                new ActivityResultContract(new ValueTypeDescriptor("Int32"), false, ActivityValuePolicy.Default, []), [],
                new ActivityActivationRequirement("test", "execute"), SideEffectProfile.External,
                (mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0);
            root = Node(new Dictionary<string, string>(), "{\"x\":1.0}", contract);
        }
        else if (vector.GetProperty("family").GetString() == "template-data-string")
        {
            var payload = new { metadata = new { workflowStateEffects = vector.GetProperty("payload").GetString() } };
            root = Node(new Dictionary<string, string>(), JsonSerializer.Serialize(payload));
        }
        else
        {
            var metadata = new Dictionary<string, string>();
            if (vector.GetProperty("payload").GetString() is { } raw) metadata[vector.GetProperty("key").GetString()!] = raw;
            root = Node(metadata);
            if (vector.GetProperty("scope").GetString() == "child") root = Parent(root);
        }
        var expected = "sha256:" + vector.GetProperty("sha256").GetString();
        var actual = Template(root);
        Assert.True(expected == actual, $"First-stable hash freeze: changes require an explicit identity/migration decision. Expected={expected}; actual={actual}");
    }

    [Theory]
    [InlineData("workflowStateEffects", "{\"writesInstanceName\":true,\"outputNames\":[\"Ren\\u00e9\"]}", "{ \"outputNames\":[\"René\"],\"writesInstanceName\":true }", true)]
    [InlineData("workflowStateEffects", "{\"accessesState\":false}", "{\"accessesState\":true}", false)]
    [InlineData("workflowStateEffects", "{}", "{\"future\":null}", false)]
    [InlineData("workflowStateEffects", "null", "{}", false)]
    [InlineData("runtime.mayWriteVariables", "[{\"key\":\"x\",\"name\":\"X\"}]", "[ {\"name\":\"X\", \"key\":\"x\"} ]", true)]
    [InlineData("runtime.mayWriteVariables", "[1,2]", "[2,1]", false)]
    public void Actual_template_node_and_full_artifact_hashes_bind_owner_metadata_values(string key, string first, string second, bool equal)
    {
        var before = Node(new Dictionary<string, string> { [key] = first });
        var after = Node(new Dictionary<string, string> { [key] = second });
        Assert.Equal(equal, Template(before) == Template(after));
        var hasher = new WorkflowExecutableHasher();
        Assert.Equal(equal, hasher.ComputeHash(before) == hasher.ComputeHash(after));
        Assert.Equal(equal, hasher.ComputeHash(before, new(1, []), []) == hasher.ComputeHash(after, new(1, []), []));
        Assert.Equal(first, before.Metadata[key]);
        Assert.Equal(equal, Template(Parent(before)) == Template(Parent(after)));
        Assert.NotEqual(Template(Node(new Dictionary<string, string>())), Template(before));
    }

    [Fact]
    public void Template_does_not_reinterpret_ordinary_metadata_or_json_looking_user_strings()
    {
        Assert.NotEqual(Template(Node(new Dictionary<string, string> { ["ordinary"] = "{}" })), Template(Node(new Dictionary<string, string> { ["ordinary"] = "{ }" })));
        var before = Node(new Dictionary<string, string>(), """{"metadata":{"workflowStateEffects":"{}"}}""");
        var after = Node(new Dictionary<string, string>(), """{"metadata":{"workflowStateEffects":"{ }"}}""");
        Assert.NotEqual(Template(before), Template(after));
        Assert.Equal("{}", before.DescriptorPayload.GetProperty("metadata").GetProperty("workflowStateEffects").GetString());
    }

    [Theory]
    [InlineData("workflowStateEffects", "sensitive-broken-json")]
    [InlineData("runtime.mayWriteVariables", "{\"sensitive\":\"\\ud800\"}")]
    public void Malformed_known_metadata_is_refused_in_parent_and_child_without_content_disclosure(string key, string raw)
    {
        var node = Node(new Dictionary<string, string> { [key] = raw });
        var failure = Assert.Throws<JsonException>(() => Template(Parent(node)));
        Assert.DoesNotContain("sensitive", failure.ToString(), StringComparison.Ordinal);
        var hasher = new WorkflowExecutableHasher();
        Assert.Throws<JsonException>(() => hasher.ComputeHash(node));
        Assert.Throws<JsonException>(() => hasher.ComputeHash(node, new(1, []), []));
        Assert.Equal(raw, node.Metadata[key]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Literal_unpaired_UTF16_in_known_metadata_is_not_repaired_by_the_template_serializer(bool key)
    {
        var raw = key ? "{\"\ud800\":\"sensitive\"}" : "{\"sensitive\":\"\ud800\"}";
        foreach (var metadataKey in new[] { "workflowStateEffects", "runtime.mayWriteVariables" })
        {
            var node = Node(new Dictionary<string, string> { [metadataKey] = raw });
            Assert.Throws<JsonException>(() => Template(node));
            Assert.Throws<JsonException>(() => Template(Parent(node)));
            var hasher = new WorkflowExecutableHasher();
            Assert.Throws<JsonException>(() => hasher.ComputeHash(node));
            Assert.Throws<JsonException>(() => hasher.ComputeHash(node, new(1, []), []));
            Assert.Equal(raw, node.Metadata[metadataKey]);
        }
    }

    [Theory]
    [InlineData("accessesWorkflowState")]
    [InlineData("runtime.mayWriteEveryVisibleVariable")]
    public void Ordinary_capability_flags_are_bound_by_both_executable_paths(string key)
    {
        var nodes = new[] { Node(new Dictionary<string, string>()), Node(new Dictionary<string, string> { [key] = "false" }), Node(new Dictionary<string, string> { [key] = "true" }) };
        var hasher = new WorkflowExecutableHasher();
        Assert.Equal(3, nodes.Select(hasher.ComputeHash).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(3, nodes.Select(node => hasher.ComputeHash(node, new(1, []), [])).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(3, nodes.Select(Template).Distinct(StringComparer.Ordinal).Count());
    }

    private static ExecutableNode Node(IReadOnlyDictionary<string, string> metadata, string payload = "{}", ActivityContract? contract = null) =>
        new("node", "authored", "test.activity", "1", "test", Json(payload), new Dictionary<string, RuntimeInputBinding>(), metadata, activityContract: contract);

    private static ExecutableNode Parent(ExecutableNode child) =>
        new("parent", "parent", "test.container", "1", "test", Json("{}"), new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(),
            childSlots: [new("body", [child])]);

    private static string Template(ExecutableNode node) => ExecutableActivityTemplateBehaviorHasher.Compute(node,
        new Dictionary<string, WorkflowExecutableResumeTarget>(), [], [], [], [], "provider", new Dictionary<string, string>());

    private static JsonElement Json(string raw)
    {
        using var document = JsonDocument.Parse(raw);
        return document.RootElement.Clone();
    }
}
