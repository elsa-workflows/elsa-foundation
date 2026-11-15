using Elsa.Activities.Testing;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Workflows.Runtime.Activities;
using Elsa.Rules.Workflows.Runtime.Models;
using Elsa.Rules.Workflows.Runtime.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executables;

namespace Elsa.Rules.Tests;

public sealed class RulesNativeArtifactIdentityTests
{
    [Fact]
    public async Task Native_hash_is_preserved_through_state_address_fingerprint_and_completion_proof()
    {
        var contract = ClrActivityContractTestBuilder.BuildContract(typeof(RunRules));
        var node = new ExecutableNode("rules", "authored-rules", RunRules.ActivityType, "1.0.0",
            contract.DescriptorKind, contract.DescriptorPayload,
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(), activityContract: contract);
        var hash = new WorkflowExecutableHasher().ComputeHash(node);
        Assert.StartsWith("sha256:", hash);
        var fixture = new RulesNativeDeliveryTestFixture(hash);
        var state = new RunRulesState(1, RulesNativeBoundary.Write(fixture.Request));
        Assert.Equal(hash, RulesNativeBoundary.ReadState(state).ExecutableHash);
        var address = RulesNativeBoundary.Read<RuleNativeResultAddress>(RulesNativeBoundary.Write(fixture.Address));
        Assert.Equal(hash, address.ExecutableHash);
        Assert.Equal(fixture.Address, address);

        // Removing the namespace changes the native destination, even when the digest is identical.
        var stripped = new RulesNativeDeliveryTestFixture(hash["sha256:".Length..]);
        Assert.NotEqual(RuleRecordCodec.Fingerprint(fixture.Invocation), RuleRecordCodec.Fingerprint(stripped.Invocation));
        Assert.Throws<InvalidDataException>(() => RulesNativeBoundary.EnsureAddress(
            fixture.Request, stripped.Address, stripped.Address.RegistrationId));
        Assert.Null((await fixture.InspectAsync()).Proof);
        fixture.SetCompleted(retirePrivateState: true, retireReceiptPayload: true);
        Assert.Equal(RuleDeliveryProofKind.AlreadyConsumed, (await fixture.InspectAsync()).Proof?.Kind);
        Assert.Throws<ArgumentException>(() => new RuleInvocationFingerprint(hash));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Missing_native_identity_is_rejected_by_address_and_state(string? hash) => AssertInvalidIdentity(hash!);

    [Fact]
    public void Native_identity_retains_the_shared_utf8_bound()
    {
        // Native identity syntax is owned by its producer; Rules only bounds the retained value.
        var atLimit = new string('é', RuleLimits.MaxIdentifierUtf8Bytes / 2);
        var fixture = new RulesNativeDeliveryTestFixture(atLimit);
        Assert.Equal(atLimit, RulesNativeBoundary.ReadState(new RunRulesState(1, RulesNativeBoundary.Write(fixture.Request))).ExecutableHash);
        AssertInvalidIdentity(atLimit + "a");
    }

    private static void AssertInvalidIdentity(string hash)
    {
        Assert.ThrowsAny<ArgumentException>(() => new RulesNativeDeliveryTestFixture(hash));
        var request = new RulesNativeDeliveryTestFixture().Request with { ExecutableHash = hash };
        Assert.ThrowsAny<ArgumentException>(() => RulesNativeBoundary.ReadState(new RunRulesState(1, RulesNativeBoundary.Write(request))));
    }
}
