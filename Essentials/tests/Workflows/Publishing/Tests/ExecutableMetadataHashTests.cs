using System.Text.Json;
using Elsa.Workflows.Publishing.Core.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executables;
using Xunit;

namespace Elsa.Workflows.Publishing.Tests;

public sealed class ExecutableMetadataHashTests
{
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

    private static ExecutableNode Node(IReadOnlyDictionary<string, string> metadata, string payload = "{}") =>
        new("node", "authored", "test.activity", "1", "test", Json(payload), new Dictionary<string, RuntimeInputBinding>(), metadata);

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
