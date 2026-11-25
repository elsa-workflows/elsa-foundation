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
            ["artifact"] = "6f3cc824bf14f66d4382156c428600ffd0a35f6f5eb9476ee3baa0ff4b6a60c8", ["invocation"] = "b9e6df9b47292fe3e3c67297867560b1a14943dfb163f4fa5375f44c2f361ef3", ["facts"] = "b36609539e3ba5950e02efce70c8931722da4bcc1950c49f8554924260de3fdc", ["pin"] = "875ee5a48250b9051291d122976c4190596816113abc010609d104f4a506b99c",
            ["receipt"] = "3d9dd1bc7684ee29aebb400649f0ba07ff0dc67fe31b41a8945985a455905917", ["delivery"] = "bccb760bb1623008a1879bb465efb2789eb0f40f49fed54f396778e016625431", ["destination"] = "2140eca0611f05f31c6d77f86e779aea76d67847d10bb799a0f98bc9b7aa03b9"
        };
        Assert.True(expected.All(item => actual[item.Key] == item.Value),
            "First-stable hash freeze: changes require an explicit identity/migration decision. Actual: " + JsonSerializer.Serialize(actual));
        Assert.NotEqual(RuleRecordCodec.Hash(invocation.Artifact.CanonicalBytes.Span), actual["artifact"]);
        Assert.NotEqual(RuleRecordCodec.Hash(RuleRecordCodec.InvocationBytes(invocation)), actual["invocation"]);
        Assert.NotEqual(RuleRecordCodec.Hash(RuleRecordCodec.ReceiptBytes(receipt)), actual["receipt"]);
        Assert.NotEqual(RuleRecordCodec.Hash(RuleRecordCodec.DeliveryBytes("delivery", destination, terminal)), actual["delivery"]);
    }
}
