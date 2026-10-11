using Elsa.Workflows.Design.Reconciliation.Git.Services;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit.Reconciliation.Git;

/// <summary>
/// The canonicalizer must make the on-disk (indented) file a pure-whitespace rendering of the compact
/// canonical form, so <c>hash(compact(indent(x))) == hash(x)</c> and reviewable diffs never change the
/// content identity (ADR 0034 D3, spec 085 R4).
/// </summary>
public sealed class GitCanonicalJsonTests
{
    [Fact]
    public void Indent_then_compact_round_trips_to_the_same_hash()
    {
        const string compact = "{\"name\":\"Send Email\",\"variables\":[],\"nested\":{\"a\":1,\"b\":\"x y z\"}}";

        var indented = GitCanonicalJson.Indent(compact);
        var recovered = GitCanonicalJson.CompactFromFile(indented);

        Assert.Contains("\n", indented); // actually indented
        Assert.Equal(compact, recovered);
        Assert.Equal(GitCanonicalJson.Sha256Hex(compact), GitCanonicalJson.HashFile(indented));
    }

    [Fact]
    public void Whitespace_inside_string_values_is_preserved()
    {
        // A naive byte-level whitespace strip would corrupt "Send Email" -> "SendEmail"; the structural
        // canonicalizer must preserve it.
        const string compact = "{\"displayName\":\"Send   Email\"}";

        var recovered = GitCanonicalJson.CompactFromFile(GitCanonicalJson.Indent(compact));

        Assert.Contains("Send   Email", recovered);
        Assert.Equal(compact, recovered);
    }
}
