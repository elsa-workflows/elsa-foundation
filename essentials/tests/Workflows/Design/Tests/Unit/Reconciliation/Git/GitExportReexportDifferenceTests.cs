using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Serialization.Testing;
using Elsa.Workflows.Design.Reconciliation.Git.Services;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit.Reconciliation.Git;

/// <summary>
/// FR-022a / US4 scenario 5: an unchanged definition re-exported after S-9 differs from its earlier export only by
/// written null members, carried data text kept as received, and relaxed encoding of Elsa's own strings. Hashed with
/// <c>JsonHashInput</c>, only the null members move the content hash.
/// </summary>
public sealed class GitExportReexportDifferenceTests
{
    [Fact]
    public void An_unchanged_definition_differs_only_in_the_three_intended_ways_and_only_nulls_move_its_hash()
    {
        var state = GitTestSupport.AuthoredStateFixture;
        state = state with { Variables = [state.Variables.Single() with { Name = "René" }] };
        var current = GitCanonicalJson.Indent(GitCanonicalJson.ToCompact(state, SerializationServices.PayloadSerializer()));

        var earlier = EarlierExport(current, dropNullMembers: true);
        var earlierWithNulls = EarlierExport(current, dropNullMembers: false);

        // 1. Null members are written now; the earlier export omitted them.
        var nullMembers = NullMemberPaths(JsonNode.Parse(current)!, "$").ToArray();
        Assert.NotEmpty(nullMembers);
        Assert.Empty(NullMemberPaths(JsonNode.Parse(earlier)!, "$"));
        // 2. Carried data keeps the text it was received with; the earlier export re-escaped it.
        Assert.Contains(GitTestSupport.AuthoredRawFixtureValue, current, StringComparison.Ordinal);
        Assert.DoesNotContain(GitTestSupport.AuthoredRawFixtureValue, earlier, StringComparison.Ordinal);
        // 3. Elsa's own strings use the relaxed encoder.
        Assert.Contains("\"René\"", current, StringComparison.Ordinal);
        Assert.Contains("\"Ren\\u00E9\"", earlier, StringComparison.Ordinal);

        // Only the null members move the hash: the re-encoded, re-escaped rendering hashes like the current file.
        Assert.Equal(GitCanonicalJson.HashFile(current), GitCanonicalJson.HashFile(earlierWithNulls));
        Assert.NotEqual(GitCanonicalJson.HashFile(current), GitCanonicalJson.HashFile(earlier));
    }

    // The pre-S-9 exporter re-emitted the whole file through JsonNode with the default (escaping) encoder and
    // omitted null members of Elsa's own model. Carried data values ("value") kept their nulls.
    private static string EarlierExport(string current, bool dropNullMembers)
    {
        var node = JsonNode.Parse(current)!;
        if (dropNullMembers) DropNullMembers(node);
        return node.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.Default });
    }

    private static void DropNullMembers(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var name in obj.Where(pair => pair.Value is null).Select(pair => pair.Key).ToArray()) obj.Remove(name);
                foreach (var (name, child) in obj) if (name != "value") DropNullMembers(child);
                break;
            case JsonArray array:
                foreach (var child in array) DropNullMembers(child);
                break;
        }
    }

    private static IEnumerable<string> NullMemberPaths(JsonNode? node, string path) => node switch
    {
        JsonObject obj => obj.SelectMany(pair => pair.Value is null ? [path + "." + pair.Key]
            : pair.Key == "value" ? [] : NullMemberPaths(pair.Value, path + "." + pair.Key)),
        JsonArray array => array.SelectMany((child, index) => NullMemberPaths(child, $"{path}[{index}]")),
        _ => []
    };
}
