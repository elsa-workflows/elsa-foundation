using System.Text;
using System.Text.Json;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Runtime;
using static Elsa.Rules.Tests.RuleTestData;

namespace Elsa.Rules.Tests;

public sealed class RulesJsonHashBaselineTests
{
    [Theory]
    [InlineData("{\"n\":1.0,\"text\":\"\\u0061\"}", "{\"text\":\"a\",\"n\":1e0}", true)]
    [InlineData("{}", "{\"nil\":null}", false)]
    [InlineData("[1,2]", "[2,1]", false)]
    [InlineData("9007199254740993", "9007199254740992", false)]
    public void Json_hashes_exact_values_while_opaque_artifacts_keep_physical_integrity(string left, string right, bool equal)
    {
        var a = Encoding.UTF8.GetBytes(left);
        var b = Encoding.UTF8.GetBytes(right);
        Assert.Equal(equal, RuleRecordCodec.JsonHash(a) == RuleRecordCodec.JsonHash(b));
        Assert.Equal(RuleRecordCodec.JsonHash(a), RuleRecordCodec.ArtifactHash(1, a));
        Assert.Equal(RuleRecordCodec.Hash(a), RuleRecordCodec.ArtifactHash(99, a));
        Assert.NotEqual(RuleRecordCodec.ArtifactHash(99, a), RuleRecordCodec.ArtifactHash(99, b));
    }

    [Fact]
    public void Json_families_keep_frozen_golden_identities_and_do_not_use_former_text_hashes()
    {
        var invocation = Invocation();
        var receipt = new RuleRoundEvaluator().Evaluate(Input(Witness(), Facts("seed")));
        var facts = Facts("left", "seed");
        var terminal = new RuleTerminalResult("terminal", RuleTerminalKind.Succeeded, 1, 1, facts, RuleRecordCodec.FactsHash(facts), null);
        var destination = Destination();
        var actual = new Dictionary<string, string>
        {
            ["artifact"] = invocation.Artifact.Sha256,
            ["invocation"] = RuleRecordCodec.Fingerprint(invocation).Sha256,
            ["facts"] = RuleRecordCodec.FactsHash(facts),
            ["pin"] = RuleRecordCodec.PinHash(invocation.ExpectedImplementation),
            ["receipt"] = receipt.SemanticReceiptSha256,
            ["delivery"] = RuleRecordCodec.JsonHash(RuleRecordCodec.DeliveryBytes("delivery", destination, terminal)),
            ["destination"] = RuleRecordCodec.DestinationIdentity(destination)
        };
        var expected = new Dictionary<string, string>
        {
            ["artifact"] = "pending", ["invocation"] = "pending", ["facts"] = "pending", ["pin"] = "pending",
            ["receipt"] = "pending", ["delivery"] = "pending", ["destination"] = "pending"
        };
        Assert.True(expected.All(item => actual[item.Key] == item.Value),
            "First-stable hash freeze: changes require an explicit identity/migration decision. Actual: " + JsonSerializer.Serialize(actual));
        Assert.NotEqual(RuleRecordCodec.Hash(invocation.Artifact.CanonicalBytes.Span), actual["artifact"]);
        Assert.NotEqual(RuleRecordCodec.Hash(RuleRecordCodec.InvocationBytes(invocation)), actual["invocation"]);
        Assert.NotEqual(RuleRecordCodec.Hash(RuleRecordCodec.FactsBytes(facts)), actual["facts"]);
        Assert.NotEqual(RuleRecordCodec.Hash(RuleRecordCodec.ReceiptBytes(receipt)), actual["receipt"]);
        Assert.NotEqual(RuleRecordCodec.Hash(RuleRecordCodec.DeliveryBytes("delivery", destination, terminal)), actual["delivery"]);
    }
}
